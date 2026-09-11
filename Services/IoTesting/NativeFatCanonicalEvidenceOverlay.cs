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
