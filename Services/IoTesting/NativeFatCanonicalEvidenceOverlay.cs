using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services.IoTesting;

public enum NativeFatEvidenceField
{
    Value1,
    Value2,
    Result
}

/// <summary>
/// Sparse FAT-only evidence keyed by the stable IEC 61850 identity of the canonical
/// Engineering row: IEDName + IEC Telegram. DeviceId, row index, selected index and
/// display labels are deliberately excluded so evidence cannot jump to another signal
/// after reordering or recreation of the Engineering runtime device.
/// </summary>
public static class NativeFatCanonicalEvidenceOverlay
{
    public static string BuildRowKey(Iec61850MonitorPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        // P4A compatibility note: point.PointKey is intentionally not used here because
        // it contains the runtime DeviceId and is not stable across Engineering recreation.
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
                NativeFatEvidenceField.Value1 => slot.Value1,
                NativeFatEvidenceField.Value2 => slot.Value2,
                NativeFatEvidenceField.Result => slot.Result,
                _ => string.Empty
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

        // Fail closed: a canonical FAT row without both IEDName and IEC Telegram has no
        // stable identity. Never fall back to DeviceId, row position or SignalName.
        if (!TryBuildRowKey(point, out var key))
            return;

        var text = value ?? string.Empty;

        lock (cache.EvidenceByRow)
        {
            if (!cache.EvidenceByRow.TryGetValue(key, out var slot))
            {
                // Reading/clearing an untouched cell must not allocate evidence.
                if (string.IsNullOrWhiteSpace(text))
                    return;

                slot = new NativeFatEvidenceSlotState();
                cache.EvidenceByRow[key] = slot;
            }

            switch (field)
            {
                case NativeFatEvidenceField.Value1:
                    slot.Value1 = text;
                    break;
                case NativeFatEvidenceField.Value2:
                    slot.Value2 = text;
                    break;
                case NativeFatEvidenceField.Result:
                    slot.Result = text;
                    break;
            }

            RemoveIfEmpty(cache, key, slot);
        }
    }

    /// <summary>
    /// P2/P4A merge rule: persisted evidence may fill missing cells but may never overwrite
    /// evidence captured after hydration started. Hydration has already resolved every key
    /// to one unique canonical IEDName + IEC Telegram identity before this method is called.
    /// </summary>
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
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;

                var incoming = pair.Value;
                if (incoming == null ||
                    (string.IsNullOrWhiteSpace(incoming.Value1) &&
                     string.IsNullOrWhiteSpace(incoming.Value2) &&
                     string.IsNullOrWhiteSpace(incoming.Result)))
                {
                    continue;
                }

                if (!cache.EvidenceByRow.TryGetValue(pair.Key, out var current))
                {
                    cache.EvidenceByRow[pair.Key] = Clone(incoming);
                    mergedRows++;
                    continue;
                }

                var changed = false;
                if (string.IsNullOrWhiteSpace(current.Value1) && !string.IsNullOrWhiteSpace(incoming.Value1))
                {
                    current.Value1 = incoming.Value1;
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(current.Value2) && !string.IsNullOrWhiteSpace(incoming.Value2))
                {
                    current.Value2 = incoming.Value2;
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

    public static IReadOnlyDictionary<string, NativeFatEvidenceSlotState> Snapshot(
        NativeFatIedSessionCacheState cache)
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

    private static NativeFatEvidenceSlotState Clone(NativeFatEvidenceSlotState source)
        => new()
        {
            Value1 = source.Value1,
            Value2 = source.Value2,
            Result = source.Result
        };

    private static void RemoveIfEmpty(
        NativeFatIedSessionCacheState cache,
        string key,
        NativeFatEvidenceSlotState slot)
    {
        // Keep the overlay genuinely sparse. Clearing the last evidence value removes
        // the entry rather than leaving a shadow row behind.
        if (string.IsNullOrWhiteSpace(slot.Value1) &&
            string.IsNullOrWhiteSpace(slot.Value2) &&
            string.IsNullOrWhiteSpace(slot.Result))
        {
            cache.EvidenceByRow.Remove(key);
        }
    }
}
