using System.Globalization;

namespace ArIED61850Tester.Services;

/// <summary>
/// Association-local, per-point SOE repeat protection. The exact same
/// process timestamp and discrete value may be published again by another
/// configured BRCB (or after buffered chronology is replayed).
///
/// Only an actual routed wire data-change with a parseable IED process t can
/// be deduplicated. Untimestamped, uncertain, GI/integrity and differently
/// valued/timestamped edges remain visible. It never drops Report frames,
/// changes RCB continuity, or suppresses the live point image.
/// </summary>
public sealed class Iec61850SoeRepeatGuard
{
    private const int MaxRecentIdentities = 32;
    private readonly Queue<string> _order = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public int TrackedIdentityCount => _seen.Count;

    public bool Admit(
        bool routedReport, bool wireFrame, string? reason, string? sourceMode,
        string? processTimestamp, string? newValue)
    {
        // Do not collapse a fast Open/Close/Open sequence when the IED only
        // reports seconds precision. Duplicate identity requires at least a
        // millisecond-resolved process timestamp, as in SG7's wire-derived t.
        var stamp = processTimestamp?.Trim() ?? string.Empty;
        var decimalAt = stamp.IndexOf('.');
        var hasMillisecondResolution = decimalAt >= 0 &&
            decimalAt + 3 < stamp.Length &&
            char.IsDigit(stamp[decimalAt + 1]) &&
            char.IsDigit(stamp[decimalAt + 2]) &&
            char.IsDigit(stamp[decimalAt + 3]);
        if (!hasMillisecondResolution)
            return true;
        if (!routedReport || !wireFrame ||
            Iec61850EventOriginPolicy.Cause(reason, sourceMode) != "Data change" ||
            string.IsNullOrWhiteSpace(processTimestamp) ||
            string.IsNullOrWhiteSpace(newValue) ||
            !DateTimeOffset.TryParse(processTimestamp, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var instant))
            return true;

        // Per-point lifetime provides IED identity and IEC/FC isolation.
        // Canonical UTC ticks avoid duplicate rows with differing formatting
        // of the SAME recorded physical transition.
        var identity = instant.UtcTicks.ToString(CultureInfo.InvariantCulture) +
                       "|" + newValue.Trim().ToUpperInvariant();
        if (!_seen.Add(identity))
            return false;

        _order.Enqueue(identity);
        if (_order.Count > MaxRecentIdentities)
            _seen.Remove(_order.Dequeue());
        return true;
    }
}
