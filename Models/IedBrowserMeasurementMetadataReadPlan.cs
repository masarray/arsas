using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Hydrates IEC measurement metadata for one EXACT selected live DataSet.
/// These CF reads enrich the existing per-IED canonical ledger; they are not
/// new DataSet members, not Report mutations, and never write the IED.
/// </summary>
public static class IedBrowserMeasurementMetadataReadPlan
{
    public static IReadOnlyList<IedBrowserReadTarget> BuildForPage(
        LiveIedModelDiscoveryDocument? model, IedBrowserTreeRow? page,
        IedBrowserLiveValues ledger, int budget = 24)
    {
        var reference = page?.Payload switch
        {
            LiveIedDataSetModel set when page.Kind == "DataSet" => set.Reference,
            RcbExportRow rcb when page.Kind == "RCB" => rcb.DataSetReference,
            _ => ""
        };
        return Build(model, reference, ledger, budget);
    }

    public static IReadOnlyList<IedBrowserReadTarget> Build(
        LiveIedModelDiscoveryDocument? model, string? dataSetReference,
        IedBrowserLiveValues ledger, int budget = 24)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (model is null || ledger.IsOffline || budget <= 0 ||
            string.IsNullOrWhiteSpace(dataSetReference)) return [];

        var identity = IedBrowserLiveValues.CanonicalReference(dataSetReference);
        var matching = model.DataSets.Where(s =>
            string.Equals(IedBrowserLiveValues.CanonicalReference(s.Reference),
                identity, StringComparison.Ordinal)).Take(2).ToArray();
        if (matching.Length != 1) return [];

        // The current MMS discovery model, not SCL, is authority for an FCDA
        // and for each CF leaf. Ambiguous duplicate live declarations fail closed.
        var attributes = model.LogicalDevices.SelectMany(ld => ld.LogicalNodes)
            .SelectMany(ln => ln.DataObjects).SelectMany(obj => obj.Attributes)
            .Where(a => !string.IsNullOrWhiteSpace(a.MmsReference)).ToArray();
        var unique = attributes.GroupBy(a =>
                IedBrowserLiveValues.CanonicalReference(a.MmsReference) + "|" +
                (a.FunctionalConstraint ?? "").Trim().ToUpperInvariant(),
                StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        var members = matching[0].Members
            .Where(m => string.Equals(m.FunctionalConstraint, "MX",
                StringComparison.OrdinalIgnoreCase))
            .Select(m => IedBrowserLiveValues.CanonicalReference(m.Reference))
            .Where(m => m.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var owners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attr in attributes)
        {
            if (!string.Equals(attr.FunctionalConstraint, "MX",
                StringComparison.OrdinalIgnoreCase)) continue;
            var reference = IedBrowserLiveValues.CanonicalReference(attr.MmsReference);
            const string suffix = ".cVal.mag.f";
            if (!reference.EndsWith(suffix, StringComparison.Ordinal)) continue;
            var phase = reference[..^suffix.Length];
            // Member scope includes exact FCD and FCDA subobjects, but not
            // other logical nodes, siblings, or similarly prefixed objects.
            if (members.Any(m => phase == m ||
                    phase.StartsWith(m + ".", StringComparison.Ordinal) ||
                    m.StartsWith(phase + ".", StringComparison.Ordinal)))
                owners.Add(phase);
        }

        var targets = new List<IedBrowserReadTarget>();
        foreach (var owner in owners.OrderBy(x => x, StringComparer.Ordinal))
        {
            var si = owner + ".units.SIUnit|CF";
            var multiplier = owner + ".units.multiplier|CF";
            // Never publish half of a schema-unverified engineering unit.
            if (!unique.TryGetValue(si, out var siAttr) ||
                !unique.TryGetValue(multiplier, out var mulAttr)) continue;
            foreach (var a in new[] { siAttr, mulAttr })
            {
                if (ledger.TryGet(a.MmsReference, "CF", out var value) &&
                    !value.IsStale && value.Association == ledger.Association)
                    continue;
                targets.Add(new IedBrowserReadTarget(a.MmsReference, "CF",
                    string.IsNullOrWhiteSpace(a.SclBType) ? a.MmsType : a.SclBType));
                if (targets.Count >= budget) return targets;
            }
        }
        return targets;
    }
}
