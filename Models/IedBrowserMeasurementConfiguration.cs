using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Exact FC=CF unit companion *presentation* next to MX DataSet members.
/// These leaves are NOT inserted into DataSet membership nor inferred from
/// measurement names, Report payload or an unrelated LD/DO. Only an actual
/// ledger observation (or explicitly marked CF discovery instance snapshot)
/// supplies the displayed engineering unit.
/// </summary>
public static class IedBrowserMeasurementConfiguration
{
    public static IReadOnlyList<IedBrowserDetailRow> ForDataSet(
        LiveIedDataSetModel dataSet, LiveIedModelDiscoveryDocument? model)
    {
        if (model is null) return [];
        var mx = dataSet.Members.Where(m =>
                string.Equals(m.FunctionalConstraint, "MX", StringComparison.Ordinal))
            .Select(m => IedBrowserLiveValues.CanonicalReference(m.Reference))
            .Where(r => r.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        if (mx.Count == 0) return [];

        var result = new List<IedBrowserDetailRow>();
        foreach (var obj in model.LogicalDevices.SelectMany(ld => ld.LogicalNodes)
                     .SelectMany(ln => IedBrowserEngineeringObjects.GroupExact(ln.DataObjects)))
        {
            var owner = IedBrowserLiveValues.CanonicalReference(obj.Reference);
            if (owner.Length == 0 ||
                !mx.Any(member => member == owner ||
                    member.StartsWith(owner + ".", StringComparison.Ordinal)))
                continue;
            var unitsReference = owner + ".units";
            var leaves = obj.Attributes.Where(attr =>
                string.Equals(attr.FunctionalConstraint, "CF", StringComparison.Ordinal) &&
                (IedBrowserLiveValues.CanonicalReference(attr.MmsReference) ==
                     unitsReference + ".SIUnit" ||
                 IedBrowserLiveValues.CanonicalReference(attr.MmsReference) ==
                     unitsReference + ".multiplier"))
                .GroupBy(attr => IedBrowserLiveValues.CanonicalReference(attr.MmsReference),
                    StringComparer.Ordinal)
                // A conflicting declared instance is not safe presentation
                // authority, even if one of the declarations has a value.
                .Where(group => group.Count() == 1)
                .Select(group => group.Single())
                .OrderBy(attr => attr.MmsReference.EndsWith("$SIUnit",
                    StringComparison.Ordinal) ? 0 : 1)
                .ToArray();
            if (leaves.Length == 0) continue;

            var current = leaves;
            result.Add(new IedBrowserDetailRow("Data",
                owner + ".units [CF]", "Not read", 0,
                "Exact IEC CF configuration · not a DataSet member or MX Report value")
            {
                ValueReference = unitsReference,
                FunctionalConstraint = "CF",
                LoadChildren = () => current.Select(attr =>
                    new IedBrowserDetailRow("Data",
                        IedBrowserLiveValues.CanonicalReference(attr.MmsReference)[
                            (unitsReference.Length + 1)..] + " [CF]",
                        "Not read", 1, "Independent MMS CF attribute")
                    {
                        ValueReference = attr.MmsReference,
                        FunctionalConstraint = "CF"
                    }).ToArray()
            });
            if (result.Count >= 12) break; // bounded contextual rows
        }
        return result;
    }
}
