using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Explicit, non-cyclic read plans. Only model-proven FC leaf attributes are allowed;
/// controls are never operated, created, reserved, enabled or written.
/// </summary>
/// <summary>Typed selection routes avoid treating an FC DataSet read as control-block status.</summary>
public enum IedBrowserReadSelectionKind
{
    FcAttributes,
    ReadOnlyRcb,
    SettingGroupAttributes,
    GooseInspectionRequiresSubscriber
}

public static class IedBrowserReadPlan
{
    public static IedBrowserReadSelectionKind SelectionKind(IedBrowserTreeRow? page)
        => page?.Payload switch
        {
            RcbExportRow => IedBrowserReadSelectionKind.ReadOnlyRcb,
            LiveIedControlBlockModel when page.Kind == "SGCB" =>
                IedBrowserReadSelectionKind.SettingGroupAttributes,
            LiveIedControlBlockModel when page.Kind == "GoCB" =>
                IedBrowserReadSelectionKind.GooseInspectionRequiresSubscriber,
            _ => IedBrowserReadSelectionKind.FcAttributes
        };

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
                // SGCB page: model-proven SG/SE parameters. The control
                // state's exact safe fields are appended separately below.
                LiveIedControlBlockModel block when page.Kind == "SGCB" =>
                    model.LogicalDevices
                        .Where(ld => string.Equals(ld.MmsDomain, block.Domain, StringComparison.Ordinal))
                        .SelectMany(ld => ld.LogicalNodes)
                        .SelectMany(ln => ln.DataObjects)
                        .SelectMany(obj => obj.Attributes)
                        .Where(attr => attr.FunctionalConstraint is "SG" or "SE"),
                // GoCB DataSet member reads do not prove GoEna. Only exact
                // safe control fields from discovery's FC inventory are legal.
                LiveIedControlBlockModel when page.Kind == "GoCB" =>
                    Array.Empty<LiveIedDataAttributeModel>(),
                LiveIedControlBlockModel block when !string.IsNullOrWhiteSpace(block.DataSetReference) =>
                    ForDataSet(all, schema, FindSet(model, block.DataSetReference)),
                RcbExportRow => Array.Empty<LiveIedDataAttributeModel>(),
                _ => Array.Empty<LiveIedDataAttributeModel>()
            };
        }

        var attributes = selected.Where(a => ReadableFc.Contains(a.FunctionalConstraint))
            .Where(a => !string.IsNullOrWhiteSpace(a.MmsReference))
            .Select(a => new IedBrowserReadTarget(
                a.MmsReference, a.FunctionalConstraint,
                string.IsNullOrWhiteSpace(a.SclBType) ? a.MmsType : a.SclBType));
        // Exact, model-inventory-proven control state is an additional
        // read-only projection. It is never added to cyclic or Read All scans.
        var control = !readAll && page?.Payload is LiveIedControlBlockModel inspectionBlock &&
            page.Kind is "SGCB" or "GoCB"
            ? IedBrowserControlStateEvidence.ReadTargets(inspectionBlock, page.Kind)
            : Array.Empty<IedBrowserReadTarget>();
        return attributes.Concat(control)
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
