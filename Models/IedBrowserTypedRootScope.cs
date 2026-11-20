namespace ArIED61850Tester.Models;

/// <summary>
/// Pure read-only admission of exact live-schema children for the FC roots
/// already admitted by a finite selected-page plan. A 32-target plan is NOT
/// a limit on how many schema-verified children a single wire root can return.
/// This never schedules another root, FC, LD, or arbitrary SCL-only leaf.
/// </summary>
public static class IedBrowserTypedRootScope
{
    public static IReadOnlyList<IedBrowserReadTarget> Build(
        IReadOnlyList<IedBrowserReadTarget> scheduled,
        IReadOnlyList<IedBrowserReadTarget> verifiedPage,
        int maxRoots = 8, int maxSiblingsPerRoot = 512)
    {
        ArgumentNullException.ThrowIfNull(scheduled);
        ArgumentNullException.ThrowIfNull(verifiedPage);
        if (maxRoots <= 0 || maxSiblingsPerRoot <= 0 ||
            scheduled.Count == 0 || verifiedPage.Count == 0)
            return [];

        var admittedRoots = scheduled.Select(IedBrowserSgRootProjection.RootFor)
            .Where(root => root.Length != 0)
            .Distinct(StringComparer.Ordinal)
            .Take(Math.Min(8, maxRoots))
            .ToHashSet(StringComparer.Ordinal);
        if (admittedRoots.Count == 0) return [];

        // Ambiguous duplicate declarations cannot prove a wire target, even if
        // both references are text-identical. Keep exact FC identity and case.
        var valid = verifiedPage
            .Where(t => admittedRoots.Contains(IedBrowserSgRootProjection.RootFor(t)))
            .GroupBy(t => IedBrowserLiveValues.CanonicalReference(t.Reference) +
                "|" + t.FunctionalConstraint.Trim().ToUpperInvariant(),
                StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .Select(g => g.Single())
            .ToArray();

        var output = new List<IedBrowserReadTarget>();
        foreach (var root in admittedRoots.OrderBy(x => x, StringComparer.Ordinal))
        {
            var group = valid.Where(t =>
                string.Equals(IedBrowserSgRootProjection.RootFor(t),
                    root, StringComparison.Ordinal)).ToArray();
            // Do not silently take the first N children and label a partial
            // FC-root projection complete. Use the existing leaf path instead.
            if (group.Length > Math.Min(512, maxSiblingsPerRoot)) continue;
            output.AddRange(group);
        }
        return output;
    }
}
