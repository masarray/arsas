using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;

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
/// Existing IO FAT snapshots are read as passive evidence provenance only; opening FAT never
/// opens/restores their workspace model or starts any legacy bootstrap path.
/// </summary>
public sealed class NativeFatEvidenceHydrationService : IDisposable
{
    internal const string SnapshotSchema = "ARSAS-NATIVE-FAT-EVIDENCE-2.0";
    internal const string LegacySnapshotSchema = "ARSAS-NATIVE-FAT-EVIDENCE-1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly HashSet<string> FunctionalConstraintTokens = new(
        new[] { "st", "mx", "sp", "sv", "cf", "dc", "sg", "se", "sr", "or", "bl", "ex", "co", "us", "ms", "rp", "br", "lg", "go", "gs" },
        StringComparer.OrdinalIgnoreCase);

    private readonly string _rootDirectory;
    private readonly string _legacyProjectsRoot;
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private bool _disposed;

    public NativeFatEvidenceHydrationService(
        string? rootDirectory = null,
        string? legacyProjectsRoot = null)
    {
        var usingDefaultRoot = string.IsNullOrWhiteSpace(rootDirectory);
        _rootDirectory = usingDefaultRoot
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ARSAS",
                "Native FAT Evidence")
            : Path.GetFullPath(rootDirectory!);

        _legacyProjectsRoot = !string.IsNullOrWhiteSpace(legacyProjectsRoot)
            ? Path.GetFullPath(legacyProjectsRoot)
            : usingDefaultRoot
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ARSAS",
                    "IO Testing Projects")
                : string.Empty;
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
        var canonical = CaptureCanonicalIdentity(device);
        var stopwatch = Stopwatch.StartNew();
        var path = SnapshotPath(identity.DeviceName);
        var legacyNativePath = LegacyDeviceSnapshotPath(identity.DeviceId);

        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = File.Exists(path)
                ? path
                : File.Exists(legacyNativePath)
                    ? legacyNativePath
                    : FindLegacyNativeSnapshotByIedName(identity.DeviceName);
            if (sourcePath == null)
            {
                // First native launch may still have evidence in the pre-P1 persistent
                // project snapshot. Read it passively and map only uniquely covered rows.
                var legacy = await TryHydrateLegacySnapshotAsync(
                    identity,
                    canonical,
                    cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();
                if (legacy != null)
                {
                    return new NativeFatEvidenceHydrationResult(
                        true,
                        true,
                        legacy.EvidenceByRow.Count,
                        legacy.IgnoredRows,
                        stopwatch.ElapsedMilliseconds,
                        $"Restored {legacy.EvidenceByRow.Count} legacy FAT evidence row(s) for {identity.DeviceName} in {stopwatch.ElapsedMilliseconds} ms without opening the legacy workspace.",
                        legacy.EvidenceByRow);
                }

                return EmptyResult(
                    stopwatch.ElapsedMilliseconds,
                    $"No saved FAT evidence exists yet for {identity.DeviceName}.");
            }

            var bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize<NativeFatEvidenceDocument>(bytes, JsonOptions)
                ?? throw new InvalidDataException("Native FAT evidence snapshot is invalid.");

            if (!string.Equals(document.Schema, SnapshotSchema, StringComparison.Ordinal) &&
                !string.Equals(document.Schema, LegacySnapshotSchema, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsupported native FAT evidence schema '{document.Schema}'.");
            }

            // P4A/P4B identity is domain-stable. Runtime DeviceId is diagnostics only and
            // may legitimately change after Engineering recreation or application restart.
            if (!string.IsNullOrWhiteSpace(document.DeviceName) &&
                !NativeFatCanonicalEvidenceOverlay.NormalizeIedName(document.DeviceName)
                    .Equals(
                        NativeFatCanonicalEvidenceOverlay.NormalizeIedName(identity.DeviceName),
                        StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Native FAT evidence belongs to a different IEDName.");
            }

            var loaded = new Dictionary<string, NativeFatEvidenceSlotState>(StringComparer.OrdinalIgnoreCase);
            var ignored = 0;
            foreach (var pair in document.EvidenceByRow ?? new Dictionary<string, NativeFatEvidenceSlotState>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var resolvedKey = ResolvePersistedRowKey(pair.Key, identity, canonical, document.DeviceName);
                if (resolvedKey == null)
                {
                    ignored++;
                    continue;
                }

                var source = pair.Value;
                if (source == null || IsEmpty(source))
                    continue;

                loaded[resolvedKey] = Clone(source);
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
                File.Exists(path) || File.Exists(legacyNativePath),
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
        var path = SnapshotPath(identity.DeviceName);

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
    {
        var stableIdentity = NativeFatCanonicalEvidenceOverlay.NormalizeIedName(iedName);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(stableIdentity));
        var token = Convert.ToHexString(digest).ToLowerInvariant()[..24];
        return Path.Combine(_rootDirectory, $"{token}.native-fat-evidence.json");
    }

    private string LegacyDeviceSnapshotPath(string deviceId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(deviceId.Trim().ToLowerInvariant()));
        var token = Convert.ToHexString(digest).ToLowerInvariant()[..24];
        return Path.Combine(_rootDirectory, $"{token}.native-fat-evidence.json");
    }

    private string? FindLegacyNativeSnapshotByIedName(string iedName)
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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _ioGate.Dispose();
    }

    private async Task<LegacyHydration?> TryHydrateLegacySnapshotAsync(
        NativeFatDeviceIdentity identity,
        CanonicalEvidenceIdentity canonical,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_legacyProjectsRoot) || !Directory.Exists(_legacyProjectsRoot))
            return null;

        string[] candidates;
        try
        {
            candidates = Directory
                .EnumerateFiles(_legacyProjectsRoot, "project.snapshot.json", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var bytes = await File.ReadAllBytesAsync(candidate, cancellationToken).ConfigureAwait(false);
                using var document = JsonDocument.Parse(bytes);
                if (!TryGetProjectIeds(document.RootElement, out var ieds) ||
                    !TryFindLegacyIed(ieds, identity, out var legacyIed))
                {
                    continue;
                }

                // Newest matching snapshot is authoritative, including an intentionally
                // empty evidence set. Never resurrect older evidence after a later clear.
                return ExtractLegacyEvidence(legacyIed, canonical);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                // A damaged/unreadable unrelated historical snapshot must not block FAT.
                continue;
            }
        }

        return null;
    }

    private static bool TryGetProjectIeds(JsonElement root, out JsonElement ieds)
    {
        ieds = default;
        if (!root.TryGetProperty("project", out var project) ||
            !project.TryGetProperty("ieds", out ieds) ||
            ieds.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        return true;
    }

    private static bool TryFindLegacyIed(
        JsonElement ieds,
        NativeFatDeviceIdentity identity,
        out JsonElement legacyIed)
    {
        legacyIed = default;
        JsonElement? nameAndIp = null;
        JsonElement? uniqueIp = null;
        var ipMatches = 0;

        foreach (var ied in ieds.EnumerateArray())
        {
            var liveDeviceId = GetString(ied, "liveDeviceId");
            if (!string.IsNullOrWhiteSpace(liveDeviceId) &&
                liveDeviceId.Equals(identity.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                legacyIed = ied;
                return true;
            }

            var ip = GetString(ied, "ipAddress");
            if (!ip.Equals(identity.IpAddress, StringComparison.OrdinalIgnoreCase))
                continue;

            ipMatches++;
            uniqueIp = ied;
            if (GetString(ied, "iedName").Equals(identity.DeviceName, StringComparison.OrdinalIgnoreCase))
                nameAndIp = ied;
        }

        if (nameAndIp.HasValue)
        {
            legacyIed = nameAndIp.Value;
            return true;
        }

        if (ipMatches == 1 && uniqueIp.HasValue)
        {
            legacyIed = uniqueIp.Value;
            return true;
        }

        return false;
    }

    private static LegacyHydration ExtractLegacyEvidence(
        JsonElement legacyIed,
        CanonicalEvidenceIdentity canonical)
    {
        var loaded = new Dictionary<string, NativeFatEvidenceSlotState>(StringComparer.OrdinalIgnoreCase);
        var ignored = 0;
        if (!legacyIed.TryGetProperty("testPoints", out var testPoints) ||
            testPoints.ValueKind != JsonValueKind.Array)
        {
            return new LegacyHydration(loaded, ignored);
        }

        foreach (var point in testPoints.EnumerateArray())
        {
            if (!point.TryGetProperty("runtime", out var runtime) || runtime.ValueKind != JsonValueKind.Object)
                continue;

            var value1Capture = GetEvidenceCapture(runtime, "value1Evidence", FatValueSlot.Value1)
                ?? GetEvidenceCapture(runtime, "onEvidence", FatValueSlot.Value1);
            var value2Capture = GetEvidenceCapture(runtime, "value2Evidence", FatValueSlot.Value2)
                ?? GetEvidenceCapture(runtime, "offEvidence", FatValueSlot.Value2);
            var value1 = value1Capture?.RawValue ?? GetEvidenceRaw(runtime, "value1Evidence");
            if (string.IsNullOrWhiteSpace(value1))
                value1 = GetEvidenceRaw(runtime, "onEvidence");
            var value2 = value2Capture?.RawValue ?? GetEvidenceRaw(runtime, "value2Evidence");
            if (string.IsNullOrWhiteSpace(value2))
                value2 = GetEvidenceRaw(runtime, "offEvidence");
            var result = ReadLegacyResult(point, runtime, value1, value2);

            if (string.IsNullOrWhiteSpace(value1) &&
                string.IsNullOrWhiteSpace(value2) &&
                string.IsNullOrWhiteSpace(result))
            {
                continue;
            }

            var rowKey = ResolveLegacyRowKey(point, canonical);
            if (string.IsNullOrWhiteSpace(rowKey))
            {
                ignored++;
                continue;
            }

            if (!loaded.TryGetValue(rowKey, out var slot))
            {
                slot = new NativeFatEvidenceSlotState();
                loaded[rowKey] = slot;
            }

            if (string.IsNullOrWhiteSpace(slot.Value1) && !string.IsNullOrWhiteSpace(value1))
            {
                slot.Value1 = value1;
                slot.Value1Evidence = value1Capture;
            }
            if (string.IsNullOrWhiteSpace(slot.Value2) && !string.IsNullOrWhiteSpace(value2))
            {
                slot.Value2 = value2;
                slot.Value2Evidence = value2Capture;
            }
            if (string.IsNullOrWhiteSpace(slot.Result) && !string.IsNullOrWhiteSpace(result))
                slot.Result = result;
        }

        return new LegacyHydration(loaded, ignored);
    }

    private static string? ResolvePersistedRowKey(
        string persistedKey,
        NativeFatDeviceIdentity identity,
        CanonicalEvidenceIdentity canonical,
        string persistedIedName)
    {
        if (string.IsNullOrWhiteSpace(persistedKey))
            return null;
        if (canonical.RowKeys.Contains(persistedKey))
            return persistedKey;

        // Pre-P4A snapshots used runtime DeviceId|reference. Recover only through the
        // existing unique IEC-reference alias map; ambiguity remains fail-closed.
        var separator = persistedKey.IndexOf('|');
        if (separator <= 0 || separator >= persistedKey.Length - 1)
            return null;

        var owner = persistedKey[..separator].Trim();
        var reference = persistedKey[(separator + 1)..].Trim();
        var ownerMatchesRuntime = owner.Equals(identity.DeviceId, StringComparison.OrdinalIgnoreCase);
        var ownerMatchesIed = NativeFatCanonicalEvidenceOverlay.NormalizeIedName(owner)
            .Equals(
                NativeFatCanonicalEvidenceOverlay.NormalizeIedName(identity.DeviceName),
                StringComparison.OrdinalIgnoreCase);
        var documentMatchesIed = !string.IsNullOrWhiteSpace(persistedIedName) &&
            NativeFatCanonicalEvidenceOverlay.NormalizeIedName(persistedIedName)
                .Equals(
                    NativeFatCanonicalEvidenceOverlay.NormalizeIedName(identity.DeviceName),
                    StringComparison.OrdinalIgnoreCase);
        if (!ownerMatchesRuntime && !ownerMatchesIed && !documentMatchesIed)
            return null;

        var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in ReferenceAliases(reference))
        {
            if (canonical.AliasToRowKey.TryGetValue(alias, out var rowKey) &&
                !string.IsNullOrWhiteSpace(rowKey))
            {
                matches.Add(rowKey);
            }
        }
        return matches.Count == 1 ? matches.First() : null;
    }

    private static string? ResolveLegacyRowKey(
        JsonElement point,
        CanonicalEvidenceIdentity canonical)
    {
        var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in new[]
                 {
                     "sourceIecReference",
                     "eventLogSearchReference",
                     "reportDisplayReference",
                     "objectReference",
                     "signalAddress"
                 })
        {
            var reference = GetString(point, property);
            foreach (var alias in ReferenceAliases(reference))
            {
                if (canonical.AliasToRowKey.TryGetValue(alias, out var rowKey) &&
                    !string.IsNullOrWhiteSpace(rowKey))
                {
                    matches.Add(rowKey);
                }
            }
        }

        return matches.Count == 1 ? matches.First() : null;
    }

    private static string ReadLegacyResult(
        JsonElement point,
        JsonElement runtime,
        string value1,
        string value2)
    {
        var state = ReadStateOrdinal(runtime);
        if (state == 5) return "PASS";
        if (state == 6) return "REVIEW";
        if (state == 7) return "FAILED";

        var reviewStatus = GetString(point, "reviewStatus").Trim();
        if (reviewStatus.Equals("PASS", StringComparison.OrdinalIgnoreCase) ||
            reviewStatus.Equals("REVIEW", StringComparison.OrdinalIgnoreCase) ||
            reviewStatus.Equals("FAILED", StringComparison.OrdinalIgnoreCase))
        {
            return reviewStatus.ToUpperInvariant();
        }

        var captureMode = ReadCaptureModeOrdinal(point);
        return captureMode == 1 &&
               !string.IsNullOrWhiteSpace(value1) &&
               !string.IsNullOrWhiteSpace(value2)
            ? "COMPLETE"
            : string.Empty;
    }

    private static int ReadStateOrdinal(JsonElement runtime)
    {
        if (!runtime.TryGetProperty("state", out var state))
            return -1;
        if (state.ValueKind == JsonValueKind.Number && state.TryGetInt32(out var ordinal))
            return ordinal;
        if (state.ValueKind != JsonValueKind.String)
            return -1;

        return (state.GetString() ?? string.Empty).Trim() switch
        {
            "Passed" => 5,
            "Review" => 6,
            "Failed" => 7,
            _ => -1
        };
    }

    private static int ReadCaptureModeOrdinal(JsonElement point)
    {
        if (!point.TryGetProperty("captureMode", out var mode))
            return -1;
        if (mode.ValueKind == JsonValueKind.Number && mode.TryGetInt32(out var ordinal))
            return ordinal;
        if (mode.ValueKind == JsonValueKind.String &&
            string.Equals(mode.GetString(), "OperatorSnapshot", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }
        return 0;
    }

    private static FatValueEvidence? GetEvidenceCapture(
        JsonElement runtime,
        string property,
        FatValueSlot slot)
    {
        if (!runtime.TryGetProperty(property, out var evidence) || evidence.ValueKind != JsonValueKind.Object)
            return null;

        var raw = GetString(evidence, "rawValue").Trim();
        if (raw.Length == 0)
            return null;

        var capturedAt = GetDateTimeOffset(evidence, "capturedAt");
        var iedTimestamp = GetDateTimeOffset(evidence, "iedTimestamp");
        if (capturedAt == null && iedTimestamp == null)
            return null;

        return new FatValueEvidence(
            Guid.NewGuid(),
            slot,
            FatEvidenceCaptureKind.AutomaticValue,
            raw,
            capturedAt ?? iedTimestamp!.Value,
            iedTimestamp,
            GetString(evidence, "quality"),
            GetString(evidence, "acquisitionSource"),
            GetInt64(evidence, "sequence"),
            GetInt64(evidence, "connectionGeneration", -1));
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string property)
    {
        var text = GetString(element, property);
        return DateTimeOffset.TryParse(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AllowWhiteSpaces | System.Globalization.DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }

    private static long GetInt64(JsonElement element, string property, long fallback = 0)
    {
        if (!element.TryGetProperty(property, out var value))
            return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;
        return long.TryParse(value.ToString(), out number) ? number : fallback;
    }

    private static string GetEvidenceRaw(JsonElement runtime, string property)
    {
        if (!runtime.TryGetProperty(property, out var evidence) ||
            evidence.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
            evidence.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        return GetString(evidence, "rawValue").Trim();
    }

    private static string GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return string.Empty;
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? string.Empty
                : value.ToString();
    }

    private static CanonicalEvidenceIdentity CaptureCanonicalIdentity(Iec61850MonitorDevice device)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aliases = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in device.Points)
        {
            if (!NativeFatCanonicalEvidenceOverlay.TryBuildRowKey(point, out var key))
                continue;
            keys.Add(key);
            AddAliases(aliases, point.IecReference, key);
            AddAliases(aliases, point.IecTelegram, key);
        }
        return new CanonicalEvidenceIdentity(keys, aliases);
    }

    private static void AddAliases(
        Dictionary<string, string?> aliases,
        string? reference,
        string rowKey)
    {
        foreach (var alias in ReferenceAliases(reference))
        {
            if (aliases.TryGetValue(alias, out var existing))
            {
                if (!string.Equals(existing, rowKey, StringComparison.OrdinalIgnoreCase))
                    aliases[alias] = null;
            }
            else
            {
                aliases[alias] = rowKey;
            }
        }
    }

    private static IEnumerable<string> ReferenceAliases(string? reference)
    {
        var normalized = NormalizeReference(reference);
        if (normalized.Length == 0)
            yield break;

        yield return normalized;
        var withoutFc = RemoveFunctionalConstraint(normalized);
        if (!withoutFc.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            yield return withoutFc;
    }

    private static string NormalizeReference(string? reference)
    {
        var text = (reference ?? string.Empty)
            .Trim()
            .Replace('$', '.')
            .Replace("..", ".", StringComparison.Ordinal)
            .ToLowerInvariant();
        while (text.Contains("..", StringComparison.Ordinal))
            text = text.Replace("..", ".", StringComparison.Ordinal);
        return text.Trim('.');
    }

    private static string RemoveFunctionalConstraint(string normalized)
    {
        var slash = normalized.IndexOf('/');
        if (slash < 0 || slash >= normalized.Length - 1)
            return normalized;

        var domain = normalized[..(slash + 1)];
        var path = normalized[(slash + 1)..].Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (path.Length < 3 || !FunctionalConstraintTokens.Contains(path[1]))
            return normalized;

        return domain + string.Join('.', path.Where((_, index) => index != 1));
    }

    private static NativeFatDeviceIdentity CaptureIdentity(Iec61850MonitorDevice device)
        => new(
            device.DeviceId,
            device.Name,
            device.IpAddress);

    private static NativeFatEvidenceSlotState Clone(NativeFatEvidenceSlotState source)
        => new()
        {
            Value1 = source.Value1Evidence?.RawValue ?? source.Value1,
            Value2 = source.Value2Evidence?.RawValue ?? source.Value2,
            Value1Evidence = source.Value1Evidence,
            Value2Evidence = source.Value2Evidence,
            Result = source.Result
        };

    private static bool IsEmpty(NativeFatEvidenceSlotState slot)
        => string.IsNullOrWhiteSpace(slot.Value1Evidence?.RawValue ?? slot.Value1) &&
           string.IsNullOrWhiteSpace(slot.Value2Evidence?.RawValue ?? slot.Value2) &&
           string.IsNullOrWhiteSpace(slot.Result);

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

    private sealed record CanonicalEvidenceIdentity(
        HashSet<string> RowKeys,
        Dictionary<string, string?> AliasToRowKey);

    private sealed record LegacyHydration(
        IReadOnlyDictionary<string, NativeFatEvidenceSlotState> EvidenceByRow,
        int IgnoredRows);

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
        => (Math.Abs(phase) % 3) switch
        {
            0 => "·",
            1 => "··",
            _ => "···"
        };
}
