using AR.Iec61850.FaultRecords;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Converts a successful IEC 61850 FileDirectory fault-record listing into durable
/// IED-level FAT evidence. Download is deliberately not required: discovery proves
/// that the relay file service is reachable and exposes a supported fault record.
/// </summary>
public static class IoFatRemoteComtradeEvidenceService
{
    public const string EventType = "COMTRADE_REMOTE_EVIDENCE";
    public const string AcquisitionSource = "IEC 61850 FileDirectory";

    public static IoFatSupplementalEvidenceSummary? CaptureLatest(
        IoTestWorkspacePersistence? storage,
        IoTestProject project,
        IoTestIedPlan ied,
        IEnumerable<Iec61850FaultRecordSet> records)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(ied);
        ArgumentNullException.ThrowIfNull(records);

        var latest = records
            .Where(record => record.Files.Count > 0)
            .OrderByDescending(record => record.LastModifiedUtc ?? DateTimeOffset.MinValue)
            .ThenBy(record => record.RecordId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (latest == null)
            return null;

        var orderedFiles = latest.Files
            .OrderBy(FilePriority)
            .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var fileNames = string.Join(" + ", orderedFiles.Select(file => file.Name));
        var primaryPath = orderedFiles.FirstOrDefault()?.RemotePath ?? latest.RecordId;
        var capturedAt = DateTimeOffset.UtcNow;

        var alreadyCaptured =
            ied.HasRemoteComtradeEvidence &&
            ied.LatestComtradeFiles.Equals(fileNames, StringComparison.OrdinalIgnoreCase) &&
            ied.LatestComtradeRemotePath.Equals(primaryPath, StringComparison.OrdinalIgnoreCase) &&
            Nullable.Equals(ied.LatestComtradeModifiedAtUtc, latest.LastModifiedUtc) &&
            ied.LatestComtradeCompleteness.Equals(latest.Completeness, StringComparison.OrdinalIgnoreCase);

        ied.LatestComtradeFiles = fileNames;
        ied.LatestComtradeRemotePath = primaryPath;
        ied.LatestComtradeCompleteness = latest.Completeness;
        ied.LatestComtradeAcquisitionSource = AcquisitionSource;
        ied.LatestComtradeModifiedAtUtc = latest.LastModifiedUtc;
        ied.LatestComtradeCapturedAtUtc = capturedAt;
        ied.LatestComtradeFileCount = orderedFiles.Length;
        ied.LatestComtradeKnownSizeBytes = latest.KnownSizeBytes;

        var reason =
            "IED returned a supported COMTRADE/fault-record entry through IEC 61850 FileDirectory. " +
            "This is FAT evidence that the remote file-service browse path is available. " +
            "FileOpen/FileRead download is optional additional verification and is not claimed by this evidence.";

        var summary = new IoFatSupplementalEvidenceSummary(
            IoFatSupplementalEvidenceService.ComtradeKind,
            ied.IedName,
            capturedAt,
            "PASS",
            $"PASS · latest remote COMTRADE · {fileNames}",
            latest.RecordId,
            $"Remote COMTRADE listed: {fileNames}",
            "Listed",
            AcquisitionSource,
            fileNames,
            primaryPath,
            string.Empty,
            latest.KnownSizeBytes,
            reason);

        if (storage == null || alreadyCaptured)
            return summary;

        var entry = new IoTestJournalEntry
        {
            EventType = EventType,
            RecordedAtUtc = capturedAt,
            ProjectId = project.ProjectId,
            SessionId = Guid.NewGuid(),
            IedName = ied.IedName,
            IpAddress = ied.IpAddress,
            SourceWorkbookName = project.SourceWorkbookName,
            SourceWorkbookSha256 = project.SourceWorkbookSha256,
            ApplicationVersion = typeof(IoFatRemoteComtradeEvidenceService).Assembly.GetName().Version?.ToString() ?? string.Empty,
            Operator = Environment.UserName,
            Workstation = Environment.MachineName,
            ObjectReference = latest.RecordId,
            ObservedValue = $"Remote COMTRADE listed: {fileNames}",
            IedTimestamp = latest.LastModifiedUtc,
            Quality = "Listed",
            AcquisitionSource = AcquisitionSource,
            Verdict = "PASS",
            Reason = reason,
            EvidenceKind = IoFatSupplementalEvidenceService.ComtradeKind,
            ArtifactName = fileNames,
            ArtifactPath = primaryPath,
            ArtifactBytes = latest.KnownSizeBytes
        };

        var evidenceRoot = Directory.GetParent(storage.EvidenceProjectDirectory)?.FullName
                           ?? storage.EvidenceProjectDirectory;
        using var journal = IoTestEvidenceJournal.Create(
            evidenceRoot,
            project,
            ied,
            entry.SessionId,
            entry.RecordedAtUtc);
        journal.Append(entry);
        return summary;
    }

    private static int FilePriority(Iec61850FaultRecordFile file)
    {
        var extension = (file.Extension ?? string.Empty).Trim().ToLowerInvariant();
        return extension switch
        {
            ".cfg" => 0,
            ".dat" => 1,
            ".cff" => 2,
            ".hdr" => 3,
            ".inf" => 4,
            ".zip" => 5,
            _ => 10
        };
    }
}