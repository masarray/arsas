namespace ArIED61850Tester.Models;

/// <summary>
/// Revision-only notification from the canonical ledger. This is NOT another
/// live-value store, Report decoder, event history, or IEC acquisition channel.
/// Readers always resolve the current evidence through IedBrowserLiveValues.
/// </summary>
public enum CanonicalValueChangeKind
{
    AssociationInvalidated,
    Report,
    Read,
    ReportCompanion
}

public sealed record CanonicalValueChange(
    long Revision, long Association, string Reference, string FunctionalConstraint,
    CanonicalValueChangeKind Kind);

public sealed record CanonicalValueChangeBatch(
    long ThroughRevision, long LatestRevision, bool RequiresFullRefresh,
    IReadOnlyList<CanonicalValueChange> Changes);

/// <summary>
/// Constant-memory revision ring shared by arbitrarily many independent cursor
/// readers. All methods run within the enclosing IedBrowserLiveValues gate:
/// there is no second lock, cross-thread event handler, UI callback, or queue
/// consumer that can steal another workspace's notifications.
/// </summary>
internal sealed class CanonicalValueChangeJournal
{
    private readonly CanonicalValueChange?[] _ring;
    private int _count;
    private long _latestRevision;

    internal CanonicalValueChangeJournal(int capacity = 4096)
    {
        if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
        _ring = new CanonicalValueChange[capacity];
    }

    internal void Append(CanonicalValueChange change)
    {
        if (change.Revision != _latestRevision + 1)
            throw new InvalidOperationException("Canonical revision journal must be contiguous.");
        _latestRevision = change.Revision;
        _ring[(int)(_latestRevision % _ring.Length)] = change;
        if (_count < _ring.Length) _count++;
    }

    internal CanonicalValueChangeBatch Read(long afterRevision, int maxChanges)
    {
        if (maxChanges is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(maxChanges));
        if (afterRevision < 0 || afterRevision > _latestRevision)
            return new(_latestRevision, _latestRevision, true, []);
        if (afterRevision == _latestRevision)
            return new(afterRevision, _latestRevision, false, []);

        var earliestRetained = _latestRevision - _count + 1;
        if (afterRevision < earliestRetained - 1)
            return new(_latestRevision, _latestRevision, true, []);

        var upper = Math.Min(_latestRevision, afterRevision + maxChanges);
        var changes = new CanonicalValueChange[(int)(upper - afterRevision)];
        var invalidated = false;
        for (long revision = afterRevision + 1; revision <= upper; revision++)
        {
            var change = _ring[(int)(revision % _ring.Length)]!;
            if (change.Revision != revision)
                return new(_latestRevision, _latestRevision, true, []);
            changes[(int)(revision - afterRevision - 1)] = change;
            invalidated |= change.Kind == CanonicalValueChangeKind.AssociationInvalidated;
        }
        return new(upper, _latestRevision, invalidated, changes);
    }
}
