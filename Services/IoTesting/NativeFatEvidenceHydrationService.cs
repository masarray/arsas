using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record NativeFatEvidenceHydrationResult(
    bool Succeeded,
    bool SnapshotFound,
    int LoadedRows,
    int IgnoredRows,
    long ElapsedMilliseconds,
    string Message,
    IReadOnlyDictionary<string, NativeFatEvidenceSlotState> EvidenceByRow);

/// <summary>
/// P2 local evidence persistence/hydration for the native Engineering FAT surface.
/// This service owns only sparse Value 1 / Value 2 / Result data. It never touches
/// Engineering acquisition, SCL, reports, MMS sessions, polling cadence, or canonical rows.
/// </summary>
public sealed class NativeFatEvidenceHydrationService : IDisposable
{
    internal const string SnapshotSchema = "ARSAS-NATIVE-FAT-EVIDENCE-1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _rootDirectory;
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private bool _disposed;

    public NativeFatEvidenceHydrationService(string? rootDirectory = null)
    {
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ARSAS",
                "Native FAT Evidence")
            : Path.GetFullPath(rootDirectory);
    }

    public async Task<NativeFatEvidenceHydrationResult> HydrateAsync(
        Iec61850MonitorDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ThrowIfDisposed();

        // Snapshot canonical identity synchronously on the caller/UI thread. Everything
        // after this point is file IO / JSON work and does not enumerate the live collection.
        var identity = CaptureIdentity(device);
        var canonicalKeys = device.Points
            .Select(NativeFatCanonicalEvidenceOverlay.BuildRowKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stopwatch = Stopwatch.StartNew();
        var path = SnapshotPath(identity.DeviceId);

        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path))
            {
                stopwatch.Stop();
                return EmptyResult(
                    stopwatch.ElapsedMilliseconds,
                    $"No saved FAT evidence exists yet for {identity.DeviceName}.");
            }

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize<NativeFatEvidenceDocument>(bytes, JsonOptions)
                ?? throw new InvalidDataException("Native FAT evidence snapshot is invalid.");

            if (!string.Equals(document.Schema, SnapshotSchema, StringComparison.Ordinal))
                throw new InvalidDataException($"Unsupported native FAT evidence schema '{document.Schema}'.");
            if (!string.Equals(document.DeviceId, identity.DeviceId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Native FAT evidence belongs to a different Engineering device identity.");

            var loaded = new Dictionary<string, NativeFatEvidenceSlotState>(StringComparer.OrdinalIgnoreCase);
            var ignored = 0;
            foreach (var pair in document.EvidenceByRow ?? new Dictionary<string, NativeFatEvidenceSlotState>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!canonicalKeys.Contains(pair.Key))
                {
                    ignored++;
                    continue;
                }

                var source = pair.Value;
                if (source == null ||
                    (string.IsNullOrWhiteSpace(source.Value1) &&
                     string.IsNullOrWhiteSpace(source.Value2) &&
                     string.IsNullOrWhiteSpace(source.Result)))
                {
                    continue;
                }

                loaded[pair.Key] = Clone(source);
            }

            stopwatch.Stop();
            return new NativeFatEvidenceHydrationResult(
                true,
                true,
                loaded.Count,
                ignored,
                stopwatch.ElapsedMilliseconds,
                $"Restored {loaded.Count} sparse evidence row(s) for {identity.DeviceName} in {stopwatch.ElapsedMilliseconds} ms.",
                loaded);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            stopwatch.Stop();
            return new NativeFatEvidenceHydrationResult(
                false,
                File.Exists(path),
                0,
                0,
                stopwatch.ElapsedMilliseconds,
                ex.Message,
                new Dictionary<string, NativeFatEvidenceSlotState>(StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task SaveAsync(
        Iec61850MonitorDevice device,
        NativeFatIedSessionCacheState cache,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(cache);
        ThrowIfDisposed();

        // Capture device/row identity before yielding so background persistence never walks
        // a WPF-bound ObservableCollection. Evidence itself is copied under its own lock.
        var identity = CaptureIdentity(device);
        var canonicalKeys = device.Points
            .Select(NativeFatCanonicalEvidenceOverlay.BuildRowKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var evidence = NativeFatCanonicalEvidenceOverlay.Snapshot(cache)
            .Where(pair => canonicalKeys.Contains(pair.Key))
            .ToDictionary(
                pair => pair.Key,
                pair => Clone(pair.Value),
                StringComparer.OrdinalIgnoreCase);
        var document = new NativeFatEvidenceDocument
        {
            Schema = SnapshotSchema,
            SavedAtUtc = DateTimeOffset.UtcNow,
            DeviceId = identity.DeviceId,
            DeviceName = identity.DeviceName,
            IpAddress = identity.IpAddress,
            EvidenceByRow = evidence
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        var path = SnapshotPath(identity.DeviceId);

        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_rootDirectory);
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
        finally
        {
            _ioGate.Release();
        }
    }

    internal string SnapshotPath(string deviceId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(deviceId.Trim().ToLowerInvariant()));
        var token = Convert.ToHexString(digest).ToLowerInvariant()[..24];
        return Path.Combine(_rootDirectory, $"{token}.native-fat-evidence.json");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _ioGate.Dispose();
    }

    private static NativeFatDeviceIdentity CaptureIdentity(Iec61850MonitorDevice device)
        => new(
            device.DeviceId,
            device.Name,
            device.IpAddress);

    private static NativeFatEvidenceSlotState Clone(NativeFatEvidenceSlotState source)
        => new()
        {
            Value1 = source.Value1,
            Value2 = source.Value2,
            Result = source.Result
        };

    private static NativeFatEvidenceHydrationResult EmptyResult(long elapsedMilliseconds, string message)
        => new(
            true,
            false,
            0,
            0,
            elapsedMilliseconds,
            message,
            new Dictionary<string, NativeFatEvidenceSlotState>(StringComparer.OrdinalIgnoreCase));

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record NativeFatDeviceIdentity(
        string DeviceId,
        string DeviceName,
        string IpAddress);

    private sealed class NativeFatEvidenceDocument
    {
        public string Schema { get; set; } = SnapshotSchema;
        public DateTimeOffset SavedAtUtc { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public Dictionary<string, NativeFatEvidenceSlotState> EvidenceByRow { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}

public static class NativeFatEvidenceLoadingPresentation
{
    /// <summary>
    /// One shared UI clock advances this phase for every unresolved evidence cell.
    /// No cell owns a timer or animation object.
    /// </summary>
    public static string RollingDots(int phase)
        => Math.Abs(phase) % 3 switch
        {
            0 => "·",
            1 => "··",
            _ => "···"
        };
}
