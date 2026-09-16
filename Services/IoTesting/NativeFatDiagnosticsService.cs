using System.Globalization;
using AR.Iec61850.FaultRecords;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record NativeFatTimeSyncPointEvidence(
    string Role,
    string SignalName,
    string IecReference,
    string Value,
    string Quality,
    string DeviceTimestamp,
    double? DeltaSeconds,
    bool Trusted);

public sealed record NativeFatTimeSyncDiagnosticResult(
    bool IsSynchronized,
    string Verdict,
    string Summary,
    bool LtmsPresent,
    bool LtmsTrusted,
    int FreshPrimaryTimestampCount,
    bool ExplicitNegativeSyncStatus,
    IReadOnlyList<NativeFatTimeSyncPointEvidence> PrimaryEvidence,
    IReadOnlyList<NativeFatTimeSyncPointEvidence> SecondaryTelemetry);

/// <summary>
/// Read-only native FAT diagnostics over the canonical Engineering rows.
/// No network reads, polling changes, reconnects, or hidden acquisition are allowed here.
/// LTMS plus fresh IEC timestamp/quality evidence is authoritative. When LTMS is absent,
/// two independent fresh IEC timestamps are required as a conservative fallback.
/// Vendor sync flags (TimeSynchrnz/SyncSt/TimeSync) are secondary only: a positive flag
/// can never create an OK verdict, while an explicit negative flag fails closed.
/// </summary>
public static class NativeFatTimeSyncDiagnosticService
{
    public static readonly TimeSpan MaximumTrustedClockDelta = TimeSpan.FromSeconds(10);

    public static NativeFatTimeSyncDiagnosticResult Evaluate(
        Iec61850MonitorDevice device,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(device);
        nowUtc = nowUtc.ToUniversalTime();

        var primary = new List<NativeFatTimeSyncPointEvidence>();
        var secondary = new List<NativeFatTimeSyncPointEvidence>();
        var ltmsPresent = false;
        var ltmsTrusted = false;
        var freshPrimaryCount = 0;
        var explicitNegative = false;

        foreach (var point in device.Points)
        {
            var isLtms = IsLtms(point);
            var isSecondary = IsSecondaryTelemetry(point);
            var parsed = TryParseDeviceTimestamp(point.DeviceTimestamp);
            var delta = parsed.HasValue
                ? (nowUtc - parsed.Value.ToUniversalTime()).Duration()
                : (TimeSpan?)null;
            var goodQuality = Iec61850QualityPresentation.Classify(point.Quality) == Iec61850QualityPresentation.Good;
            var timestampTrusted = parsed.HasValue &&
                                   delta.HasValue &&
                                   goodQuality &&
                                   delta.Value <= MaximumTrustedClockDelta;

            if (isLtms)
            {
                ltmsPresent = true;
                var trusted = goodQuality && (timestampTrusted || IsUsable(point.Value));
                ltmsTrusted |= trusted;
                primary.Add(ToEvidence("LTMS", point, delta, trusted));
                continue;
            }

            if (isSecondary)
            {
                if (IsSyncStatusPoint(point) && NormalizeSyncBoolean(point.Value) == false)
                    explicitNegative = true;
                secondary.Add(ToEvidence("Secondary", point, delta, timestampTrusted));
                continue;
            }

            if (timestampTrusted)
            {
                freshPrimaryCount++;
                primary.Add(ToEvidence("IEC timestamp", point, delta, true));
            }
        }

        if (explicitNegative)
        {
            return new NativeFatTimeSyncDiagnosticResult(
                false,
                "NOT OK",
                "Device-side synchronization telemetry explicitly reports not synchronized.",
                ltmsPresent,
                ltmsTrusted,
                freshPrimaryCount,
                true,
                primary,
                secondary);
        }

        if (ltmsPresent)
        {
            if (ltmsTrusted && freshPrimaryCount >= 1)
            {
                return new NativeFatTimeSyncDiagnosticResult(
                    true,
                    "OK",
                    "LTMS evidence is present and cross-checked by a fresh good-quality IEC timestamp.",
                    true,
                    true,
                    freshPrimaryCount,
                    false,
                    primary,
                    secondary);
            }

            return new NativeFatTimeSyncDiagnosticResult(
                false,
                "REVIEW",
                ltmsTrusted
                    ? "LTMS is present, but no separate fresh good-quality IEC timestamp currently cross-checks it."
                    : "LTMS is present, but its live evidence is not currently trustworthy enough to prove synchronization.",
                true,
                ltmsTrusted,
                freshPrimaryCount,
                false,
                primary,
                secondary);
        }

        if (freshPrimaryCount >= 2)
        {
            return new NativeFatTimeSyncDiagnosticResult(
                true,
                "OK",
                "LTMS is not exposed; two or more independent fresh good-quality IEC timestamps agree with the ARSAS clock window.",
                false,
                false,
                freshPrimaryCount,
                false,
                primary,
                secondary);
        }

        return new NativeFatTimeSyncDiagnosticResult(
            false,
            "REVIEW",
            "Synchronization is not proven. LTMS is absent and fewer than two independent fresh good-quality IEC timestamps are available.",
            false,
            false,
            freshPrimaryCount,
            false,
            primary,
            secondary);
    }

