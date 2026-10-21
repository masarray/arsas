using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// One explicit Browser Read flight only, with a hard read budget and
/// negative-result caching per exact FC+DO. Never persists beyond this
/// operator action, never tries another DO or another FC as a fallback.
/// </summary>
internal sealed class IedBrowserTypedDoReadFlight
{
    private const int MaximumParentAttempts = 24;
    private readonly NativeIec61850Client _client;
    private readonly Dictionary<string, IReadOnlyList<IedBrowserReadTarget>> _verified;
    private readonly Dictionary<string, IReadOnlyDictionary<string, IedBrowserReadResult>>
        _completed = new(StringComparer.Ordinal);
    private int _attemptCount;

    public IedBrowserTypedDoReadFlight(
        NativeIec61850Client client,
        IReadOnlyList<IedBrowserReadTarget> verifiedScope)
    {
        _client = client;
        _verified = verifiedScope
            .GroupBy(IedBrowserVerifiedDoProjection.ParentFor, StringComparer.Ordinal)
            .Where(g => g.Key.Length > 0 && g.Count() <= 128)
            .ToDictionary(g => g.Key,
                g => (IReadOnlyList<IedBrowserReadTarget>)g
                    .DistinctBy(t => Key(t), StringComparer.Ordinal)
                    .ToArray(), StringComparer.Ordinal);
    }

    private static string Key(IedBrowserReadTarget t)
        => IedBrowserLiveValues.CanonicalReference(t.Reference) +
           "|" + t.FunctionalConstraint.Trim().ToUpperInvariant();

    public async Task<IedBrowserReadResult?> TryReadAsync(
        IedBrowserReadTarget failedLeaf, CancellationToken cancellationToken)
    {
        var parent = IedBrowserVerifiedDoProjection.ParentFor(failedLeaf);
        if (parent.Length == 0 || !_verified.TryGetValue(parent, out var children) ||
            !children.Any(t => Key(t) == Key(failedLeaf)))
            return null;

        if (!_completed.TryGetValue(parent, out var projected))
        {
            if (_attemptCount >= MaximumParentAttempts) return null;
            _attemptCount++;
            IReadOnlyList<IedBrowserReadResult> observed = [];
            try
            {
                // One linked budget includes GVA + one parent Read together.
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(3));
                observed = await _client.ReadVerifiedDoParentAsync(
                    parent, children, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The attempted parent is cached negatively. Never repeat
                // the same timeout for every leaf within that DO.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine(
                    "IEC typed DO read unavailable (no value claimed): " + ex.Message);
            }
            projected = observed
                .GroupBy(x => Key(x.Target), StringComparer.Ordinal)
                .Where(g => g.Count() == 1)
                .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
            _completed.Add(parent, projected);
        }
        return projected.TryGetValue(Key(failedLeaf), out var value) ? value : null;
    }
}
