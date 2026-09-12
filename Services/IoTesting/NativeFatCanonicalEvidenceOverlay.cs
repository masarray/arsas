using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services.IoTesting;

public enum NativeFatEvidenceField
{
    Value1,
    Value2,
    Result
}

/// <summary>
/// Sparse FAT-only evidence keyed by the canonical Engineering live-row identity.
/// It deliberately never owns or clones IEC 61850 rows: the row object remains
/// Iec61850MonitorPoint and this service stores only operator evidence.
/// </summary>
public static class NativeFatCanonicalEvidenceOverlay
{
    public static string BuildRowKey(Iec61850MonitorPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        // Engineering already defines PointKey as DeviceId + normalized IEC reference.
        // Reuse that identity verbatim so FAT cannot invent a second semantic key space.
        if (!string.IsNullOrWhiteSpace(point.IecReference))
            return point.PointKey;

        // Defensive fallback for non-canonical/manual monitor rows. Automatic static
        // DataSet FAT is expected to take the PointKey path above.
        if (!string.IsNullOrWhiteSpace(point.IecTelegram))
            return $"{point.DeviceId}|{point.IecTelegram.Trim()}";

        return $"{point.DeviceId}|{point.SignalName.Trim()}|{point.IecDataType.Trim()}";
    }

    public static string Read(
        NativeFatIedSessionCacheState cache,
        Iec61850MonitorPoint point,
        NativeFatEvidenceField field)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(point);

        lock (cache.EvidenceByRow)
        {
            if (!cache.EvidenceByRow.TryGetValue(BuildRowKey(point), out var slot))
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

        var key = BuildRowKey(point);
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
    /// P2 merge rule: persisted evidence may fill missing cells but may never overwrite
    /// evidence captured after hydration started. This lets Start FAT remain usable while
    /// disk hydration is still completing.
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
