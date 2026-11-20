namespace ArIED61850Tester.Models;

/// <summary>
/// Bounded navigation/read admission only. This never stores MMS values,
/// quality, timestamps or derived presentation. Those belong to LiveValues.
/// Failed or skipped Dispatcher callbacks are not an acquisition attempt.
/// </summary>
public sealed class IedBrowserFocusedAdmission
{
    private string _deviceId = "";
    private long _association = -1;
    private readonly Dictionary<string, int> _attempts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _completed = new(StringComparer.Ordinal);
    private const int MaxPageKeys = 128;

    public static string PageKey(string pageKey, string? selectedDoReference)
        => pageKey + (string.IsNullOrWhiteSpace(selectedDoReference)
            ? "" : "|DO|" + selectedDoReference);

    private void EnsureAssociation(string deviceId, long association)
    {
        if (string.Equals(_deviceId, deviceId, StringComparison.Ordinal) &&
            _association == association) return;
        _deviceId = deviceId;
        _association = association;
        _attempts.Clear();
        _completed.Clear();
    }

    public bool CanStart(string deviceId, long association, string pageKey,
        int maxAttempts)
    {
        EnsureAssociation(deviceId, association);
        return maxAttempts > 0 && !_completed.Contains(pageKey) &&
            (!_attempts.TryGetValue(pageKey, out var count) ||
             count < maxAttempts);
    }

    public bool TryStart(string deviceId, long association, string pageKey,
        int maxAttempts)
    {
        if (!CanStart(deviceId, association, pageKey, maxAttempts))
            return false;
        // Cap metadata growth even for enormous LN catalogs. The live
        // observation ledger is unaffected by this admission eviction.
        if (_attempts.Count >= MaxPageKeys && !_attempts.ContainsKey(pageKey))
        {
            _attempts.Clear();
            _completed.Clear();
        }
        _attempts.TryGetValue(pageKey, out var count);
        _attempts[pageKey] = count + 1;
        return true;
    }

    public void Complete(string pageKey) => _completed.Add(pageKey);

    public int Attempts(string pageKey)
        => _attempts.TryGetValue(pageKey, out var count) ? count : 0;
}
