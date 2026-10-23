using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Durable bridge for the Phase-D Close IED -> re-import lifecycle.
///
/// The normal project snapshot intentionally represents the current operator scope, so an
/// IED removed from that scope must not be reintroduced automatically on application reopen.
/// Before removal, this service copies only that IED's already-proven snapshot payload into
/// a separate evidence checkpoint. Re-import may restore evidence only when the physical IED
/// identity matches and each individual point still has the same evidence-critical
/// configuration fingerprint. Live binding/connection state is never restored.
/// </summary>
public static class IoFatClosedIedCheckpointService
{
    public const string CheckpointVersion = "ARSAS-IOFAT-CLOSED-IED-1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string SaveFromCurrentSnapshot(
        IoTestWorkspacePersistence storage,
        IoTestIedPlan ied)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(ied);

        if (!File.Exists(storage.SnapshotPath))
            throw new InvalidOperationException("The FAT project snapshot does not exist yet; save progress before closing this IED.");

        using var snapshot = JsonDocument.Parse(File.ReadAllBytes(storage.SnapshotPath));
        var root = snapshot.RootElement;
        var snapshotVersion = RequiredString(root, "snapshotVersion");
        if (!snapshotVersion.Equals(IoTestWorkspacePersistence.SnapshotVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported FAT snapshot version '{snapshotVersion}'.");

        var project = RequiredObject(root, "project");
        var wantedKey = IoTestPerIedProgressIdentity.IedKey(ied);
        var savedMatches = RequiredArray(project, "ieds")
            .EnumerateArray()
            .Where(candidate => IoTestPerIedProgressIdentity.IedKey(
                OptionalString(candidate, "iedName", string.Empty),
                OptionalString(candidate, "ipAddress", string.Empty))
                .Equals(wantedKey, StringComparison.Ordinal))
            .ToArray();
        if (savedMatches.Length != 1)
        {
            throw new InvalidDataException(
                $"Cannot checkpoint IED '{ied.IedName}': the saved project contains {savedMatches.Length} matching technical endpoint(s). Expected exactly one.");
        }

        var directory = CheckpointDirectory(storage);
        Directory.CreateDirectory(directory);
        var path = CheckpointPath(storage, wantedKey);
        var payload = new ClosedIedCheckpoint(
            CheckpointVersion,
            DateTimeOffset.UtcNow,
            wantedKey,
            IoTestPerIedProgressIdentity.IedConfigurationFingerprint(ied),
            savedMatches[0].Clone());
        WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        return path;
    }

    public static ClosedIedRestoreResult TryRestore(
        IoTestWorkspacePersistence? storage,
        IoTestIedPlan currentIed)
    {
        ArgumentNullException.ThrowIfNull(currentIed);
        if (storage == null)
            return ClosedIedRestoreResult.NotFound("No persistent FAT workspace is attached.");

        var iedKey = IoTestPerIedProgressIdentity.IedKey(currentIed);
        var path = CheckpointPath(storage, iedKey);
        if (!File.Exists(path))
            return ClosedIedRestoreResult.NotFound("No previous closed-IED evidence checkpoint exists for this endpoint.");

        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        var version = RequiredString(root, "checkpointVersion");
        if (!version.Equals(CheckpointVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported closed-IED checkpoint version '{version}'.");

        var savedKey = RequiredString(root, "iedKey");
        if (!savedKey.Equals(iedKey, StringComparison.Ordinal))
            throw new InvalidDataException("The closed-IED checkpoint belongs to a different technical endpoint.");

        var savedIed = RequiredObject(root, "ied");
        RestoreIedLevelEvidence(currentIed, savedIed);

        var savedPoints = RequiredArray(savedIed, "testPoints")
            .EnumerateArray()
            .GroupBy(point => RequiredString(point, "testPointId"), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);

        var restoredPoints = 0;
        foreach (var point in currentIed.TestPoints)
        {
            if (!savedPoints.TryGetValue(point.TestPointId, out var savedPoint))
                continue;
            if (!IoTestPerIedProgressIdentity.PointConfigurationMatches(point, savedPoint))
                continue;

            RestorePointProgress(point, savedPoint);
            restoredPoints++;
        }

        currentIed.InitializeRuntimeNotifications();
        var savedFingerprint = OptionalString(root, "iedConfigurationFingerprint", string.Empty);
        var currentFingerprint = IoTestPerIedProgressIdentity.IedConfigurationFingerprint(currentIed);
        return new ClosedIedRestoreResult(
            Found: true,
            RestoredPointCount: restoredPoints,
            SavedIedConfigurationFingerprint: savedFingerprint,
            CurrentIedConfigurationFingerprint: currentFingerprint,
            CheckpointPath: path,
            Message: restoredPoints == 0
                ? "A previous checkpoint was found, but no point had the same evidence-critical configuration. Historical evidence was left untouched."
                : $"Restored historical FAT evidence for {restoredPoints} matching point(s); live acquisition requires a fresh connection/baseline.");
    }

    internal static string CheckpointPathForTest(string evidenceProjectDirectory, string iedKey)
    {
        var safeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(iedKey))).ToLowerInvariant();
        return Path.Combine(evidenceProjectDirectory, "closed-ied-checkpoints", safeHash + ".json");
    }

    private static string CheckpointDirectory(IoTestWorkspacePersistence storage)
        => Path.Combine(storage.EvidenceProjectDirectory, "closed-ied-checkpoints");

    private static string CheckpointPath(IoTestWorkspacePersistence storage, string iedKey)
        => CheckpointPathForTest(storage.EvidenceProjectDirectory, iedKey);

    private static void RestorePointProgress(IoTestPointPlan point, JsonElement saved)
    {
        point.WorkspaceSelected = OptionalBool(saved, "workspaceSelected", true);
        point.TestEnabled = OptionalBool(saved, "testEnabled", true);
        point.RestoreFatDisposition(OptionalEnum(saved, "fatDisposition", FatSignalDisposition.Included));

        if (!saved.TryGetProperty("runtime", out var runtime) || runtime.ValueKind != JsonValueKind.Object)
            return;

        point.Runtime.Attempt = OptionalInt(runtime, "attempt", 0);
        point.Runtime.OnEvidence = OptionalEvidence(runtime, "onEvidence");
        point.Runtime.OffEvidence = OptionalEvidence(runtime, "offEvidence");
        point.Runtime.Value1Evidence = OptionalFatEvidence(runtime, "value1Evidence");
        point.Runtime.Value2Evidence = OptionalFatEvidence(runtime, "value2Evidence");

        // Runtime continuity is intentionally severed. Evidence survives; association,
        // sequence, generation and LIVE presentation must be proven again after re-import.
        point.Runtime.LastObservedState = null;
        point.Runtime.LastSequence = -1;
        point.Runtime.ConnectionGeneration = -1;
        point.Runtime.CurrentValue = "-";
        point.Runtime.CurrentQuality = "Unknown";
        point.Runtime.CurrentSource = "Restored after Close IED · fresh live baseline required";

        if (point.CaptureMode == FatCaptureMode.OperatorSnapshot)
        {
            point.Runtime.State = IoTestPointState.NotStarted;
            point.Runtime.StatusReason = point.IsFatEvidenceComplete
                ? "Value 1 and Value 2 evidence restored after Close IED; reconnect live acquisition to recapture either value."
                : point.Runtime.Value1Evidence is not null || point.Runtime.Value2Evidence is not null
                    ? "Partial Value 1 / Value 2 evidence restored after Close IED; reconnect live acquisition to capture the remaining value."
                    : "Historical scope restored; reconnect live acquisition before operator snapshot capture.";
            return;
        }

        var savedState = OptionalEnum(runtime, "state", IoTestPointState.NotStarted);
        if (savedState is IoTestPointState.Passed or IoTestPointState.Review or IoTestPointState.Failed)
        {
            point.Runtime.State = savedState;
            point.Runtime.StatusReason = OptionalString(runtime, "statusReason", "Restored completed result after Close IED");
        }
        else if (point.Runtime.OnEvidence != null && point.Runtime.OffEvidence == null)
        {
            point.Runtime.State = IoTestPointState.Review;
            point.Runtime.StatusReason = "Historical ON evidence was restored, but OFF continuity across Close IED cannot be proven.";
        }
        else
        {
            point.Runtime.State = IoTestPointState.NotStarted;
            point.Runtime.StatusReason = "Historical progress restored after Close IED; a new good-quality live baseline is required before continuing.";
        }
    }

    private static void RestoreIedLevelEvidence(IoTestIedPlan ied, JsonElement saved)
    {
        ied.LatestComtradeFiles = OptionalString(saved, "latestComtradeFiles", string.Empty);
        ied.LatestComtradeRemotePath = OptionalString(saved, "latestComtradeRemotePath", string.Empty);
        ied.LatestComtradeCompleteness = OptionalString(saved, "latestComtradeCompleteness", string.Empty);
        ied.LatestComtradeAcquisitionSource = OptionalString(saved, "latestComtradeAcquisitionSource", string.Empty);
        ied.LatestComtradeModifiedAtUtc = OptionalDateTimeOffset(saved, "latestComtradeModifiedAtUtc");
        ied.LatestComtradeCapturedAtUtc = OptionalDateTimeOffset(saved, "latestComtradeCapturedAtUtc");
        ied.LatestComtradeFileCount = OptionalInt(saved, "latestComtradeFileCount", 0);
        ied.LatestComtradeKnownSizeBytes = OptionalLong(saved, "latestComtradeKnownSizeBytes", 0L);
    }

    private static IoTestTransitionEvidence? OptionalEvidence(JsonElement runtime, string property)
    {
        if (!runtime.TryGetProperty(property, out var element) || element.ValueKind == JsonValueKind.Null)
            return null;
        return element.Deserialize<IoTestTransitionEvidence>(JsonOptions);
    }

    private static FatValueEvidence? OptionalFatEvidence(JsonElement runtime, string property)
    {
        if (!runtime.TryGetProperty(property, out var element) || element.ValueKind == JsonValueKind.Null)
            return null;
        return element.Deserialize<FatValueEvidence>(JsonOptions);
    }

    private static TEnum OptionalEnum<TEnum>(JsonElement parent, string property, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (!parent.TryGetProperty(property, out var value))
            return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && Enum.IsDefined(typeof(TEnum), number))
            return (TEnum)Enum.ToObject(typeof(TEnum), number);
        if (value.ValueKind == JsonValueKind.String && Enum.TryParse<TEnum>(value.GetString(), true, out var parsed))
            return parsed;
        return fallback;
    }

    private static JsonElement RequiredObject(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Checkpoint property '{property}' is missing or invalid.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Checkpoint property '{property}' is missing or invalid.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Checkpoint property '{property}' is missing or invalid.");
        return value.GetString() ?? string.Empty;
    }

    private static string OptionalString(JsonElement parent, string property, string fallback)
        => parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static bool OptionalBool(JsonElement parent, string property, bool fallback)
        => parent.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static int OptionalInt(JsonElement parent, string property, int fallback)
        => parent.TryGetProperty(property, out var value) && value.TryGetInt32(out var number)
            ? number
            : fallback;

    private static long OptionalLong(JsonElement parent, string property, long fallback)
        => parent.TryGetProperty(property, out var value) && value.TryGetInt64(out var number)
            ? number
            : fallback;

    private static DateTimeOffset? OptionalDateTimeOffset(JsonElement parent, string property)
        => parent.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.String &&
           value.TryGetDateTimeOffset(out var parsed)
            ? parsed
            : null;

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllBytes(temp, bytes);
        try
        {
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed record ClosedIedCheckpoint(
        string CheckpointVersion,
        DateTimeOffset SavedAtUtc,
        string IedKey,
        string IedConfigurationFingerprint,
        JsonElement Ied);
}

public sealed record ClosedIedRestoreResult(
    bool Found,
    int RestoredPointCount,
    string SavedIedConfigurationFingerprint,
    string CurrentIedConfigurationFingerprint,
    string CheckpointPath,
    string Message)
{
    public static ClosedIedRestoreResult NotFound(string message)
        => new(false, 0, string.Empty, string.Empty, string.Empty, message);
}
