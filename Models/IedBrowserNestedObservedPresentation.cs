namespace ArIED61850Tester.Models;

/// <summary>
/// Display-only exact child reuse for one IEC phase DO whose own structure
/// has no scalar MMS leaf. No read, cache, synthetic phase or angle inference.
/// </summary>
public static class IedBrowserNestedObservedPresentation
{
    public static bool TryCompose(
        IedBrowserDetailRow parent, IedBrowserLiveValues ledger,
        IReadOnlyDictionary<string, string?> units, bool monitoring,
        out IedBrowserCompositeObservation observed)
    {
        observed = null!;
        if (!parent.IsDataObject ||
            !string.Equals(parent.FunctionalConstraint, "MX", StringComparison.Ordinal) ||
            parent.Name is not ("phsA" or "phsB" or "phsC" or "neut" or "net" or "res") ||
            !parent.HasChildren)
            return false;
        // One exact cVal owner, never instCVal, sibling phasors or guessed FC.
        var candidates = parent.GetChildren().Where(child =>
            child.Name == "cVal [MX]" &&
            string.Equals(child.FunctionalConstraint, "MX", StringComparison.Ordinal))
            .Take(2).ToArray();
        if (candidates.Length != 1) return false;
        var child = candidates[0];
        if (child.PolarPlan is null && child.PhasePlans.Count == 0 &&
            child.VectorComponents.Count == 0)
            return false;
        return IedBrowserEngineeringComposite.TryCompose(
            child, ledger, units, monitoring, out observed);
    }
}
