using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// An IEC DO without a single model-proven scalar may still contain observed
/// leaves. Summarize actual evidence *presence*, never synthesize a DO scalar
/// from arbitrary CF/DC children. Per-row bounded exact-reference plan is
/// constructed once; refresh performs at most 24 O(1) ledger lookups.
/// </summary>
public static class IedBrowserObservedParentPresentation
{
    private const int MaxCandidates = 24;

    public static IReadOnlyList<IedBrowserReadTarget> BuildCandidates(
        LiveIedDataObjectModel obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var owner = IedBrowserLiveValues.CanonicalReference(obj.Reference);
        if (owner.Length == 0) return [];

        // FC=SE is an editable setting-group shadow, not proof of the active
        // FC=SG configuration. CO service structures are not process reads.
        var declarations = obj.Attributes
            .Select(attr => new IedBrowserReadTarget(
                IedBrowserLiveValues.CanonicalReference(attr.MmsReference),
                (attr.FunctionalConstraint ?? "").Trim().ToUpperInvariant(),
                attr.MmsType ?? ""))
            .Where(candidate => candidate.Reference.StartsWith(
                    owner + ".", StringComparison.Ordinal) &&
                candidate.FunctionalConstraint.Length > 0 &&
                candidate.FunctionalConstraint is not ("SE" or "CO"))
            .DistinctBy(candidate => candidate.Reference + "|" +
                candidate.FunctionalConstraint, StringComparer.Ordinal)
            .OrderBy(candidate => candidate.FunctionalConstraint, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Reference, StringComparer.Ordinal)
            .Take(MaxCandidates)
            .ToArray();
        return declarations;
    }

    public static bool TrySummarize(
        IedBrowserDetailRow parent, IedBrowserLiveValues ledger,
        bool preferReport, out string label, out string source, out string tooltip)
    {
        label = "";
        source = "";
        tooltip = "";
        if (!parent.IsDataObject || parent.ParentObservedChildCandidates.Count == 0)
            return false;
        var current = ledger.Association;
        var found = 0;
        var stale = false;
        var samples = new List<string>(capacity: 3);
        foreach (var target in parent.ParentObservedChildCandidates)
        {
            if (!ledger.TryGet(target.Reference, target.FunctionalConstraint,
                    out var observed, preferReport: preferReport) ||
                observed.Association != current)
                continue;
            found++;
            stale |= observed.IsStale;
            if (samples.Count < 3)
                samples.Add(target.Reference + " [" + target.FunctionalConstraint +
                    "] · " + observed.Source);
        }
        if (found == 0) return false;
        // A partial/capped presence check NEVER claims all children were read
        // or reports an arbitrary scalar/unit as the entire DO value.
        label = stale ? "Offline · child values observed" : "Child values observed";
        source = stale ? "Stale exact child evidence" : "Exact child evidence";
        tooltip = "No unique IEC DO scalar; observed exact model-declared " +
            "child evidence only (no invented parent value). " +
            string.Join("; ", samples);
        return true;
    }
}
