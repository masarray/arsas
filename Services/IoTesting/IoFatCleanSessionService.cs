using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record IoFatCleanSessionResult(
    int ResetPointCount,
    int ArchivedJournalCount,
    string ArchiveDirectory);

/// <summary>
/// Creates a clean FAT evidence boundary without rewriting or deleting historical proof.
/// Existing hash-chained journals are verified, then moved below an archive subdirectory;
/// the active top-level evidence directory becomes empty so a new export cannot pick up
/// timestamps from the previous test run.
/// </summary>
public static class IoFatCleanSessionService
{
    public static IoFatCleanSessionResult ResetForRetest(
        IoTestWorkspacePersistence storage,
        IoTestProject project)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(project);

        var evidenceDirectory = storage.EvidenceProjectDirectory;
        var journals = Directory.Exists(evidenceDirectory)
            ? Directory.EnumerateFiles(evidenceDirectory, "*.evidence.jsonl", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();

        // Verify every old journal before moving anything. A clean retest must never hide
        // already-damaged evidence behind an archive operation.
        foreach (var path in journals)
        {
            var verification = IoTestEvidenceJournal.Verify(path);
            if (!verification.IsValid)
            {
                throw new InvalidDataException(
                    $"Cannot create a clean FAT session because evidence '{Path.GetFileName(path)}' failed integrity verification: {verification.Error}");
            }
        }

        var archiveDirectory = string.Empty;
        if (journals.Length > 0)
        {
            archiveDirectory = CreateArchiveDirectory(evidenceDirectory);
            Directory.CreateDirectory(archiveDirectory);
            foreach (var path in journals)
            {
                var destination = Path.Combine(archiveDirectory, Path.GetFileName(path));
                File.Move(path, destination, overwrite: false);
            }
        }

        var resetPointCount = 0;
        foreach (var ied in project.Ieds)
        {
            ClearIedEvidence(ied);
            foreach (var point in ied.TestPoints)
            {
                var runtime = point.Runtime;
                runtime.State = IoTestPointState.NotStarted;
                runtime.LastObservedState = null;
                runtime.LastSequence = -1;
                runtime.ConnectionGeneration = -1;
                runtime.OnEvidence = null;
                runtime.OffEvidence = null;
                runtime.StatusReason = "Clean retest · no evidence captured";
                runtime.Attempt = 0;
                runtime.CurrentValue = "-";
                runtime.CurrentQuality = "Unknown";
                runtime.CurrentSource = "Clean retest · awaiting live baseline";
                runtime.CurrentIedTimestamp = "—";

                // Selection belongs to the operator. A clean evidence reset may clear
                // runtime/evidence state, but it must not enable or disable a FAT row.
                resetPointCount++;
            }
        }

        storage.SaveNow();
        return new IoFatCleanSessionResult(resetPointCount, journals.Length, archiveDirectory);
    }

    private static string CreateArchiveDirectory(string evidenceDirectory)
    {
        var archiveRoot = Path.Combine(evidenceDirectory, "archive");
        var baseName = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
        var candidate = Path.Combine(archiveRoot, baseName + "_pre_retest");
        var suffix = 1;
        while (Directory.Exists(candidate))
            candidate = Path.Combine(archiveRoot, $"{baseName}_pre_retest_{suffix++}");
        return candidate;
    }

    private static void ClearIedEvidence(IoTestIedPlan ied)
    {
        ied.LatestComtradeFiles = string.Empty;
        ied.LatestComtradeRemotePath = string.Empty;
        ied.LatestComtradeCompleteness = string.Empty;
        ied.LatestComtradeAcquisitionSource = string.Empty;
        ied.LatestComtradeModifiedAtUtc = null;
        ied.LatestComtradeCapturedAtUtc = null;
        ied.LatestComtradeFileCount = 0;
        ied.LatestComtradeKnownSizeBytes = 0L;
    }
}
