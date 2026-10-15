using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// The only legal MMS control-state reads in the Browser are exact leaf names
/// observed in the pinned engine's GetNameList-derived FC inventory. SCL-only
/// control declarations never authorize a speculative MMS read.
/// </summary>
public static class IedBrowserControlStateEvidence
{
    private static readonly string[] SettingFields = ["NumOfSG", "ActSG", "LActTm"];
    private static readonly string[] GooseFields = ["GoEna", "NdsCom"];

    public static IReadOnlyList<IedBrowserReadTarget> ReadTargets(
        LiveIedControlBlockModel? block, string pageKind)
    {
        if (block is null || !IsVerifiedInventory(block, pageKind))
            return Array.Empty<IedBrowserReadTarget>();
        var allow = pageKind == "SGCB" ? SettingFields : GooseFields;
        return allow.Where(field => block.Attributes.Contains(field, StringComparer.Ordinal))
            .Select(field => new IedBrowserReadTarget(
                // The functional constraint is passed separately. The pinned
                // engine parses "domain/LN.DO.field" to LN$FC$DO$field.
                $"{block.Domain}/{block.LogicalNode}.{block.Name}.{field}",
                block.FunctionalConstraint,
                field switch
                {
                    "NumOfSG" or "ActSG" => "UInt32",
                    "LActTm" => "UtcTime",
                    _ => "Boolean"
                }))
            .ToArray();
    }

    public static bool IsVerifiedInventory(LiveIedControlBlockModel block, string pageKind)
    {
        if (block.DiscoveryStatus != "AttributeInventoryOnly" ||
            block.Attributes.Count == 0 ||
            string.IsNullOrWhiteSpace(block.Domain) ||
            string.IsNullOrWhiteSpace(block.LogicalNode) ||
            string.IsNullOrWhiteSpace(block.Name))
            return false;
        if (pageKind == "SGCB")
        {
            if (block.Kind is not ("SettingGroupControl" or "SGCB") ||
                block.Name != "SGCB" || block.LogicalNode != "LLN0" ||
                block.FunctionalConstraint is not ("SP" or "SG" or "SE"))
                return false;
        }
        else if (pageKind == "GoCB")
        {
            if (block.Kind is not ("GSEControl" or "GOOSE") ||
                block.FunctionalConstraint != "GO")
                return false;
        }
        else return false;

        var exactInventoryReference =
            $"{block.Domain}/{block.LogicalNode}.{block.FunctionalConstraint}.{block.Name}";
        return string.Equals(Canonical(block.Reference),
            Canonical(exactInventoryReference), StringComparison.Ordinal);
    }

    public static bool TryRead(
        IedBrowserLiveValues ledger, LiveIedControlBlockModel block,
        string pageKind, string field, out IedBrowserObservedValue observed,
        out string exactReference)
    {
        observed = null!;
        exactReference = "";
        var target = ReadTargets(block, pageKind).FirstOrDefault(item =>
            item.Reference.EndsWith("." + field, StringComparison.Ordinal));
        if (target is null) return false;
        exactReference = target.Reference;
        // The canonical ledger, with its association fence and stale state,
        // is the only observation authority. No shadow cache or snapshot merge.
        if (!ledger.TryGet(target.Reference, target.FunctionalConstraint,
                out observed, preferReport: false) ||
            !string.Equals(observed.Source, "MMS Read", StringComparison.Ordinal))
        {
            observed = null!;
            return false;
        }
        return true;
    }
    /// <summary>
    /// SGCB LActTm is a real relay value, but an all-zero wire UTC-Time
    /// denotes an unset time rather than an actual group-change in 1970.
    /// A formatted date alone is never proof: require exact raw MMS bytes.
    /// </summary>
    public static string FormatObservedField(
        string pageKind, string field, IedBrowserObservedValue observed)
        => pageKind == "SGCB" && field == "LActTm" &&
           observed.TimeBits is
           {
               IsDecoded: true,
               HasWireProvenance: true,
               SecondsSinceEpoch: 0,
               FractionOfSecond24: 0
           }
           ? "Not initialized"
           : observed.Value;

    private static string Canonical(string reference)
        => (reference ?? "").Trim().Replace('$', '.');
}