    private static NativeFatTimeSyncPointEvidence ToEvidence(
        string role,
        Iec61850MonitorPoint point,
        TimeSpan? delta,
        bool trusted)
        => new(
            role,
            point.SignalName ?? string.Empty,
            point.IecReference ?? string.Empty,
            point.Value ?? string.Empty,
            point.Quality ?? string.Empty,
            point.DeviceTimestamp ?? string.Empty,
            delta?.TotalSeconds,
            trusted);

    private static bool IsLtms(Iec61850MonitorPoint point)
    {
        var reference = Normalize(point.IecReference);
        var signal = Normalize(point.SignalName);
        return reference.Contains("/ltms", StringComparison.Ordinal) ||
               reference.Contains(".ltms", StringComparison.Ordinal) ||
               reference.StartsWith("ltms", StringComparison.Ordinal) ||
               signal.Contains("ltms", StringComparison.Ordinal);
    }

    private static bool IsSecondaryTelemetry(Iec61850MonitorPoint point)
    {
        var text = $"{Normalize(point.IecReference)} {Normalize(point.SignalName)}";
        return text.Contains("timesynchrnz", StringComparison.Ordinal) ||
               text.Contains("syncst", StringComparison.Ordinal) ||
               text.Contains("time sync", StringComparison.Ordinal) ||
               text.Contains("timesync", StringComparison.Ordinal) ||
               text.Contains("server 1", StringComparison.Ordinal) ||
               text.Contains("server1", StringComparison.Ordinal) ||
               text.Contains("server 2", StringComparison.Ordinal) ||
               text.Contains("server2", StringComparison.Ordinal) ||
               text.Contains("current server", StringComparison.Ordinal) ||
               text.Contains("currentserver", StringComparison.Ordinal);
    }

    private static bool IsSyncStatusPoint(Iec61850MonitorPoint point)
    {
        var text = $"{Normalize(point.IecReference)} {Normalize(point.SignalName)}";
        return text.Contains("timesynchrnz", StringComparison.Ordinal) ||
               text.Contains("syncst", StringComparison.Ordinal) ||
               text.Contains("time sync", StringComparison.Ordinal) ||
               text.Contains("timesync", StringComparison.Ordinal);
    }

    private static bool? NormalizeSyncBoolean(string? value)
    {
        var text = Normalize(value);
        if (!IsUsable(text))
            return null;

        if (text is "true" or "1" or "1.0" or "on" or "active" or "synchronized" or "synchronised" or "synced" or "ok")
            return true;
        if (text is "false" or "0" or "0.0" or "off" or "inactive" or "not synchronized" or "not synchronised" or "unsynchronized" or "unsynchronised" or "not synced")
            return false;
        return null;
    }

    private static DateTimeOffset? TryParseDeviceTimestamp(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (!IsUsable(text))
            return null;

        if (DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return null;
    }

    private static bool IsUsable(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length > 0 && text != "-" && text != "—" &&
               !text.Equals("unknown", StringComparison.OrdinalIgnoreCase) &&
               !text.Equals("pending", StringComparison.OrdinalIgnoreCase) &&
               !text.Contains("not probed", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().Replace('$', '.').ToLowerInvariant();
}

/// <summary>
/// Counts only files actually returned by the IEC 61850 fault-record/FileDirectory catalog.
/// No synthetic record/file count is permitted on the native FAT surface.
/// </summary>
public static class NativeFatComtradeDiagnosticService
{
    public static int CountDetectedFiles(IEnumerable<Iec61850FaultRecordSet>? records)
    {
        if (records == null)
            return 0;

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            if (record?.Files == null)
                continue;

            foreach (var file in record.Files)
            {
                var key = !string.IsNullOrWhiteSpace(file.RemotePath)
                    ? file.RemotePath.Trim()
                    : file.Name?.Trim();
                if (!string.IsNullOrWhiteSpace(key))
                    files.Add(key);
            }
        }

        return files.Count;
    }
}
