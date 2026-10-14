using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Explicit, non-cyclic read plans. Only model-proven FC leaf attributes are allowed;
/// controls are never operated, created, reserved, enabled or written.
/// </summary>
public static class IedBrowserReadPlan
{
    private static readonly HashSet<string> ReadableFc = new(
        ["ST", "MX", "CF", "DC", "SP", "SG", "SE", "EX"], StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<IedBrowserReadTarget> Build(
        LiveIedModelDiscoveryDocument? model, IedBrowserTreeRow? page, bool readAll)
    {
        if (model is null) return Array.Empty<IedBrowserReadTarget>();
        var schema = new IedBrowserSchemaIndex(model);
        var all = model.LogicalDevices.SelectMany(ld => ld.LogicalNodes)
            .SelectMany(ln => ln.DataObjects)
            .SelectMany(obj => obj.Attributes)
            .Where(a => ReadableFc.Contains(a.FunctionalConstraint))
            .ToArray();

        IEnumerable<LiveIedDataAttributeModel> selected = all;
        if (!readAll)
        {
            selected = page?.Payload switch
            {
                LiveIedDataAttributeModel attribute => all.Where(a =>
                    string.Equals(a.MmsReference, attribute.MmsReference, StringComparison.Ordinal) &&
                    string.Equals(a.FunctionalConstraint, attribute.FunctionalConstraint,
                        StringComparison.OrdinalIgnoreCase)),
                LiveIedDataObjectModel obj => obj.Attributes,
                LiveIedLogicalNodeModel ln => ln.DataObjects.SelectMany(o => o.Attributes),
                LiveIedLogicalDeviceModel ld => ld.LogicalNodes.SelectMany(ln => ln.DataObjects)
                    .SelectMany(o => o.Attributes),
                LiveIedDataSetModel set => ForDataSet(all, schema, set),
                LiveIedControlBlockModel block when !string.IsNullOrWhiteSpace(block.DataSetReference) =>
                    ForDataSet(all, schema, FindSet(model, block.DataSetReference)),
                RcbExportRow rcb when !string.IsNullOrWhiteSpace(rcb.DataSetReference) =>
                    ForDataSet(all, schema, FindSet(model, rcb.DataSetReference)),
                _ => Array.Empty<LiveIedDataAttributeModel>()
            };
        }

        return selected.Where(a => ReadableFc.Contains(a.FunctionalConstraint))
            .Where(a => !string.IsNullOrWhiteSpace(a.MmsReference))
            .Select(a => new IedBrowserReadTarget(
                a.MmsReference, a.FunctionalConstraint,
                string.IsNullOrWhiteSpace(a.SclBType) ? a.MmsType : a.SclBType))
            .DistinctBy(x => IedBrowserLiveValues.CanonicalReference(x.Reference) + "|" +
                x.FunctionalConstraint.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .ToArray();
    }

    private static LiveIedDataSetModel? FindSet(LiveIedModelDiscoveryDocument model, string reference)
        => model.DataSets.FirstOrDefault(ds =>
            string.Equals(ds.Reference, reference, StringComparison.Ordinal));

    private static IEnumerable<LiveIedDataAttributeModel> ForDataSet(
        IEnumerable<LiveIedDataAttributeModel> attributes,
        IedBrowserSchemaIndex schema, LiveIedDataSetModel? set)
        => set is null ? [] : attributes.Where(attr =>
            schema.ContainsDataSetMember(set.Reference, attr.MmsReference,
                attr.FunctionalConstraint));
}
