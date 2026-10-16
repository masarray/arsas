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

    /// <summary>
    /// FC CO denotes command parameters, not scalar MMS Read targets.
    /// Keeping that separate from a genuinely missing read is essential
    /// during FAT; no control operation is implied by Browser navigation.
    /// </summary>
    public static string UnobservedValue(string? functionalConstraint)
        => string.Equals(functionalConstraint, "CO", StringComparison.OrdinalIgnoreCase)
            ? "Control only"
            : "Not read";

    public static IReadOnlyList<IedBrowserReadTarget> Build(
        LiveIedModelDiscoveryDocument? model, IedBrowserTreeRow? page,
        bool readAll, string? selectedDoReference = null)
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
                // An explicit DO focus in an LN page scopes Read to every
                // model-verified safe FC leaf of that exact DO. No first-child
                // selection, prefix matching, or matching names in other LNs.
                LiveIedLogicalNodeModel ln => ln.DataObjects
                    .Where(o => selectedDoReference is null ||
                        string.Equals(o.Reference, selectedDoReference,
                            StringComparison.Ordinal))
                    .SelectMany(o => o.Attributes),
                LiveIedLogicalDeviceModel ld => ld.LogicalNodes.SelectMany(ln => ln.DataObjects)
                    .SelectMany(o => o.Attributes),
                LiveIedDataSetModel set => ForDataSet(all, schema, set),
                // The visible SGCB page is active SG, never an implicit
                // selection/confirmation of edit-group [SE] attributes.
                LiveIedControlBlockModel block when page.Kind == "SGCB" =>
                    model.LogicalDevices
                        .Where(ld => string.Equals(ld.MmsDomain, block.Domain,
                            StringComparison.Ordinal))
                        .SelectMany(ld => ld.LogicalNodes)
                        .SelectMany(ln => ln.DataObjects)
                        .SelectMany(obj => obj.Attributes)
                        .Where(attr => string.Equals(attr.FunctionalConstraint, "SG",
                            StringComparison.OrdinalIgnoreCase)),
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
        // ReadTargets already enforces the status and FC of the verified
        // attribute inventory, including refusal of SCL-only declarations.
        // A separate global-GoCB-membership check wrongly rejects legitimate
        // independently verified MMS control inventories.
        var control = !readAll && page?.Payload is LiveIedControlBlockModel inspectionBlock &&
            page.Kind is "SGCB" or "GoCB"
            ? IedBrowserControlStateEvidence.ReadTargets(inspectionBlock, page.Kind)
            : Array.Empty<IedBrowserReadTarget>();
        return attributes.Concat(control)
            .DistinctBy(x => IedBrowserLiveValues.CanonicalReference(x.Reference) + "|" +
                x.FunctionalConstraint.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Finite, evidence-first initial acquisition for a freshly discovered
    /// LIVE MMS schema. This is not Browser Read All or a new polling plan:
    /// only critical scalar status/nameplate/switch leaves are eligible.
    /// Discovery/SCL declarations alone never supply a value.
    /// </summary>
    public static IReadOnlyList<IedBrowserReadTarget> BuildInitialPriority(
        LiveIedModelDiscoveryDocument? verifiedLiveModel,
        IedBrowserLiveValues ledger, int maxTargets)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (verifiedLiveModel is null || maxTargets <= 0 || ledger.IsOffline)
            return Array.Empty<IedBrowserReadTarget>();
        var schema = new IedBrowserSchemaIndex(verifiedLiveModel);
        var association = ledger.Association;
        var candidates = verifiedLiveModel.LogicalDevices
            .SelectMany(ld => ld.LogicalNodes)
            .SelectMany(ln => ln.DataObjects)
            .SelectMany(obj => obj.Attributes.Select(attr => (obj, attr)))
            .Select(entry =>
            {
                var reference = IedBrowserLiveValues.CanonicalReference(
                    entry.attr.MmsReference);
                var fc = (entry.attr.FunctionalConstraint ?? "").Trim().ToUpperInvariant();
                return new
                {
                    entry.attr, Reference = reference, Fc = fc,
                    Priority = InitialPriority(entry.obj, reference, fc, schema),
                    Key = reference + "|" + fc
                };
            })
            .Where(x => x.Priority >= 0 && x.Reference.Length > 0)
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            // Conflicting / duplicate declarations are not authority for MMS.
            .Where(g => g.Count() == 1)
            .Select(g => g.First())
            .Where(x => !ledger.TryGet(x.Reference, x.Fc, out var observed) ||
                        observed.IsStale || observed.Association != association)
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.Reference, StringComparer.Ordinal)
            .Take(maxTargets)
            .Select(x => new IedBrowserReadTarget(
                x.attr.MmsReference, x.Fc,
                string.IsNullOrWhiteSpace(x.attr.SclBType)
                    ? x.attr.MmsType : x.attr.SclBType))
            .ToArray();
        // SGCB is not an LN DataObject. Its exact GetNameList FC inventory
        // must be admitted through the same bounded read plan, not skipped
        // because the generic attribute walker cannot see the control block.
        // Only read-only NumOfSG/ActSG/LActTm; never EditSG/CnfEdit/SE.
        var settings = verifiedLiveModel.SettingGroupControls
            .SelectMany(block => IedBrowserControlStateEvidence.ReadTargets(block, "SGCB"))
            .GroupBy(target => IedBrowserLiveValues.CanonicalReference(target.Reference) +
                               "|" + target.FunctionalConstraint,
                StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group => group.First())
            .Where(target => !ledger.TryGet(target.Reference,
                target.FunctionalConstraint, out var observed) ||
                observed.IsStale || observed.Association != association)
            .OrderBy(target => target.Reference.EndsWith(".LActTm",
                StringComparison.Ordinal) ? 1 : 0)
            .ThenBy(target => target.Reference, StringComparer.Ordinal)
            .ToArray();
        // Likewise, GoEna/NdsCom belong to the exact live GO control
        // inventory, not ordinary data-object attributes or received GOOSE
        // frames. A passive subscriber cannot prove either MMS status.
        // Limit background status reads for dense GoCB catalogs; unvisited
        // objects retain honest Not read evidence.
        var goose = verifiedLiveModel.GooseControlBlocks
            .SelectMany(block => IedBrowserControlStateEvidence.ReadTargets(block, "GoCB"))
            .GroupBy(target => IedBrowserLiveValues.CanonicalReference(target.Reference) +
                               "|" + target.FunctionalConstraint,
                StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group => group.First())
            .Where(target => !ledger.TryGet(target.Reference,
                target.FunctionalConstraint, out var observed) ||
                observed.IsStale || observed.Association != association)
            .OrderBy(target => target.Reference.EndsWith(".NdsCom",
                StringComparison.Ordinal) ? 1 : 0)
            .ThenBy(target => target.Reference, StringComparer.Ordinal)
            .Take(8).ToArray();
        // Prioritize verified control-block state and then finite LN image,
        // without an extra background service or second observation cache.
        return settings.Concat(goose).Concat(candidates)
            .DistinctBy(target => IedBrowserLiveValues.CanonicalReference(target.Reference) +
                "|" + target.FunctionalConstraint.Trim().ToUpperInvariant(),
                StringComparer.Ordinal)
            .Take(maxTargets).ToArray();
    }

    private static int InitialPriority(
        LiveIedDataObjectModel obj, string reference, string fc,
        IedBrowserSchemaIndex schema)
    {
        var owner = IedBrowserLiveValues.CanonicalReference(obj.Reference);
        if (owner.Length == 0 ||
            !reference.StartsWith(owner + ".", StringComparison.Ordinal))
            return -1;
        var leaf = reference[(owner.Length + 1)..];
        var slash = owner.LastIndexOf('/');
        var dot = owner.LastIndexOf('.');
        if (dot <= slash) return -1;
        var ln = owner[(slash + 1)..dot];
        var station = ln == "LLN0" || IsLnClass(ln, "LPHD");
        var isStatus = fc == "ST" && leaf == "stVal" &&
                       obj.Name is "Mod" or "Beh" or "Health";
        if (station && isStatus) return 0;
        var switching = IsLnClass(ln, "XCBR") ||
                        IsLnClass(ln, "XSWI") || IsLnClass(ln, "CSWI");
        var position = switching && obj.Name == "Pos" &&
                       schema.IsDeclaredDoublePointValue(owner + ".stVal", "ST");
        if (position && fc == "ST" && leaf == "stVal") return 1;
        if (station && obj.Name == "NamPlt" && fc == "DC" &&
            leaf.Length > 0 && !leaf.Contains('.')) return 2;
        if (isStatus) return 3;
        if (position && fc == "ST" && leaf is ("q" or "t")) return 4;
        if (obj.Name == "NamPlt" && fc == "DC" &&
            leaf.Length > 0 && !leaf.Contains('.')) return 5;
        return -1;
    }

    private static bool IsLnClass(string ln, string standard)
        => ln.StartsWith(standard, StringComparison.Ordinal) &&
           (ln.Length == standard.Length ||
            char.IsAsciiDigit(ln[standard.Length]));

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
