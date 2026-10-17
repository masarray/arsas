using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// One-shot CF/DC hydration for an expanded data-model subobject. Uses only
/// existing verified discovery attributes and an association-bound SSOT.
/// Never scans other LNs, never reads CO, never supplies guessed values.
/// </summary>
public sealed record IedBrowserVisibleConfigurationReadRequest(string Scope)
{
    public static IReadOnlyList<IedBrowserReadTarget> Build(
        LiveIedModelDiscoveryDocument? model, IedBrowserTreeRow? page,
        string scope, IedBrowserLiveValues ledger, int budget = 18)
    {
        if (model is null || page is not { Kind: "LN",
                Payload: LiveIedLogicalNodeModel } ||
            string.IsNullOrWhiteSpace(scope) || ledger.IsOffline ||
            budget <= 0) return [];

        var canonical = IedBrowserLiveValues.CanonicalReference(scope);
        var prefix = canonical + ".";
        var candidates = model.LogicalDevices
            .SelectMany(ld => ld.LogicalNodes)
            .Where(ln => ReferenceEquals(ln, page.Payload))
            .SelectMany(ln => ln.DataObjects)
            .Where(obj => canonical == IedBrowserLiveValues.CanonicalReference(obj.Reference) ||
                canonical.StartsWith(
                    IedBrowserLiveValues.CanonicalReference(obj.Reference) + ".",
                    StringComparison.Ordinal))
            .SelectMany(obj => obj.Attributes)
            .Where(a => a.FunctionalConstraint is "CF" or "DC" &&
                IedBrowserLiveValues.CanonicalReference(a.MmsReference)
                    .StartsWith(prefix, StringComparison.Ordinal))
            .Where(a => !ledger.TryGet(a.MmsReference, a.FunctionalConstraint,
                out var observed) || observed.IsStale ||
                observed.Association != ledger.Association)
            .GroupBy(a => IedBrowserLiveValues.CanonicalReference(a.MmsReference) +
                "|" + a.FunctionalConstraint, StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .Select(g => g.First())
            .OrderBy(a => Priority(IedBrowserLiveValues.CanonicalReference(a.MmsReference),
                a.FunctionalConstraint))
            .ThenBy(a => a.MmsReference, StringComparer.Ordinal)
            .Take(Math.Min(24, budget))
            .Select(a => new IedBrowserReadTarget(a.MmsReference,
                a.FunctionalConstraint,
                string.IsNullOrWhiteSpace(a.SclBType) ? a.MmsType : a.SclBType))
            .ToArray();
        return candidates;
    }

    private static int Priority(string reference, string fc)
        => fc == "CF" && (reference.EndsWith(".units.SIUnit", StringComparison.Ordinal) ||
                           reference.EndsWith(".units.multiplier", StringComparison.Ordinal))
            ? 0
            : fc == "DC" || reference.EndsWith(".db", StringComparison.Ordinal) ||
              reference.EndsWith(".dbAng", StringComparison.Ordinal) ? 1 : 2;
}
