using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// One in-flight read-plan local typed FC=SG/SP0 root result projection.
/// This dictionary is discarded after a bounded Read; never a value cache.
/// </summary>
internal sealed class IedBrowserSgRootReadFlight
{
    private readonly NativeIec61850Client _client;
    private readonly IReadOnlyList<IedBrowserReadTarget> _targets;
    private readonly IReadOnlyList<IedBrowserReadTarget> _verifiedRootScope;
    private readonly HashSet<string> _scheduledKeys;
    private readonly HashSet<string> _drainedRootFanout = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyDictionary<string, IedBrowserReadResult>>
        _completed = new(StringComparer.Ordinal);
    private const int MaxRootReadsPerPage = 8;

    internal IedBrowserSgRootReadFlight(NativeIec61850Client client,
        IReadOnlyList<IedBrowserReadTarget> targets,
        IReadOnlyList<IedBrowserReadTarget>? verifiedRootScope = null)
    {
        _client = client;
        _targets = targets;
        _verifiedRootScope = verifiedRootScope ?? targets;
        _scheduledKeys = targets.Select(Key).ToHashSet(StringComparer.Ordinal);
    }

    private static string Key(IedBrowserReadTarget target)
        => IedBrowserLiveValues.CanonicalReference(target.Reference) +
            "|" + target.FunctionalConstraint;

    // Emit only unscheduled siblings whose exact values have already been
    // proved by THIS wire GVA+Read result. The result inbox/association-fenced
    // canonical ledger remain the sole publication route. One-shot per root.
    internal IReadOnlyList<IedBrowserReadResult> DrainUnscheduledSiblings(
        IedBrowserReadTarget selected)
    {
        var root = IedBrowserSgRootProjection.RootFor(selected);
        if (root.Length == 0 || !_completed.TryGetValue(root, out var values) ||
            !_drainedRootFanout.Add(root))
            return [];
        return values.Where(item => !_scheduledKeys.Contains(item.Key))
            .Select(item => item.Value).ToArray();
    }

    internal async Task<IedBrowserReadResult?> TryReadAsync(
        IedBrowserReadTarget target, CancellationToken cancellationToken)
    {
        // A manual LN Read contains hundreds of CF/DC/ST leaves. The former
        // 32-total-target veto disabled typed SP0/SG0 reads exactly on those
        // dense protection LNs. Bound by page and group instead, not by the
        // number of unrelated, independently declared target attributes.
        if (_targets.Count > 4096 ||
            target.FunctionalConstraint is not ("SG" or "SP" or "CF" or "DC" or "EX"))
            return null;
        var root = IedBrowserSgRootProjection.RootFor(target);
        if (root.Length == 0) return null;
        if (!_completed.TryGetValue(root, out var values))
        {
            if (_completed.Count >= MaxRootReadsPerPage)
                return null;
            var siblings = _verifiedRootScope.Where(t =>
                string.Equals(IedBrowserSgRootProjection.RootFor(t),
                    root, StringComparison.Ordinal)).ToArray();
            // An oversized or omitted verified group cannot safely be
            // partially matched to the structural response. Restrict it to
            // the originally requested exact target list instead.
            if (siblings.Length == 0)
                siblings = _targets.Where(t =>
                    string.Equals(IedBrowserSgRootProjection.RootFor(t),
                        root, StringComparison.Ordinal)).ToArray();
            IReadOnlyList<IedBrowserReadResult> read = [];
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                if (IedBrowserSgRootProjection.CanReadRoot(siblings.Length))
                    read = await _client.ReadVerifiedSgRootAsync(
                        root, siblings, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Existing individual leaf path remains an exact FC fallback.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // No speculative values from a malformed/unavailable FC root.
                System.Diagnostics.Debug.WriteLine("IEC setting FC root read unavailable: " + ex.Message);
            }
            values = read.GroupBy(v =>
                IedBrowserLiveValues.CanonicalReference(v.Target.Reference) +
                "|" + v.Target.FunctionalConstraint, StringComparer.Ordinal)
                .Where(g => g.Count() == 1)
                .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
            _completed.Add(root, values);
        }
        var key = IedBrowserLiveValues.CanonicalReference(target.Reference) +
            "|" + target.FunctionalConstraint;
        return values.TryGetValue(key, out var result) ? result : null;
    }
}
