namespace ArIED61850Tester.Models;

public enum IedBrowserDeferredReadKind
{
    NotScheduled,
    TerminalResultUnverified
}

public sealed record IedBrowserDeferredRead(
    IedBrowserReadTarget Target, IedBrowserDeferredReadKind Kind);

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
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(scheduled);
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(terminalFailure);
        if (maxTargets < 1) return [];
        var scheduledKeys = scheduled.Select(Key).ToHashSet(StringComparer.Ordinal);
        var output = new List<IedBrowserDeferredRead>();
        foreach (var target in declared.Take(maxTargets))
        {
            if (observed(target) || terminalFailure(target)) continue;
            output.Add(new IedBrowserDeferredRead(target,
                scheduledKeys.Contains(Key(target))
                    ? IedBrowserDeferredReadKind.TerminalResultUnverified
                    : IedBrowserDeferredReadKind.NotScheduled));
        }
        return output;
    }

    private static string Key(IedBrowserReadTarget target)
        => IedBrowserLiveValues.CanonicalReference(target.Reference) + "|" +
           (target.FunctionalConstraint ?? "").Trim().ToUpperInvariant();
}
