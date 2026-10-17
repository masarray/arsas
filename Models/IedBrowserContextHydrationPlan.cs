using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Finite, focus-scoped read-only acquisition for an operator's current Browser
/// page. Only the current LIVE MMS discovery catalog authorizes targets. SCL
/// schema may describe an unread value but must never authorize wire traffic.
/// No Read All, command FC, edit group, GI, RCB activation or cyclic polling.
/// </summary>
public static class IedBrowserContextHydrationPlan
{
    private static readonly HashSet<string> SafeFc = new(
        ["ST", "MX", "CF", "DC", "EX", "SG", "SP"], StringComparer.Ordinal);

    public static IReadOnlyList<IedBrowserReadTarget> Build(
        LiveIedModelDiscoveryDocument model,
        IedBrowserTreeRow page,
        IedBrowserLiveValues ledger,
        int maxTargets,
        string? focusedDo = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(ledger);
        if (maxTargets <= 0 || ledger.IsOffline)
            return [];

        IEnumerable<IedBrowserReadTarget> candidates;
        if (page.Kind == "SGCB" && page.Payload is LiveIedControlBlockModel sgcb)
        {
            // The original planner independently authenticates live SGCB
            // inventory. Do not use SCL-only or guessed SGCB field references.
            var control = IedBrowserControlStateEvidence.ReadTargets(sgcb, "SGCB");
            var activeSettings = model.LogicalDevices
                .Where(ld => string.Equals(ld.MmsDomain, sgcb.Domain,
                    StringComparison.Ordinal))
                .SelectMany(ld => ld.LogicalNodes)
                .SelectMany(ln => ln.DataObjects)
                .SelectMany(obj => obj.Attributes)
                .Where(attr => string.Equals(attr.FunctionalConstraint, "SG",
                    StringComparison.Ordinal))
                .Select(ToTarget);
            candidates = control.Concat(activeSettings);
        }
        else if (page.Payload is LiveIedLogicalNodeModel ln)
        {
            candidates = ln.DataObjects
                .Where(obj => focusedDo is null ||
                    string.Equals(obj.Reference, focusedDo, StringComparison.Ordinal))
                .SelectMany(obj => obj.Attributes)
                .Where(attr => SafeFc.Contains(attr.FunctionalConstraint))
                .Select(ToTarget);
        }
        else if (page.Payload is LiveIedDataSetModel dataSet)
        {
            // Include CF unit declarations only as separate exact measurement
            // configuration context; MX Report payload cannot supply CF.
            var members = dataSet.Members
                .Where(m => string.Equals(m.FunctionalConstraint, "MX",
                    StringComparison.Ordinal)).Select(m =>
                    IedBrowserLiveValues.CanonicalReference(m.Reference))
                .Where(r => r.Length > 0).ToArray();
            candidates = model.LogicalDevices.SelectMany(ld => ld.LogicalNodes)
                .SelectMany(ln => IedBrowserEngineeringObjects.GroupExact(ln.DataObjects))
                .Where(obj => members.Any(member =>
                    member == obj.Reference ||
                    member.StartsWith(obj.Reference + ".", StringComparison.Ordinal)))
                .SelectMany(obj =>
                {
                    // Exact MX member ancestors only. PhsA must never read
                    // phsB.units simply because both share the same DO.
                    var contexts = new HashSet<string>(StringComparer.Ordinal)
                        { IedBrowserLiveValues.CanonicalReference(obj.Reference) };
                    foreach (var member in members.Where(m =>
                                 m.StartsWith(obj.Reference + ".", StringComparison.Ordinal)))
                    {
                        for (var cursor = member; cursor.Length > obj.Reference.Length;)
                        {
                            contexts.Add(cursor);
                            var dot = cursor.LastIndexOf('.');
                            if (dot <= obj.Reference.Length) break;
                            cursor = cursor[..dot];
                        }
                    }
                    return obj.Attributes.Where(attr =>
                        string.Equals(attr.FunctionalConstraint, "CF",
                            StringComparison.Ordinal) &&
                        contexts.Any(owner =>
                            IedBrowserLiveValues.CanonicalReference(attr.MmsReference) ==
                                owner + ".units.SIUnit" ||
                            IedBrowserLiveValues.CanonicalReference(attr.MmsReference) ==
                                owner + ".units.multiplier"));
                })
                .Select(ToTarget);
        }
        else
            return [];

        var association = ledger.Association;
        return candidates
            .Where(target => target.Reference.Length != 0 &&
                SafeFc.Contains(target.FunctionalConstraint))
            .Select(target => new { Target = target,
                Canonical = IedBrowserLiveValues.CanonicalReference(target.Reference) })
            .Where(x => x.Canonical.Length > 0)
            .GroupBy(x => x.Canonical + "|" + x.Target.FunctionalConstraint,
                StringComparer.Ordinal)
            // Conflicting live DA declarations cannot authorize a read.
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .Where(x => !ledger.TryGet(x.Canonical, x.Target.FunctionalConstraint,
                out var observed) || observed.IsStale ||
                observed.Association != association)
            .OrderBy(x => Priority(x.Canonical, x.Target.FunctionalConstraint))
            .ThenBy(x => x.Canonical, StringComparer.Ordinal)
            .Take(maxTargets)
            .Select(x => x.Target)
            .ToArray();
    }

    private static IedBrowserReadTarget ToTarget(LiveIedDataAttributeModel attribute)
        => new(attribute.MmsReference, attribute.FunctionalConstraint,
            string.IsNullOrWhiteSpace(attribute.SclBType)
                ? attribute.MmsType : attribute.SclBType);

    private static int Priority(string reference, string fc)
    {
        if (fc == "ST" && reference.EndsWith(".stVal", StringComparison.Ordinal))
            return 0;
        if (fc == "ST" && (reference.EndsWith(".q", StringComparison.Ordinal) ||
                            reference.EndsWith(".t", StringComparison.Ordinal)))
            return 1;
        if (reference.EndsWith(".NumOfSG", StringComparison.Ordinal) ||
            reference.EndsWith(".ActSG", StringComparison.Ordinal))
            return 0;
        if (fc == "SG") return 2;
        if (fc == "CF" && (reference.EndsWith(".units.SIUnit", StringComparison.Ordinal) ||
                            reference.EndsWith(".units.multiplier", StringComparison.Ordinal)))
            return 3;
        if (fc == "MX" && reference.EndsWith(".mag.f", StringComparison.Ordinal))
            return 4;
        if (fc == "MX" && (reference.EndsWith(".q", StringComparison.Ordinal) ||
                            reference.EndsWith(".t", StringComparison.Ordinal)))
            return 5;
        return 6;
    }
}
