namespace ArIED61850Tester.Models;

public enum IedBrowserDeferredReadKind
{
    NotScheduled,
    TerminalResultUnverified
}

public sealed record IedBrowserDeferredRead(
    IedBrowserReadTarget Target, IedBrowserDeferredReadKind Kind);

/// <summary>
/// One bounded, metadata-only summary of the exact selected Read scope.
/// A queued target is NOT proof that an MMS request reached the wire.
/// Observed values are read only from the caller's association-scoped ledger.
/// </summary>
public sealed record IedBrowserReadCoverageSummary(
    int ConsideredTargets,
    int ObservedTargets,
    int TerminalFailures,
    IReadOnlyList<IedBrowserDeferredRead> Gaps,
    bool Truncated);

/// <summary>
/// Describes read-coverage gaps on the operator's exact selection only.
/// Queuing a variable is NOT evidence that an MMS Request was put on the wire.
/// No network IO, duplicate observations or inferred FC/LD aliases.
/// </summary>
public static class IedBrowserReadCompletionAccounting
{
    public static IReadOnlyList<IedBrowserDeferredRead> Classify(
        IReadOnlyList<IedBrowserReadTarget> declared,
        IReadOnlyList<IedBrowserReadTarget> scheduled,
        Func<IedBrowserReadTarget, bool> observed,
        Func<IedBrowserReadTarget, bool> terminalFailure,
        int maxTargets = 4096)
        => Summarize(declared, scheduled, observed, terminalFailure, maxTargets).Gaps;

    /// <summary>
    /// Count each exact FC target once, with observation preceding terminal
    /// failure. Interrupted/unknown terminal results must be passed as false
    /// to terminalFailure; the caller's absence ledger preserves their state.
    /// </summary>
    public static IedBrowserReadCoverageSummary Summarize(
        IReadOnlyList<IedBrowserReadTarget> declared,
        IReadOnlyList<IedBrowserReadTarget> scheduled,
        Func<IedBrowserReadTarget, bool> observed,
        Func<IedBrowserReadTarget, bool> terminalFailure,
        int maxTargets = 4096)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(scheduled);
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(terminalFailure);
        if (maxTargets < 1)
            return new IedBrowserReadCoverageSummary(0, 0, 0, [], declared.Count > 0);

        var considered = Math.Min(declared.Count, maxTargets);
        var scheduledKeys = scheduled.Select(Key).ToHashSet(StringComparer.Ordinal);
        var gaps = new List<IedBrowserDeferredRead>();
        var observedCount = 0;
        var failedCount = 0;
        for (var i = 0; i < considered; i++)
        {
            var target = declared[i];
            if (observed(target))
            {
                observedCount++;
                continue;
            }
            if (terminalFailure(target))
            {
                failedCount++;
                continue;
            }
            gaps.Add(new IedBrowserDeferredRead(target,
                scheduledKeys.Contains(Key(target))
                    ? IedBrowserDeferredReadKind.TerminalResultUnverified
                    : IedBrowserDeferredReadKind.NotScheduled));
        }
        return new IedBrowserReadCoverageSummary(
            considered, observedCount, failedCount, gaps, declared.Count > considered);
    }

    private static string Key(IedBrowserReadTarget target)
        => IedBrowserLiveValues.CanonicalReference(target.Reference) + "|" +
           (target.FunctionalConstraint ?? "").Trim().ToUpperInvariant();
}
