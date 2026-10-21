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
    // Exact verified roots are compiled ONCE per user Read. Repeatedly
    // rescanning 4,000+ leaves for every root used to punish dense IEDs.
    private readonly IReadOnlyList<string> _orderedRoots;
    private readonly Dictionary<string, IReadOnlyList<IedBrowserReadTarget>> _rootTargets;
    private readonly HashSet<string> _drainedRootFanout = new(StringComparer.Ordinal);
    private readonly HashSet<string> _groupAttemptedRoots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyDictionary<string, IedBrowserReadResult>>
        _completed = new(StringComparer.Ordinal);
    // Automatic focused slices retain the tight 8-root bound. An explicit
    // operator Read of a large selected LN/SGCB can traverse further typed
    // FC roots without degenerating into hundreds of leaf fallback reads.
    private int MaxRootReadsPerPage => _targets.Count > 32 ? 128 : 8;

    internal IedBrowserSgRootReadFlight(NativeIec61850Client client,
        IReadOnlyList<IedBrowserReadTarget> targets,
        IReadOnlyList<IedBrowserReadTarget>? verifiedRootScope = null)
    {
        _client = client;
        _targets = targets;
        _verifiedRootScope = verifiedRootScope ?? targets;
        _scheduledKeys = targets.Select(Key).ToHashSet(StringComparer.Ordinal);
        _orderedRoots = targets.Select(IedBrowserSgRootProjection.RootFor)
            .Where(root => root.Length > 0)
            .Distinct(StringComparer.Ordinal).ToArray();
        _rootTargets = _verifiedRootScope
            .GroupBy(IedBrowserSgRootProjection.RootFor, StringComparer.Ordinal)
            .Where(group => group.Key.Length > 0 &&
                IedBrowserSgRootProjection.CanReadRoot(group.Count()))
            .ToDictionary(group => group.Key,
                group => (IReadOnlyList<IedBrowserReadTarget>)group.ToArray(),
                StringComparer.Ordinal);
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

    private async Task WarmupGroupedRootsAsync(
        IedBrowserReadTarget requested, CancellationToken cancellationToken)
    {
        var requestedRoot = IedBrowserSgRootProjection.RootFor(requested);
        if (requestedRoot.Length == 0 ||
            _groupAttemptedRoots.Contains(requestedRoot))
            return;
        // Traverse successive groups of FIVE explicit, already-scheduled
        // roots. Otherwise the old first-five-only warmup makes every later
        // SGCB root fall back to slow, repeated leaf MMS Read requests.
        var roots = new[] { requestedRoot }
            .Concat(_orderedRoots)
            .Where(root => root.Length != 0 &&
                !_groupAttemptedRoots.Contains(root))
            .Distinct(StringComparer.Ordinal)
            .Take(Math.Max(0, Math.Min(5,
                MaxRootReadsPerPage - _completed.Count))).ToArray();
        foreach (var root in roots) _groupAttemptedRoots.Add(root);

        // Only roots already scheduled in this finite page may enter a group.
        // No speculative LN/FC enumeration, no SCL-only sibling admission.
        var groups = roots
            .Where(_rootTargets.ContainsKey)
            .Select(root => (Root: root, Targets: _rootTargets[root]))
            .ToArray();
        if (groups.Length < 2) return;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var read = await _client.ReadVerifiedSgRootsAsync(
                groups, timeout.Token).ConfigureAwait(false);
            foreach (var (root, projected) in read)
            {
                _completed[root] = projected.GroupBy(result => Key(result.Target),
                    StringComparer.Ordinal)
                    .Where(g => g.Count() == 1)
                    .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Continue with existing exact single-root/leaf fallback.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine(
                "IEC grouped FC root MMS read unavailable: " + ex.Message);
        }
    }

    internal async Task<IedBrowserReadResult?> TryReadAsync(
        IedBrowserReadTarget target, CancellationToken cancellationToken)
    {
        // A manual LN Read contains hundreds of CF/DC/ST leaves. The former
        // 32-total-target veto disabled typed SP0/SG0 reads exactly on those
        // dense protection LNs. Bound by page and group instead, not by the
        // number of unrelated, independently declared target attributes.
        // Admission is bounded per EXACT typed FC root, not by the number
        // of unrelated ST/MX leaves in the selected IED. The former >4096
        // all-or-nothing veto disabled safe root reads on large substations.
        if (target.FunctionalConstraint is not ("SG" or "SP" or "CF" or "DC" or "EX"))
            return null;
        var root = IedBrowserSgRootProjection.RootFor(target);
        if (root.Length == 0) return null;
        await WarmupGroupedRootsAsync(target, cancellationToken).ConfigureAwait(false);
        if (!_completed.TryGetValue(root, out var values))
        {
            if (_completed.Count >= MaxRootReadsPerPage)
                return null;
            // Never substitute a truncated subset of a verified root: GVA
            // structural projection needs the entire exact source group.
            // Missing/oversized groups safely fall back to leaf Reads.
            var siblings = _rootTargets.GetValueOrDefault(root) ??
                Array.Empty<IedBrowserReadTarget>();
            IReadOnlyList<IedBrowserReadResult> read = [];
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                if (IedBrowserSgRootProjection.CanReadRoot(siblings.Count))
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
