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
