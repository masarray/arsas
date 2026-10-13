namespace ArIED61850Tester.Models;

/// <summary>
/// The right pane is a folder-like inspector with independent navigation
/// history. A flat category may jump directly to an RCB, so Back must restore
/// the *last viewed page*, not assume its IEC tree parent was previously viewed.
/// </summary>
public sealed class IedBrowserNavigationTrail
{
    private readonly Stack<IedBrowserTreeRow> _history = new();
    public IedBrowserTreeRow? Current { get; private set; }
    public bool CanGoBack => _history.Count != 0;

    public void SelectFromTree(IedBrowserTreeRow? node)
    {
        Current = node;
        _history.Clear();
    }

    public bool Enter(IedBrowserTreeRow target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (Current is null || ReferenceEquals(Current, target) ||
            string.Equals(Current.Key, target.Key, StringComparison.Ordinal))
            return false;
        _history.Push(Current);
        Current = target;
        return true;
    }

    public IedBrowserTreeRow? Back()
    {
        if (_history.Count == 0) return null;
        Current = _history.Pop();
        return Current;
    }
}
