using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record IoFatSupplementalEvidenceSummary(
    string Kind,
    string IedName,
    DateTimeOffset RecordedAtUtc,
    string Verdict,
    string DisplayText,
    string ObjectReference,
    string ObservedValue,
    string Quality,
    string AcquisitionSource,
    string ArtifactName,
    string ArtifactPath,
    string ArtifactSha256,
    long ArtifactBytes,
    string Reason)
{
    public bool Exists => !string.IsNullOrWhiteSpace(Kind);
}

/// <summary>
/// Device-level FAT evidence that is independent from individual ON/OFF test rows.
/// Entries use the existing tamper-evident JSONL journal so a COMTRADE or time-sync
/// result remains attached to the IED even when panel/test-point scope changes later.
/// </summary>
public static class IoFatSupplementalEvidenceService
{
    public const string ComtradeKind = "COMTRADE";
    public const string TimeSyncKind = "TIME_SYNC";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static SignalDefinition? FindTimeSyncSignal(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return device.Signals
            .Where(signal => !signal.IsControlSignal && !string.IsNullOrWhiteSpace(signal.ObjectReference))
            .Select(signal => new { Signal = signal, Score = TimeSyncScore(signal) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Signal.ObjectReference, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Signal)
            .FirstOrDefault();
    }

    public static bool EnsureTimeSyncSignalSelected(Iec61850MonitorDevice device)
    {
        var signal = FindTimeSyncSignal(device);
        if (signal == null || signal.IsSelected)
            return false;

        signal.IsSelected = true;
        return true;
    }

    public static IoFatSupplementalEvidenceSummary? CaptureTimeSync(
        IoTestWorkspacePersistence? storage,
        IoTestProject project,
        IoTestIedPlan ied,
        Iec61850MonitorDevice device)
    {
        if (storage == null)
            return null;

        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(ied);
        ArgumentNullException.ThrowIfNull(device);

        var explicitSignal = FindTimeSyncSignal(device);
        IoTestJournalEntry entry;
        if (explicitSignal != null)
        {
            var value = (explicitSignal.Value ?? string.Empty).Trim();
            var normalized = NormalizeBoolean(value);
            var verdict = normalized switch
            {
                true => "PASS",
                false => "FAIL",
                _ => IsUsableValue(value) ? "DETECTED" : "PENDING"
            };
            var reason = normalized switch
            {
                true => "IED exposes an explicit IEC 61850 synchronization status and reports synchronized.",
                false => "IED exposes an explicit IEC 61850 synchronization status and reports not synchronized.",
                _ when IsUsableValue(value) => "IED exposes an explicit synchronization object; preserve the raw vendor value for FAT evidence.",
                _ => "Synchronization object was discovered and armed for monitoring, but a live value has not arrived yet."
            };

            entry = BaseEntry(project, ied, "TIME_SYNC_EVIDENCE", TimeSyncKind) with
            {
                SignalName = string.IsNullOrWhiteSpace(explicitSignal.Name) ? "Time synchronization" : explicitSignal.Name,
                ObjectReference = explicitSignal.ObjectReference,
                ObservedValue = IsUsableValue(value) ? value : "No live value yet",
                NormalizedState = normalized,
                IedTimestamp = TryParseTimestamp(explicitSignal.DeviceTimestamp),
                Quality = explicitSignal.Quality,
                AcquisitionSource = string.IsNullOrWhiteSpace(explicitSignal.ReportPlan) ? "IEC 61850" : explicitSignal.ReportPlan,
                Verdict = verdict,
                Reason = reason
            };
        }
        else
        {
            var timestampPoint = device.Points
                .Select(point => new { Point = point, Parsed = TryParseTimestamp(point.DeviceTimestamp) })
                .Where(item => item.Parsed.HasValue)
                .OrderByDescending(item => item.Parsed)
                .FirstOrDefault();

            if (timestampPoint == null)
            {
                entry = BaseEntry(project, ied, "TIME_SYNC_EVIDENCE", TimeSyncKind) with
                {
                    ObservedValue = "No explicit sync object or parseable IED timestamp",
                    Verdict = "REVIEW",
                    Reason = "This IED model does not expose a recognized synchronization status. Capture relay clock evidence manually or verify the configured time source."
                };
            }
            else
            {
                var delta = (DateTimeOffset.UtcNow - timestampPoint.Parsed!.Value.ToUniversalTime()).Duration();
                entry = BaseEntry(project, ied, "TIME_SYNC_EVIDENCE", TimeSyncKind) with
                {
                    SignalName = timestampPoint.Point.SignalName,
                    ObjectReference = timestampPoint.Point.IecReference,
                    ObservedValue = $"IED timestamp observed; UTC delta {delta.TotalSeconds:F3} s",
                    IedTimestamp = timestampPoint.Parsed,
                    Quality = timestampPoint.Point.Quality,
                    AcquisitionSource = timestampPoint.Point.SourceMode,
                    Verdict = "OBSERVED",
                    Reason = "No explicit sync-status object was found; the freshest live IED timestamp is preserved as fallback evidence without claiming synchronization state."
                };
            }
        }

        var previous = ReadLatest(storage, ied.IedName, TimeSyncKind);
        if (previous != null && EquivalentTimeSync(previous, entry))
            return previous;

        Append(storage, project, ied, entry);
        return ToSummary(entry);
    }

    public static IoFatSupplementalEvidenceSummary? CaptureComtrade(
        IoTestWorkspacePersistence? storage,
        IoTestProject project,
        IoTestIedPlan ied,
        string recordName,
        string localDirectory)
    {
        if (storage == null || string.IsNullOrWhiteSpace(localDirectory) || !Directory.Exists(localDirectory))
            return null;

        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(ied);

        string[] files;
        try
        {
            files = Directory.EnumerateFiles(localDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => !Path.GetFileName(path).StartsWith('.', StringComparison.Ordinal))
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        if (files.Length == 0)
            return null;

        var extensions = files
            .Select(path => Path.GetExtension(path).ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasCfg = extensions.Contains(".cfg");
        var hasData = extensions.Contains(".dat") || extensions.Contains(".data");
        var hasPackage = extensions.Contains(".zip") || extensions.Contains(".cff");
        var completePair = hasCfg && hasData;
        var verdict = completePair ? "PASS" : hasPackage ? "DETECTED" : "REVIEW";
        var bytes = files.Sum(SafeLength);
        var manifestHash = HashManifest(files);
        var displayName = string.IsNullOrWhiteSpace(recordName)
            ? Path.GetFileName(localDirectory)
            : recordName.Trim();

        var entry = BaseEntry(project, ied, "COMTRADE_EVIDENCE", ComtradeKind) with
        {
            SignalName = "Fault record",
            ObservedValue = completePair
                ? $"COMTRADE CFG + DAT detected ({files.Length} file(s))"
                : $"Fault-record files detected ({files.Length} file(s))",
            AcquisitionSource = "IEC 61850 file services",
            Verdict = verdict,
            Reason = completePair
                ? "Downloaded local files contain a COMTRADE configuration/data pair and are registered as IED-level FAT evidence."
                : "Downloaded fault-record files are registered as FAT evidence, but a classic CFG + DAT pair was not both present; preserve the vendor package for review.",
            ArtifactName = displayName,
            ArtifactPath = Path.GetFullPath(localDirectory),
            ArtifactSha256 = manifestHash,
            ArtifactBytes = bytes
        };

        var existing = ReadLatest(storage, ied.IedName, ComtradeKind, manifestHash);
        if (existing != null)
            return existing;

        Append(storage, project, ied, entry);
        return ToSummary(entry);
    }

    public static IoFatSupplementalEvidenceSummary? ReadLatest(
        IoTestWorkspacePersistence? storage,
        string iedName,
        string kind,
        string? artifactSha256 = null)
    {
        if (storage == null || string.IsNullOrWhiteSpace(iedName) || string.IsNullOrWhiteSpace(kind) ||
            !Directory.Exists(storage.EvidenceProjectDirectory))
        {
            return null;
        }

        IoTestJournalEntry? latest = null;
        foreach (var path in SafeEvidenceFiles(storage.EvidenceProjectDirectory))
        {
            foreach (var entry in ReadEntries(path))
            {
                if (!entry.IedName.Equals(iedName, StringComparison.OrdinalIgnoreCase) ||
                    !entry.EvidenceKind.Equals(kind, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(artifactSha256) &&
                    !entry.ArtifactSha256.Equals(artifactSha256, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (latest == null || entry.RecordedAtUtc > latest.RecordedAtUtc)
                    latest = entry;
            }
        }

        return latest == null ? null : ToSummary(latest);
    }

    public static int Count(
        IoTestWorkspacePersistence? storage,
        string iedName,
        string kind)
    {
        if (storage == null || !Directory.Exists(storage.EvidenceProjectDirectory))
            return 0;

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in SafeEvidenceFiles(storage.EvidenceProjectDirectory))
        {
            foreach (var entry in ReadEntries(path))
            {
                if (!entry.IedName.Equals(iedName, StringComparison.OrdinalIgnoreCase) ||
                    !entry.EvidenceKind.Equals(kind, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var key = string.IsNullOrWhiteSpace(entry.ArtifactSha256)
                    ? $"{entry.EventType}|{entry.ObjectReference}|{entry.ObservedValue}|{entry.Verdict}"
                    : entry.ArtifactSha256;
                keys.Add(key);
            }
        }
        return keys.Count;
    }

    private static void Append(
        IoTestWorkspacePersistence storage,
        IoTestProject project,
        IoTestIedPlan ied,
        IoTestJournalEntry entry)
    {
        var evidenceRoot = Directory.GetParent(storage.EvidenceProjectDirectory)?.FullName
                           ?? storage.EvidenceProjectDirectory;
        using var journal = IoTestEvidenceJournal.Create(
            evidenceRoot,
            project,
            ied,
            entry.SessionId,
            entry.RecordedAtUtc);
        journal.Append(entry);
    }

    private static IReadOnlyList<string> SafeEvidenceFiles(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.evidence.jsonl", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<IoTestJournalEntry> ReadEntries(string path)
    {
        var entries = new List<IoTestJournalEntry>();
        IoTestJournalVerificationResult verification;
        try
        {
            verification = IoTestEvidenceJournal.Verify(path);
        }
        catch
        {
            return entries;
        }

        if (!verification.IsValid)
            return entries;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path, Encoding.UTF8);
        }
        catch
        {
            return entries;
        }

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var envelope = JsonSerializer.Deserialize<IoTestJournalEnvelope>(line, JsonOptions);
                if (envelope?.Entry != null)
                    entries.Add(envelope.Entry);
            }
            catch (JsonException)
            {
                // Verification already protects the chain. Ignore a line here only so a
                // damaged supplemental display cannot crash the FAT workspace.
            }
        }

        return entries;
    }

    private static IoTestJournalEntry BaseEntry(
        IoTestProject project,
        IoTestIedPlan ied,
        string eventType,
        string evidenceKind)
        => new()
        {
            EventType = eventType,
            RecordedAtUtc = DateTimeOffset.UtcNow,
            ProjectId = project.ProjectId,
            SessionId = Guid.NewGuid(),
            IedName = ied.IedName,
            IpAddress = ied.IpAddress,
            SourceWorkbookName = project.SourceWorkbookName,
            SourceWorkbookSha256 = project.SourceWorkbookSha256,
            ApplicationVersion = typeof(IoFatSupplementalEvidenceService).Assembly.GetName().Version?.ToString() ?? string.Empty,
            Operator = Environment.UserName,
            Workstation = Environment.MachineName,
            EvidenceKind = evidenceKind
        };

    private static IoFatSupplementalEvidenceSummary ToSummary(IoTestJournalEntry entry)
    {
        var detail = entry.EvidenceKind.Equals(ComtradeKind, StringComparison.OrdinalIgnoreCase)
            ? $"{entry.Verdict} · {entry.ArtifactName} · {FormatBytes(entry.ArtifactBytes)}"
            : string.IsNullOrWhiteSpace(entry.ObjectReference)
                ? $"{entry.Verdict} · {entry.ObservedValue}"
                : $"{entry.Verdict} · {entry.ObservedValue} · {entry.ObjectReference}";

        return new IoFatSupplementalEvidenceSummary(
            entry.EvidenceKind,
            entry.IedName,
            entry.RecordedAtUtc,
            entry.Verdict,
            detail,
            entry.ObjectReference,
            entry.ObservedValue,
            entry.Quality,
            entry.AcquisitionSource,
            entry.ArtifactName,
            entry.ArtifactPath,
            entry.ArtifactSha256,
            entry.ArtifactBytes,
            entry.Reason);
    }

    private static bool EquivalentTimeSync(
        IoFatSupplementalEvidenceSummary previous,
        IoTestJournalEntry current)
        => previous.ObjectReference.Equals(current.ObjectReference, StringComparison.OrdinalIgnoreCase) &&
           previous.ObservedValue.Equals(current.ObservedValue, StringComparison.OrdinalIgnoreCase) &&
           previous.Verdict.Equals(current.Verdict, StringComparison.OrdinalIgnoreCase) &&
           previous.Quality.Equals(current.Quality, StringComparison.OrdinalIgnoreCase);

    private static int TimeSyncScore(SignalDefinition signal)
    {
        var reference = (signal.ObjectReference ?? string.Empty).Replace('$', '.');
        var name = signal.Name ?? string.Empty;
        var text = $"{reference} {name}";
        if (reference.Contains("SyncSt.stVal", StringComparison.OrdinalIgnoreCase)) return 100;
        if (reference.Contains("TimeSynchrnz", StringComparison.OrdinalIgnoreCase)) return 100;
        if (reference.Contains("TimeSync", StringComparison.OrdinalIgnoreCase)) return 95;
        if (text.Contains("clock sync", StringComparison.OrdinalIgnoreCase)) return 90;
        if (text.Contains("time sync", StringComparison.OrdinalIgnoreCase)) return 90;
        if (text.Contains("synchroniz", StringComparison.OrdinalIgnoreCase)) return 80;
        return 0;
    }

    private static bool? NormalizeBoolean(string value)
    {
        if (!IsUsableValue(value))
            return null;

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is "true" or "1" or "on" or "active" or "synchronized" or "synchronised" or "synced" or "ok")
            return true;
        if (normalized is "false" or "0" or "off" or "inactive" or "not synchronized" or "not synchronised" or "unsynchronized" or "unsynchronised")
            return false;
        return null;
    }

    private static bool IsUsableValue(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length > 0 && text != "-" && text != "—" &&
               !text.Equals("unknown", StringComparison.OrdinalIgnoreCase) &&
               !text.Contains("not probed", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? TryParseTimestamp(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (!IsUsableValue(text))
            return null;

        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string HashManifest(IEnumerable<string> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in files.OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(path).ToLowerInvariant()));
            using var stream = File.OpenRead(path);
            hash.AppendData(SHA256.HashData(stream));
            hash.AppendData(BitConverter.GetBytes(SafeLength(path)));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = Math.Max(0, bytes);
        var display = (double)value;
        var unit = 0;
        while (display >= 1024d && unit < units.Length - 1)
        {
            display /= 1024d;
            unit++;
        }
        return unit == 0 ? $"{value:N0} {units[unit]}" : $"{display:N1} {units[unit]}";
    }
}
