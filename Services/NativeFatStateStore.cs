using System.Text.Json;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Non-destructive, per-IED FAT persistence.  The store never deletes unmatched signal
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

        var preferredPath = GetPreferredPath(device.Name);
        var candidate = await TryReadAsync(preferredPath, cancellationToken).ConfigureAwait(false);
        if (IsForDevice(candidate, device))
        {
            candidate!.StoragePath = preferredPath;
            Normalize(candidate, device);
            return candidate;
        }

        // IED display names can change.  Resolve by stable DeviceId before creating a new
        // state so a harmless rename cannot strand the operator's previous FAT evidence.
        foreach (var path in Directory.EnumerateFiles(_rootDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path.Equals(preferredPath, StringComparison.OrdinalIgnoreCase))
                continue;

            var probed = await TryReadAsync(path, cancellationToken).ConfigureAwait(false);
            if (!IsForDevice(probed, device))
                continue;

            probed!.StoragePath = path;
            Normalize(probed, device);
            return probed;
        }

        return new NativeFatDeviceState
        {
            DeviceId = device.DeviceId,
            IedName = device.Name,
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow,
            StoragePath = preferredPath
        };
    }

    public async Task SaveAsync(NativeFatDeviceState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        Directory.CreateDirectory(_rootDirectory);
        state.UpdatedUtc = DateTimeOffset.UtcNow;
        state.SchemaVersion = Math.Max(1, state.SchemaVersion);

        var path = string.IsNullOrWhiteSpace(state.StoragePath)
            ? GetPreferredPath(state.IedName)
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

    public static void Reconcile(NativeFatDeviceState state, Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(device);

        state.DeviceId = device.DeviceId;
        state.IedName = device.Name;
        state.Signals ??= new List<NativeFatSignalState>();

        var now = DateTimeOffset.UtcNow;
        var byKey = state.Signals
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var currentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var point in device.Points)
        {
            var key = NativeFatIdentity.BuildKey(point);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            currentKeys.Add(key);
            if (!byKey.TryGetValue(key, out var saved))
            {
                saved = new NativeFatSignalState
                {
                    Key = key,
                    SignalName = point.SignalName,
                    IecReference = point.IecReference,
                    FunctionalConstraint = point.FunctionalConstraint,
                    DataType = point.IecDataType,
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
                // Metadata follows the current engineering model while captures/history
                // remain untouched. This is what makes signal display-name changes safe.
                saved.SignalName = point.SignalName;
                saved.IecReference = point.IecReference;
                saved.FunctionalConstraint = point.FunctionalConstraint;
                saved.DataType = point.IecDataType;
                saved.LastSeenUtc = now;
                saved.IsHistorical = false;
                saved.History ??= new List<NativeFatHistoryEntry>();
                if (string.IsNullOrWhiteSpace(saved.Result))
                    saved.Result = NativeFatResult.Untested;
            }
        }

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

        var current = device.Points
            .GroupBy(NativeFatIdentity.BuildKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return state.Signals
            .Select(saved => new NativeFatSignalRow(
                saved,
                current.TryGetValue(saved.Key, out var point) ? point : null))
            .OrderBy(row => row.IsHistorical)
            .ThenBy(row => row.SignalName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.IecReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
            // Tolerant by design: a damaged old file must not block engineering.  Leave
            // the original file untouched so it can still be recovered manually.
            return null;
        }
    }

    private static bool IsForDevice(NativeFatDeviceState? state, Iec61850MonitorDevice device)
    {
        if (state == null)
            return false;
        if (!string.IsNullOrWhiteSpace(state.DeviceId) &&
            state.DeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase))
            return true;

        return string.IsNullOrWhiteSpace(state.DeviceId) &&
               state.IedName.Equals(device.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static void Normalize(NativeFatDeviceState state, Iec61850MonitorDevice device)
    {
        state.SchemaVersion = Math.Max(1, state.SchemaVersion);
        state.DeviceId = device.DeviceId;
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

    private string GetPreferredPath(string? iedName)
        => Path.Combine(_rootDirectory, SanitizeFileStem(iedName) + ".json");

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
