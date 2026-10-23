using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Durable sparse FAT evidence authority keyed by stable IEDName + IEC Telegram.
/// Loading never depends on Engineering Points being materialized and saving never walks
/// the live WPF-bound row collection. Start FAT is only a producer of evidence; this store
/// is available for automatic load as soon as an IED identity is known.
/// </summary>
internal sealed class NativeFatEvidenceStore : IDisposable
{
    internal const string SnapshotSchema = "ARSAS-NATIVE-FAT-EVIDENCE-2.0";
    private const string LegacySnapshotSchema = "ARSAS-NATIVE-FAT-EVIDENCE-1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _rootDirectory;
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private bool _disposed;

    internal NativeFatEvidenceStore(string? rootDirectory = null)
    {
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ARSAS",
                "Native FAT Evidence")
            : Path.GetFullPath(rootDirectory!);
    }

    internal async Task<NativeFatEvidenceStoreLoadResult> LoadAsync(
        string iedName,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var normalizedIedName = NativeFatCanonicalEvidenceOverlay.NormalizeIedName(iedName);
        if (normalizedIedName.Length == 0)
            return NativeFatEvidenceStoreLoadResult.Empty("IEDName is empty.");

        var stopwatch = Stopwatch.StartNew();
        var preferredPath = SnapshotPath(iedName);

        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = File.Exists(preferredPath)
                ? preferredPath
                : FindNewestSnapshotByIedName(iedName);
            if (sourcePath == null)
            {
                stopwatch.Stop();
                return NativeFatEvidenceStoreLoadResult.Empty(
                    $"No saved FAT evidence exists yet for {iedName}.",
                    stopwatch.ElapsedMilliseconds);
            }

            var bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize<NativeFatEvidenceDocument>(bytes, JsonOptions)
                ?? throw new InvalidDataException("Native FAT evidence snapshot is invalid.");

            if (!string.Equals(document.Schema, SnapshotSchema, StringComparison.Ordinal) &&
                !string.Equals(document.Schema, LegacySnapshotSchema, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsupported native FAT evidence schema '{document.Schema}'.");
            }

            if (!string.IsNullOrWhiteSpace(document.DeviceName) &&
                !NativeFatCanonicalEvidenceOverlay.NormalizeIedName(document.DeviceName)
                    .Equals(normalizedIedName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Native FAT evidence belongs to a different IEDName.");
            }

            var loaded = new Dictionary<string, NativeFatEvidenceSlotState>(StringComparer.OrdinalIgnoreCase);
            var ignored = 0;
            foreach (var pair in document.EvidenceByRow ?? new Dictionary<string, NativeFatEvidenceSlotState>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (pair.Value == null || IsEmpty(pair.Value) ||
                    !TryNormalizeRowKey(pair.Key, iedName, document.DeviceName, out var stableRowKey))
                {
                    ignored++;
                    continue;
                }

                loaded[stableRowKey] = CloneForPersistence(pair.Value);
            }

            stopwatch.Stop();
            return new NativeFatEvidenceStoreLoadResult(
                true,
                true,
                sourcePath,
                loaded.Count,
                ignored,
                stopwatch.ElapsedMilliseconds,
                $"Loaded {loaded.Count} FAT evidence row(s) for {iedName}.",
                loaded);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            stopwatch.Stop();
            return new NativeFatEvidenceStoreLoadResult(
                false,
                File.Exists(preferredPath),
                preferredPath,
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

    internal async Task SaveAsync(
        NativeFatEvidenceDurabilitySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ThrowIfDisposed();

        var evidence = new Dictionary<string, NativeFatEvidenceSlotState>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in snapshot.EvidenceByRow)
        {
            if (!TryNormalizeRowKey(pair.Key, snapshot.IedName, snapshot.IedName, out var stableRowKey) ||
                pair.Value == null || IsEmpty(pair.Value))
            {
                continue;
            }

            evidence[stableRowKey] = CloneForPersistence(pair.Value);
        }

        var document = new NativeFatEvidenceDocument
        {
            Schema = SnapshotSchema,
            SavedAtUtc = DateTimeOffset.UtcNow,
            DeviceId = snapshot.DeviceId,
            DeviceName = snapshot.IedName,
            IpAddress = snapshot.IpAddress,
            EvidenceByRow = evidence
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        var path = SnapshotPath(snapshot.IedName);

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

    internal string SnapshotPath(string iedName)
        => Path.Combine(_rootDirectory, $"{SafeIedFileToken(iedName)}.native-fat-evidence.json");

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _ioGate.Dispose();
    }

    private string? FindNewestSnapshotByIedName(string iedName)
    {
        if (!Directory.Exists(_rootDirectory))
            return null;

        var normalized = NativeFatCanonicalEvidenceOverlay.NormalizeIedName(iedName);
        foreach (var candidate in Directory
                     .EnumerateFiles(_rootDirectory, "*.native-fat-evidence.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                using var stream = File.OpenRead(candidate);
                using var document = JsonDocument.Parse(stream);
                if (!document.RootElement.TryGetProperty("deviceName", out var name))
                    continue;
                if (NativeFatCanonicalEvidenceOverlay.NormalizeIedName(name.GetString()) == normalized)
                    return candidate;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                continue;
            }
        }

        return null;
    }

    private static bool TryNormalizeRowKey(
        string? persistedKey,
        string iedName,
        string? persistedIedName,
        out string stableRowKey)
    {
        stableRowKey = string.Empty;
        if (string.IsNullOrWhiteSpace(persistedKey))
            return false;

        var separator = persistedKey.IndexOf('|');
        if (separator <= 0 || separator >= persistedKey.Length - 1)
            return false;

        var owner = persistedKey[..separator].Trim();
        var reference = persistedKey[(separator + 1)..].Trim();
        var normalizedIedName = NativeFatCanonicalEvidenceOverlay.NormalizeIedName(iedName);
        var ownerMatchesIed = NativeFatCanonicalEvidenceOverlay.NormalizeIedName(owner)
            .Equals(normalizedIedName, StringComparison.OrdinalIgnoreCase);
        var documentMatchesIed = !string.IsNullOrWhiteSpace(persistedIedName) &&
            NativeFatCanonicalEvidenceOverlay.NormalizeIedName(persistedIedName)
                .Equals(normalizedIedName, StringComparison.OrdinalIgnoreCase);
        if (!ownerMatchesIed && !documentMatchesIed)
            return false;

        var telegram = Iec61850MonitorPoint.StripIedNamePrefix(reference, iedName);
        return NativeFatCanonicalEvidenceOverlay.TryBuildRowKey(iedName, telegram, out stableRowKey);
    }

    private static NativeFatEvidenceSlotState CloneForPersistence(NativeFatEvidenceSlotState source)
    {
        var value1 = source.Value1Evidence?.RawValue ?? source.Value1;
        var value2 = source.Value2Evidence?.RawValue ?? source.Value2;
        var result = source.Result?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(result) &&
            !string.IsNullOrWhiteSpace(value1) &&
            !string.IsNullOrWhiteSpace(value2))
        {
            result = "COMPLETE";
        }

        return new NativeFatEvidenceSlotState
        {
            Value1 = value1,
            Value2 = value2,
            Value1Evidence = source.Value1Evidence,
            Value2Evidence = source.Value2Evidence,
            Result = result
        };
    }

    private static bool IsEmpty(NativeFatEvidenceSlotState slot)
        => string.IsNullOrWhiteSpace(slot.Value1Evidence?.RawValue ?? slot.Value1) &&
           string.IsNullOrWhiteSpace(slot.Value2Evidence?.RawValue ?? slot.Value2) &&
           string.IsNullOrWhiteSpace(slot.Result);

    private static string SafeIedFileToken(string iedName)
    {
        var source = string.IsNullOrWhiteSpace(iedName) ? "IED" : iedName.Trim();
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(source.Length);
        foreach (var character in source)
            builder.Append(invalid.Contains(character) || character is '/' or '\\' ? '_' : character);

        var token = builder.ToString().Trim().TrimEnd('.');
        if (token.Length == 0)
            token = "IED";

        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };
        if (reserved.Contains(token))
            token += "_IED";

        if (!string.Equals(token, source, StringComparison.Ordinal))
        {
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(source));
            token += "-" + Convert.ToHexString(digest).ToLowerInvariant()[..8];
        }

        return token;
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

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

internal sealed record NativeFatEvidenceStoreLoadResult(
    bool Succeeded,
    bool SnapshotFound,
    string SourcePath,
    int LoadedRows,
    int IgnoredRows,
    long ElapsedMilliseconds,
    string Message,
    IReadOnlyDictionary<string, NativeFatEvidenceSlotState> EvidenceByRow)
{
    internal static NativeFatEvidenceStoreLoadResult Empty(string message, long elapsedMilliseconds = 0)
        => new(
            true,
            false,
            string.Empty,
            0,
            0,
            elapsedMilliseconds,
            message,
            new Dictionary<string, NativeFatEvidenceSlotState>(StringComparer.OrdinalIgnoreCase));
}
