using System.Globalization;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public enum NativeFatEvidenceField
{
    Value1,
    Value1Timestamp,
    Value2,
    Value2Timestamp,
    Result
}

/// <summary>
/// Sparse FAT-only evidence keyed exclusively by stable IEC identity: IEDName + IEC Telegram.
/// Runtime DeviceId, row index, SelectedIndex and display labels are never evidence identity.
/// </summary>
public static class NativeFatCanonicalEvidenceOverlay
{
    public static string BuildRowKey(Iec61850MonitorPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return TryBuildRowKey(point, out var rowKey) ? rowKey : string.Empty;
    }

    public static bool TryBuildRowKey(Iec61850MonitorPoint point, out string rowKey)
    {
        ArgumentNullException.ThrowIfNull(point);
        return TryBuildRowKey(point.DeviceName, point.IecTelegram, out rowKey);
    }

    internal static bool TryBuildRowKey(string? iedName, string? iecTelegram, out string rowKey)
    {
        var normalizedIedName = NormalizeIedName(iedName);
        var normalizedTelegram = NormalizeTelegram(iecTelegram);
        if (normalizedIedName.Length == 0 || normalizedTelegram.Length == 0)
        {
            rowKey = string.Empty;
            return false;
        }

        rowKey = $"{normalizedIedName}|{normalizedTelegram}";
        return true;
    }

    public static string Read(
        NativeFatIedSessionCacheState cache,
        Iec61850MonitorPoint point,
        NativeFatEvidenceField field)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(point);
        if (!TryBuildRowKey(point, out var key))
            return string.Empty;

