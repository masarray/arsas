namespace ArIED61850Tester.Services;

public sealed record GooseProcessDelta(string Reference, string Value, bool IsVerified);

/// <summary>
/// Coalesces only an identical, explicitly identified value transition reflected
/// by a second GOOSE stream from the same station within a short wire-time window.
/// Never affects capture counts, protocol state or source stream histories.
/// </summary>
public sealed class GooseCrossStreamEventPolicy
{
    private readonly Dictionary<string,(string Stream, DateTimeOffset Time)> _recent =
        new(StringComparer.OrdinalIgnoreCase);

    public static string? Identity(string sourceMac, IReadOnlyList<GooseProcessDelta> changes)
    {
        if (changes.Count != 1 || !changes[0].IsVerified ||
            string.IsNullOrWhiteSpace(sourceMac)) return null;
        var item = changes[0];
        var path = item.Reference.Trim().Replace('$','.');
        var slash = path.LastIndexOf('/');
        var pos = path.IndexOf(".ST.", slash+1, StringComparison.OrdinalIgnoreCase);
        if (pos >= 0) path = path.Remove(pos,3);
        if (!path.EndsWith(".stVal",StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".mag.f",StringComparison.OrdinalIgnoreCase))
            return null;
        // Never assume an abbreviated LN name globally identifies a publisher.
        if (path.LastIndexOf('/') < 1) return null;
        return sourceMac+"|"+path.ToUpperInvariant()+"|"+item.Value;
    }

    public bool IsMirror(string? identity,string streamKey,DateTimeOffset timestamp)
    {
        if (identity is null) return false;
        if (_recent.TryGetValue(identity,out var seen) &&
            !seen.Stream.Equals(streamKey,StringComparison.OrdinalIgnoreCase) &&
            timestamp>=seen.Time && timestamp-seen.Time<=TimeSpan.FromMilliseconds(250))
            return true;
        _recent[identity]=(streamKey,timestamp);
        // Work is O(1) per accepted event; bounded occasional cleanup only.
        if (_recent.Count>256)
        {
            foreach(var expired in _recent.Where(pair =>
                timestamp-pair.Value.Time>TimeSpan.FromSeconds(1)).Select(pair=>pair.Key).ToArray())
                _recent.Remove(expired);
            if (_recent.Count>256) _recent.Clear();
        }
        return false;
    }

    public void Reset() => _recent.Clear();
}
