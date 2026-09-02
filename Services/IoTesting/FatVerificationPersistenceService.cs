using System.Text.Json;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record FatVerificationSnapshot(
    string SchemaVersion,
    string ProjectId,
    string SourceSetSha256,
    DateTimeOffset SavedAtUtc,
    List<FatVerificationSignalSnapshot> Signals);

public sealed record FatVerificationSignalSnapshot(
    string SignalId,
    FatSignalDisposition Disposition,
    FatValueEvidence? Value1Evidence,
    FatValueEvidence? Value2Evidence);

public static class FatVerificationPersistenceService
{
    public const string SnapshotSchemaVersion = "fat-v2.1";
    public const string SnapshotFileName = "fat-v2.snapshot.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string SnapshotPath(FatSclWorkspaceLaunchResult launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        return Path.Combine(launch.WorkspaceDirectory, SnapshotFileName);
    }

    public static void SaveNow(FatSclWorkspaceLaunchResult launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        var path = SnapshotPath(launch);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var snapshot = CreateSnapshot(launch);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static bool RestoreIfPresent(FatSclWorkspaceLaunchResult launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        var path = SnapshotPath(launch);
        if (!File.Exists(path))
            return false;
        var bytes = File.ReadAllBytes(path);
        var snapshot = JsonSerializer.Deserialize<FatVerificationSnapshot>(bytes, JsonOptions)
            ?? throw new InvalidDataException("FAT v2 snapshot is invalid.");
        ApplySnapshot(launch, snapshot);
        return true;
    }

    public static FatVerificationSnapshot CreateSnapshot(FatSclWorkspaceLaunchResult launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        return new FatVerificationSnapshot(
            SnapshotSchemaVersion,
            launch.Project.ProjectId,
            launch.SourceSetSha256,
            DateTimeOffset.UtcNow,
            launch.Project.Signals
                .OrderBy(signal => signal.SignalId, StringComparer.Ordinal)
                .Select(signal => new FatVerificationSignalSnapshot(
                    signal.SignalId,
                    signal.Disposition,
                    signal.Value1Evidence,
                    signal.Value2Evidence))
                .ToList());
    }

    public static void ApplySnapshot(
        FatSclWorkspaceLaunchResult launch,
        FatVerificationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.SchemaVersion.Equals(SnapshotSchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported FAT v2 snapshot schema '{snapshot.SchemaVersion}'.");
        if (!snapshot.SourceSetSha256.Equals(launch.SourceSetSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("FAT v2 snapshot source-set identity does not match the opened SCL engineering source set.");

        var current = launch.Project.Signals.ToDictionary(signal => signal.SignalId, StringComparer.OrdinalIgnoreCase);
        if (snapshot.Signals.Count != current.Count)
            throw new InvalidDataException("FAT v2 snapshot signal count does not match the authoritative current DataSet projection.");

        foreach (var saved in snapshot.Signals)
        {
            if (!current.TryGetValue(saved.SignalId, out var signal))
                throw new InvalidDataException($"FAT v2 snapshot references unknown static membership '{saved.SignalId}'.");

            if (saved.Disposition == FatSignalDisposition.ExcludedByOperator)
                signal.RemoveFromFat();
            else
                signal.RestoreToFat();

            if (saved.Value1Evidence != null)
            {
                if (saved.Value1Evidence.Slot != FatValueSlot.Value1)
                    throw new InvalidDataException($"FAT v2 Value 1 evidence slot is invalid for '{saved.SignalId}'.");
                signal.SetCurrentEvidence(saved.Value1Evidence);
            }
            if (saved.Value2Evidence != null)
            {
                if (saved.Value2Evidence.Slot != FatValueSlot.Value2)
                    throw new InvalidDataException($"FAT v2 Value 2 evidence slot is invalid for '{saved.SignalId}'.");
                signal.SetCurrentEvidence(saved.Value2Evidence);
            }
        }
    }
}