        lock (cache.EvidenceByRow)
        {
            if (!cache.EvidenceByRow.TryGetValue(key, out var slot))
                return string.Empty;

            return field switch
            {
                NativeFatEvidenceField.Value1 => RawValue(slot.Value1Evidence, slot.Value1),
                NativeFatEvidenceField.Value1Timestamp => TimestampValue(slot.Value1Evidence),
                NativeFatEvidenceField.Value2 => RawValue(slot.Value2Evidence, slot.Value2),
                NativeFatEvidenceField.Value2Timestamp => TimestampValue(slot.Value2Evidence),
                NativeFatEvidenceField.Result => ResolveResult(slot),
                _ => string.Empty
            };
        }
    }

    public static string ReadRaw(
        NativeFatIedSessionCacheState cache,
        Iec61850MonitorPoint point,
        NativeFatEvidenceField field)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(point);
        if (!TryBuildRowKey(point, out var key))
            return string.Empty;

        lock (cache.EvidenceByRow)
        {
            if (!cache.EvidenceByRow.TryGetValue(key, out var slot))
                return string.Empty;

            return field switch
            {
                NativeFatEvidenceField.Value1 => RawValue(slot.Value1Evidence, slot.Value1),
                NativeFatEvidenceField.Value1Timestamp => TimestampValue(slot.Value1Evidence),
                NativeFatEvidenceField.Value2 => RawValue(slot.Value2Evidence, slot.Value2),
                NativeFatEvidenceField.Value2Timestamp => TimestampValue(slot.Value2Evidence),
                NativeFatEvidenceField.Result => slot.Result,
                _ => string.Empty
            };
        }
    }

    /// <summary>
    /// Compatibility display form retained for legacy/manual consumers. Native FAT no longer
    /// uses this combined text because values and timestamps have dedicated columns.
    /// </summary>
    public static string ReadDisplay(
        NativeFatIedSessionCacheState cache,
        Iec61850MonitorPoint point,
        NativeFatEvidenceField field)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(point);
        if (!TryBuildRowKey(point, out var key))
            return string.Empty;

        lock (cache.EvidenceByRow)
        {
            if (!cache.EvidenceByRow.TryGetValue(key, out var slot))
                return string.Empty;

            return field switch
            {
                NativeFatEvidenceField.Value1 => DisplayValue(slot.Value1Evidence, slot.Value1),
                NativeFatEvidenceField.Value1Timestamp => TimestampValue(slot.Value1Evidence),
                NativeFatEvidenceField.Value2 => DisplayValue(slot.Value2Evidence, slot.Value2),
                NativeFatEvidenceField.Value2Timestamp => TimestampValue(slot.Value2Evidence),
                NativeFatEvidenceField.Result => ResolveResult(slot),
                _ => string.Empty
            };
        }
    }

    public static FatValueEvidence? ReadCapture(
        NativeFatIedSessionCacheState cache,
        Iec61850MonitorPoint point,
        NativeFatEvidenceField field)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(point);
        if (!TryBuildRowKey(point, out var key))
            return null;

        lock (cache.EvidenceByRow)
        {
            if (!cache.EvidenceByRow.TryGetValue(key, out var slot))
                return null;

            return field switch
            {
                NativeFatEvidenceField.Value1 or NativeFatEvidenceField.Value1Timestamp => slot.Value1Evidence,
                NativeFatEvidenceField.Value2 or NativeFatEvidenceField.Value2Timestamp => slot.Value2Evidence,
                _ => null
            };
        }
    }

    public static void Write(
        NativeFatIedSessionCacheState cache,
        Iec61850MonitorPoint point,
        NativeFatEvidenceField field,
        string? value)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(point);
        if (field is NativeFatEvidenceField.Value1Timestamp or NativeFatEvidenceField.Value2Timestamp)
            return;
        if (!TryBuildRowKey(point, out var key))
            return;

        var supplied = value ?? string.Empty;
        if ((field is NativeFatEvidenceField.Value1 or NativeFatEvidenceField.Value2) &&
            string.Equals(ReadRaw(cache, point, field).Trim(), supplied.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        var text = (field is NativeFatEvidenceField.Value1 or NativeFatEvidenceField.Value2)
            ? StripDisplayTimestamp(supplied)
            : supplied.Trim();

        if ((field is NativeFatEvidenceField.Value1 or NativeFatEvidenceField.Value2) &&
            !string.IsNullOrWhiteSpace(text))
        {
            WriteCapture(cache, point, field, text, FatEvidenceCaptureKind.OperatorRecapture, DateTimeOffset.Now);
            return;
        }

        lock (cache.EvidenceByRow)
        {
            if (!cache.EvidenceByRow.TryGetValue(key, out var slot))
            {
                if (string.IsNullOrWhiteSpace(text))
                    return;
                slot = new NativeFatEvidenceSlotState();
                cache.EvidenceByRow[key] = slot;
            }

            switch (field)
            {
                case NativeFatEvidenceField.Value1:
                    slot.Value1 = text;
                    slot.Value1Evidence = null;
                    break;
                case NativeFatEvidenceField.Value2:
                    slot.Value2 = text;
                    slot.Value2Evidence = null;
                    break;
                case NativeFatEvidenceField.Result:
                    slot.Result = text;
                    break;
            }

            RemoveIfEmpty(cache, key, slot);
        }
    }

    public static void WriteCapture(
        NativeFatIedSessionCacheState cache,
        Iec61850MonitorPoint point,
        NativeFatEvidenceField field,
        string rawValue,
        FatEvidenceCaptureKind captureKind,
        DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(point);
        if (field is not (NativeFatEvidenceField.Value1 or NativeFatEvidenceField.Value2))
            throw new ArgumentOutOfRangeException(nameof(field), field, "Only Value 1 / Value 2 are structured captures.");
        if (!TryBuildRowKey(point, out var key) || string.IsNullOrWhiteSpace(rawValue))
            return;

        var slotKind = field == NativeFatEvidenceField.Value1 ? FatValueSlot.Value1 : FatValueSlot.Value2;
        var evidence = new FatValueEvidence(
            Guid.NewGuid(),
            slotKind,
            captureKind,
            rawValue.Trim(),
            capturedAt,
            IoTestValueNormalizer.ParseIedTimestamp(point.DeviceTimestamp),
            string.IsNullOrWhiteSpace(point.Quality) ? "Unknown" : point.Quality.Trim(),
            string.IsNullOrWhiteSpace(point.SourceMode) ? "Engineering live" : point.SourceMode.Trim(),
            point.Sequence,
            -1);

        lock (cache.EvidenceByRow)
        {
            if (!cache.EvidenceByRow.TryGetValue(key, out var slot))
            {
                slot = new NativeFatEvidenceSlotState();
                cache.EvidenceByRow[key] = slot;
            }

            if (field == NativeFatEvidenceField.Value1)
            {
                slot.Value1 = evidence.RawValue;
                slot.Value1Evidence = evidence;
            }
            else
            {
                slot.Value2 = evidence.RawValue;
                slot.Value2Evidence = evidence;
            }
        }
    }

    public static bool PromoteValue2ToValue1(NativeFatIedSessionCacheState cache, Iec61850MonitorPoint point)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(point);
        if (!TryBuildRowKey(point, out var key))
            return false;

        lock (cache.EvidenceByRow)
        {
            if (!cache.EvidenceByRow.TryGetValue(key, out var slot))
                return false;

            var raw = RawValue(slot.Value2Evidence, slot.Value2);
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            slot.Value1 = raw;
            slot.Value1Evidence = slot.Value2Evidence is null ? null : slot.Value2Evidence with { Slot = FatValueSlot.Value1 };
            return true;
        }
    }

    public static int MergeMissing(
        NativeFatIedSessionCacheState cache,
        IReadOnlyDictionary<string, NativeFatEvidenceSlotState> hydratedEvidence)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(hydratedEvidence);
        var mergedRows = 0;

        lock (cache.EvidenceByRow)
        {
            foreach (var pair in hydratedEvidence)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null || IsEmpty(pair.Value))
                    continue;

                var incoming = pair.Value;
                if (!cache.EvidenceByRow.TryGetValue(pair.Key, out var current))
                {
                    cache.EvidenceByRow[pair.Key] = Clone(incoming);
                    mergedRows++;
                    continue;
                }

                var changed = false;
                if (!HasValue1(current) && HasValue1(incoming))
                {
                    current.Value1 = RawValue(incoming.Value1Evidence, incoming.Value1);
                    current.Value1Evidence = incoming.Value1Evidence;
                    changed = true;
                }
                if (!HasValue2(current) && HasValue2(incoming))
                {
                    current.Value2 = RawValue(incoming.Value2Evidence, incoming.Value2);
                    current.Value2Evidence = incoming.Value2Evidence;
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(current.Result) && !string.IsNullOrWhiteSpace(incoming.Result))
                {
                    current.Result = incoming.Result;
                    changed = true;
                }
                if (changed)
                    mergedRows++;
            }
        }

        return mergedRows;
    }

    public static IReadOnlyDictionary<string, NativeFatEvidenceSlotState> Snapshot(NativeFatIedSessionCacheState cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        lock (cache.EvidenceByRow)
        {
            return cache.EvidenceByRow.ToDictionary(
                pair => pair.Key,
                pair => Clone(pair.Value),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    internal static string NormalizeIedName(string? iedName)
        => (iedName ?? string.Empty).Trim().ToLowerInvariant();

    internal static string NormalizeTelegram(string? iecTelegram)
    {
        var text = (iecTelegram ?? string.Empty)
            .Trim()
            .Replace('$', '.')
            .Replace("..", ".", StringComparison.Ordinal)
            .ToLowerInvariant();
        while (text.Contains("..", StringComparison.Ordinal))
            text = text.Replace("..", ".", StringComparison.Ordinal);
        return text.Trim('.');
    }

    private static string ResolveResult(NativeFatEvidenceSlotState slot)
    {
        if (!string.IsNullOrWhiteSpace(slot.Result))
            return slot.Result.Trim();
        return HasValue1(slot) && HasValue2(slot) ? "COMPLETE" : string.Empty;
    }

    private static string StripDisplayTimestamp(string value)
    {
        var text = value?.Trim() ?? string.Empty;
        var separator = text.LastIndexOf(" - ", StringComparison.Ordinal);
        if (separator <= 0)
            return text;

        var suffix = text[(separator + 3)..];
        return DateTime.TryParseExact(
            suffix,
            "yyyy-MM-dd HH:mm:ss.fff",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _)
            ? text[..separator].Trim()
            : text;
    }

    private static string DisplayValue(FatValueEvidence? evidence, string legacyRaw)
    {
        var raw = RawValue(evidence, legacyRaw);
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        if (evidence is null)
            return raw;
        var timestamp = evidence.IedTimestamp ?? evidence.CapturedAt;
        return $"{raw} - {timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}";
    }

    private static string TimestampValue(FatValueEvidence? evidence)
    {
        if (evidence is null)
            return string.Empty;
        var timestamp = evidence.IedTimestamp ?? evidence.CapturedAt;
        return timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }

    private static string RawValue(FatValueEvidence? evidence, string legacyRaw)
        => !string.IsNullOrWhiteSpace(evidence?.RawValue)
            ? evidence.RawValue.Trim()
            : legacyRaw?.Trim() ?? string.Empty;

    private static bool HasValue1(NativeFatEvidenceSlotState slot)
        => !string.IsNullOrWhiteSpace(RawValue(slot.Value1Evidence, slot.Value1));

    private static bool HasValue2(NativeFatEvidenceSlotState slot)
        => !string.IsNullOrWhiteSpace(RawValue(slot.Value2Evidence, slot.Value2));

    private static bool IsEmpty(NativeFatEvidenceSlotState slot)
        => !HasValue1(slot) && !HasValue2(slot) && string.IsNullOrWhiteSpace(slot.Result);

    private static NativeFatEvidenceSlotState Clone(NativeFatEvidenceSlotState source)
        => new()
        {
            Value1 = RawValue(source.Value1Evidence, source.Value1),
            Value2 = RawValue(source.Value2Evidence, source.Value2),
            Value1Evidence = source.Value1Evidence,
            Value2Evidence = source.Value2Evidence,
            Result = source.Result
        };

    private static void RemoveIfEmpty(NativeFatIedSessionCacheState cache, string key, NativeFatEvidenceSlotState slot)
    {
        if (IsEmpty(slot))
            cache.EvidenceByRow.Remove(key);
    }
}
