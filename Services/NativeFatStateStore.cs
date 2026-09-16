using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Non-destructive, per-IED FAT persistence. The store never deletes unmatched signal
/// records during reconciliation; removed/changed engineering therefore remains visible
/// as historical commissioning evidence instead of becoming a load error or data loss.
/// </summary>
public sealed class NativeFatStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _rootDirectory;

    public NativeFatStateStore(string? rootDirectory = null)
    {
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ARSAS",
                "FAT",
                "NativeState")
            : rootDirectory;
    }

    public string RootDirectory => _rootDirectory;

    public async Task<NativeFatDeviceState> LoadAndReconcileAsync(
        Iec61850MonitorDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        var state = await LoadAsync(device, cancellationToken).ConfigureAwait(false);
        Reconcile(state, device);
        return state;
    }

    public async Task<NativeFatDeviceState> LoadAsync(
        Iec61850MonitorDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        Directory.CreateDirectory(_rootDirectory);

        // Runtime DeviceId is intentionally ephemeral. FAT commissioning evidence must
        // survive application restarts, so schema 2 keys storage by SCL identity when
        // available and otherwise by the configured MMS endpoint.
        var persistenceIdentity = BuildPersistenceIdentity(device);
        var preferredPath = GetPreferredPath(device.Name, persistenceIdentity);
        var preferredPathOccupied = File.Exists(preferredPath);
        var legacyPath = GetLegacyPath(device.Name);

        var paths = new List<string> { preferredPath, legacyPath };
        paths.AddRange(Directory.EnumerateFiles(_rootDirectory, "*.json", SearchOption.TopDirectoryOnly));

        var legacyNameMatches = new List<(string Path, NativeFatDeviceState State)>();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = await TryReadAsync(path, cancellationToken).ConfigureAwait(false);
            if (candidate == null)
                continue;

            if (IsForDevice(candidate, device, persistenceIdentity))
            {
                // Once a schema-2 file already carries the durable persistence identity,
                // keep its collision-safe path stable across harmless display-name changes.
                // Only schema-1/name-only evidence is migrated to the schema-2 preferred path.
                candidate.StoragePath = !string.IsNullOrWhiteSpace(candidate.PersistenceIdentity)
                    ? path
                    : SelectStoragePathForMigration(
                        path,
                        preferredPath,
                        preferredPathOccupied);
                Normalize(candidate, device, persistenceIdentity);
                return candidate;
            }

            // Schema-1 files have no stable persistence identity. DeviceId cannot be used
            // after restart, so a legacy name match is accepted only when it is unique.
            // Ambiguity is deliberately treated as a new state rather than guessing and
            // attaching one relay's commissioning evidence to another relay.
            if (string.IsNullOrWhiteSpace(candidate.PersistenceIdentity) &&
                candidate.IedName.Equals(device.Name, StringComparison.OrdinalIgnoreCase))
            {
                legacyNameMatches.Add((path, candidate));
            }
        }

        if (legacyNameMatches.Count == 1)
        {
            var legacy = legacyNameMatches[0];
            legacy.State.StoragePath = SelectStoragePathForMigration(
                legacy.Path,
                preferredPath,
                preferredPathOccupied);
            Normalize(legacy.State, device, persistenceIdentity);
            return legacy.State;
        }

        // An occupied preferred path that is unreadable or belongs to a different device
        // is evidence, not scratch space. Start a recovery state beside it; never overwrite
        // the original simply because deserialization or identity validation failed.
        var storagePath = preferredPathOccupied
            ? GetNonDestructiveRecoveryPath(preferredPath)
            : preferredPath;
        return new NativeFatDeviceState
        {
            SchemaVersion = 2,
            DeviceId = device.DeviceId,
            PersistenceIdentity = persistenceIdentity,
            IedName = device.Name,
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow,
            StoragePath = storagePath
        };
    }

    public async Task SaveAsync(NativeFatDeviceState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        Directory.CreateDirectory(_rootDirectory);
        state.UpdatedUtc = DateTimeOffset.UtcNow;
        state.SchemaVersion = Math.Max(2, state.SchemaVersion);

        var identity = string.IsNullOrWhiteSpace(state.PersistenceIdentity)
            ? BuildLegacyFallbackIdentity(state.DeviceId)
            : state.PersistenceIdentity;
        var path = string.IsNullOrWhiteSpace(state.StoragePath)
            ? GetPreferredPath(state.IedName, identity)
            : state.StoragePath;
        var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);
            state.StoragePath = path;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // A stale temp file is harmless; never turn cleanup into evidence loss.
            }
        }
    }

    /// <summary>
    /// Reconcile saved FAT evidence against the same signal scope owned by IED Explorer.
    /// Selected Explorer signals win; if no explicit selection exists, already-materialized
    /// live points define the active scope; only an entirely fresh/offline device falls back
    /// to all publishable process signals.
    /// </summary>
    public static void Reconcile(NativeFatDeviceState state, Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(device);

        state.SchemaVersion = Math.Max(2, state.SchemaVersion);
        state.DeviceId = device.DeviceId;
        state.PersistenceIdentity = BuildPersistenceIdentity(device);
        state.IedName = device.Name;
        state.Signals ??= new List<NativeFatSignalState>();

        var now = DateTimeOffset.UtcNow;
        var byKey = state.Signals
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var currentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var signal in GetCurrentExplorerSignals(device))
        {
            var key = NativeFatIdentity.BuildKey(signal);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            currentKeys.Add(key);
            if (!byKey.TryGetValue(key, out var saved))
            {
                saved = new NativeFatSignalState
                {
                    Key = key,
                    SignalName = signal.Name,
                    IecReference = signal.ObjectReference,
                    FunctionalConstraint = signal.FunctionalConstraint,
                    DataType = signal.DataType,
                    FirstSeenUtc = now,
                    LastSeenUtc = now,
                    Result = NativeFatResult.Untested,
                    IsHistorical = false
                };
                state.Signals.Add(saved);
                byKey[key] = saved;
            }
            else
            {
                // Current engineering metadata is refreshed while captures/history are
                // untouched. A display-name change is therefore a rename, not a new test.
                saved.SignalName = signal.Name;
                saved.IecReference = signal.ObjectReference;
                saved.FunctionalConstraint = signal.FunctionalConstraint;
                saved.DataType = signal.DataType;
                saved.LastSeenUtc = now;
                saved.IsHistorical = false;
                saved.History ??= new List<NativeFatHistoryEntry>();
                if (string.IsNullOrWhiteSpace(saved.Result))
                    saved.Result = NativeFatResult.Untested;
            }
        }

        // Never delete. When a signal leaves the current Explorer scope or SCL, preserve it
        // as historical evidence. Re-adding the same IEC identity automatically restores it.
        foreach (var saved in state.Signals)
        {
            if (!currentKeys.Contains(saved.Key))
                saved.IsHistorical = true;
        }

        state.UpdatedUtc = now;
    }

    public static IReadOnlyList<NativeFatSignalRow> BuildRows(
        NativeFatDeviceState state,
        Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(device);

        var signals = GetCurrentExplorerSignals(device)
            .GroupBy(NativeFatIdentity.BuildKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var points = device.Points
            .GroupBy(NativeFatIdentity.BuildKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return state.Signals
            .Select(saved => new NativeFatSignalRow(
                saved,
                signals.TryGetValue(saved.Key, out var signal) ? signal : null,
                points.TryGetValue(saved.Key, out var point) ? point : FindPointByReference(device, saved)))
            .OrderBy(row => row.IsHistorical)
            .ThenBy(row => row.SignalName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.IecReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<SignalDefinition> GetCurrentExplorerSignals(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var publishable = device.Signals
            .Where(signal => signal.CanPublishAsSignal)
            .ToArray();
        if (publishable.Length == 0)
            return Array.Empty<SignalDefinition>();

        var selected = publishable.Where(signal => signal.IsSelected).ToArray();
        if (selected.Length > 0)
            return selected;

        if (device.Points.Count > 0)
        {
            var activeKeys = device.Points
                .Select(NativeFatIdentity.BuildKey)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var materialized = publishable
                .Where(signal => activeKeys.Contains(NativeFatIdentity.BuildKey(signal)))
                .ToArray();
            if (materialized.Length > 0)
                return materialized;
        }

        return publishable;
    }

    public static string BuildPersistenceIdentity(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!string.IsNullOrWhiteSpace(device.SclSourceSha256))
        {
            var sha = NormalizeIdentityPart(device.SclSourceSha256);
            var ied = NormalizeIdentityPart(
                string.IsNullOrWhiteSpace(device.SclIedName) ? device.Name : device.SclIedName);
            var accessPoint = NormalizeIdentityPart(device.SclAccessPointName);
            return $"scl|{sha}|{ied}|{accessPoint}";
        }

        if (!string.IsNullOrWhiteSpace(device.IpAddress))
            return $"endpoint|{NormalizeIdentityPart(device.IpAddress)}:{Math.Max(1, device.Port)}";

        return BuildLegacyFallbackIdentity(device.DeviceId);
    }

    private static Iec61850MonitorPoint? FindPointByReference(
        Iec61850MonitorDevice device,
        NativeFatSignalState saved)
    {
        var reference = NativeFatIdentity.NormalizeReference(saved.IecReference);
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        return device.Points.FirstOrDefault(point =>
            NativeFatIdentity.NormalizeReference(point.IecReference)
                .Equals(reference, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<NativeFatDeviceState?> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                useAsync: true);
            return await JsonSerializer.DeserializeAsync<NativeFatDeviceState>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Tolerant by design: a damaged old file must not block engineering. Leave
            // the original file untouched so it can still be recovered manually.
            return null;
        }
    }

    private static bool IsForDevice(
        NativeFatDeviceState? state,
        Iec61850MonitorDevice device,
        string persistenceIdentity)
    {
        if (state == null)
            return false;

        if (!string.IsNullOrWhiteSpace(state.PersistenceIdentity))
        {
            return state.PersistenceIdentity.Equals(
                persistenceIdentity,
                StringComparison.OrdinalIgnoreCase);
        }

        // Exact runtime DeviceId remains a safe schema-1 compatibility match inside the
        // same session. Cross-restart migration is handled separately and only when the
        // legacy IED-name candidate is unambiguous.
        return !string.IsNullOrWhiteSpace(state.DeviceId) &&
               state.DeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase);
    }

    private static void Normalize(
        NativeFatDeviceState state,
        Iec61850MonitorDevice device,
        string persistenceIdentity)
    {
        state.SchemaVersion = Math.Max(2, state.SchemaVersion);
        state.DeviceId = device.DeviceId;
        state.PersistenceIdentity = persistenceIdentity;
        state.IedName = device.Name;
        state.Signals ??= new List<NativeFatSignalState>();
        foreach (var signal in state.Signals)
        {
            signal.History ??= new List<NativeFatHistoryEntry>();
            if (string.IsNullOrWhiteSpace(signal.Key))
                signal.Key = NativeFatIdentity.BuildKey(signal.IecReference, signal.FunctionalConstraint);
            if (string.IsNullOrWhiteSpace(signal.Result))
                signal.Result = NativeFatResult.Untested;
        }
    }

    private string SelectStoragePathForMigration(
        string sourcePath,
        string preferredPath,
        bool preferredPathOccupied)
    {
        if (sourcePath.Equals(preferredPath, StringComparison.OrdinalIgnoreCase))
            return preferredPath;

        // Move the next save to the stable schema-2 path when it is free. If something
        // already occupies that path, preserve both files instead of overwriting evidence.
        return preferredPathOccupied ? sourcePath : preferredPath;
    }

    private string GetPreferredPath(string? iedName, string? persistenceIdentity)
    {
        var stem = SanitizeFileStem(iedName);
        var identityHash = StableIdentityHash(persistenceIdentity);
        var fileStem = string.IsNullOrWhiteSpace(identityHash) ? stem : $"{stem}__{identityHash}";
        return Path.Combine(_rootDirectory, fileStem + ".json");
    }

    private string GetLegacyPath(string? iedName)
        => Path.Combine(_rootDirectory, SanitizeFileStem(iedName) + ".json");

    private static string GetNonDestructiveRecoveryPath(string preferredPath)
    {
        var directory = Path.GetDirectoryName(preferredPath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(preferredPath);
        var extension = Path.GetExtension(preferredPath);
        for (var index = 1; index <= 999; index++)
        {
            var candidate = Path.Combine(directory, $"{stem}__RECOVERY_{index:000}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(directory, $"{stem}__RECOVERY_{Guid.NewGuid():N}{extension}");
    }

    private static string StableIdentityHash(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
            return string.Empty;

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(identity.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes.AsSpan(0, 6));
    }

    private static string NormalizeIdentityPart(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string BuildLegacyFallbackIdentity(string? deviceId)
        => $"device|{NormalizeIdentityPart(deviceId)}";

    private static string SanitizeFileStem(string? value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "IED" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = new string(source.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim()
            .Trim('.');
        return string.IsNullOrWhiteSpace(result) ? "IED" : result;
    }
}
