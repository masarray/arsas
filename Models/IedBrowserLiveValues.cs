using System.Collections.Concurrent;

namespace ArIED61850Tester.Models;

/// <summary>
/// Device-scoped process-value authority. Report ingress and explicit read-only MMS
/// reads publish to the same exact IEC reference/FC registry. It never constructs
/// values from a declaration, Report ID or guessed DataSet position.
/// </summary>
public sealed record IedBrowserObservedValue(
    string Value, string Quality, string DeviceTimestamp, string Source, DateTime ObservedUtc);

public sealed class IedBrowserLiveValues
{
    private readonly ConcurrentDictionary<string, IedBrowserObservedValue> _values =
        new(StringComparer.OrdinalIgnoreCase);
    private long _version;
    public long Version => Interlocked.Read(ref _version);

    public static string CanonicalReference(string? reference)
    {
        var input = (reference ?? "").Trim();
        var dollar = input.IndexOf('$');
        if (dollar < 0) return input.Replace('$', '.');
        var domainLn = input[..dollar];
        var segments = input[(dollar + 1)..].Split('$', StringSplitOptions.RemoveEmptyEntries);
        // Standard MMS variable name: LN$FC$DO$DA. Remove FC, never LD/LN.
        return segments.Length >= 2
            ? domainLn + "." + string.Join(".", segments.Skip(1))
            : input.Replace('$', '.');
    }

    private static string Key(string reference, string fc)
        => CanonicalReference(reference) + "|" + (fc ?? "").Trim().ToUpperInvariant();

    public void Publish(
        string reference, string fc, string? value, string? quality,
        string? timestamp, string source)
    {
        if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(value) ||
            value.Trim() is "-" or "Not read" or "Unknown")
            return;
        var observed = new IedBrowserObservedValue(value.Trim(), quality ?? "",
            timestamp ?? "", source, DateTime.UtcNow);
        _values[Key(reference, fc)] = observed;
        Interlocked.Increment(ref _version);
    }

    public void PublishReport(Iec61850MonitorPoint point, bool observedReportIngress = false)
    {
        if (point.IsOffline || point.Value == "-" ||
            !observedReportIngress &&
            !point.SourceMode.Contains("Report", StringComparison.OrdinalIgnoreCase) &&
            point.RoutedReportControlReferences.Count == 0)
            return;
        Publish(point.IecReference, point.FunctionalConstraint, point.Value,
            point.Quality, point.DeviceTimestamp, "Report");
        // Preserve real point q/t companion evidence, never fabricate sub-flags.
        if (point.Quality is not ("Unknown" or "-"))
            Publish(point.QualityReference, point.FunctionalConstraint, point.Quality,
                "", point.DeviceTimestamp, "Report quality");
        if (point.DeviceTimestamp is not ("Unknown" or "-"))
            Publish(point.TimestampReference, point.FunctionalConstraint, point.DeviceTimestamp,
                "", point.DeviceTimestamp, "Report timestamp");
    }

    public bool TryGet(string reference, string fc, out IedBrowserObservedValue evidence)
    {
        if (_values.TryGetValue(Key(reference, fc), out evidence!)) return true;
        // Canonical process points are frequently identified by their DO,
        // whereas a browser DA may be the exact child stVal. These aliases
        // are unambiguous IEC value companions, not a prefix-wide guess.
        var normalized = CanonicalReference(reference);
        foreach (var suffix in new[] { ".stVal", ".posVal", ".valWTr", ".general", ".mag.f",
                                      ".instMag.f", ".cVal.mag.f" })
        {
            if (_values.TryGetValue(Key(normalized + suffix, fc), out evidence!)) return true;
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                _values.TryGetValue(Key(normalized[..^suffix.Length], fc), out evidence!))
                return true;
        }
        evidence = null!;
        return false;
    }

    public void Clear()
    {
        _values.Clear();
        Interlocked.Increment(ref _version);
    }
}

public sealed record IedBrowserReadTarget(string Reference, string FunctionalConstraint, string DataType);
public sealed record IedBrowserReadResult(
    IedBrowserReadTarget Target, string Value, string Quality,
    string DeviceTimestamp, string Error);
public sealed record IedBrowserReadProgress(int Completed, int Total);
