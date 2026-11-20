using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Read-only, on-expand q/t repair: a single exact live-model-declared IEC
/// attribute whose original typed wire bits are absent. Does not scan other
/// phases, duplicate runtime state, poll, or infer flags from report summaries.
/// </summary>
public sealed record IedBrowserExactCompanionReadRequest(string Reference, string Fc, string Kind)
{
    public static IReadOnlyList<IedBrowserReadTarget> Build(
        LiveIedModelDiscoveryDocument? model, IedBrowserTreeRow? page,
        IedBrowserLiveValues ledger, string reference, string fc, string kind)
    {
        if (model is null || ledger.IsOffline ||
            page is not { Kind: "LN", Payload: LiveIedLogicalNodeModel } ||
            kind is not ("Quality" or "Timestamp") ||
            string.IsNullOrWhiteSpace(reference) ||
            string.IsNullOrWhiteSpace(fc)) return [];

        var canonical = IedBrowserLiveValues.CanonicalReference(reference);
        var constraint = fc.Trim().ToUpperInvariant();
        if (constraint is "CO" or "SE" || !(
            kind == "Quality" && canonical.EndsWith(".q", StringComparison.Ordinal) ||
            kind == "Timestamp" && canonical.EndsWith(".t", StringComparison.Ordinal)))
            return [];

        if (ledger.TryGet(canonical, constraint, out var existing,
            requiredEvidence: kind) && !existing.IsStale &&
            existing.Association == ledger.Association &&
            (kind == "Quality" && existing.QualityBits is not null ||
             kind == "Timestamp" && existing.TimeBits is
                 { IsDecoded: true, HasWireProvenance: true }))
            return [];

        // Only a unique exact declaration in the selected live LN authorizes
        // this read. The MMS type must support the required wire evidence.
        var matches = model.LogicalDevices.SelectMany(d => d.LogicalNodes)
            .Where(ln => ReferenceEquals(ln, page.Payload))
            .SelectMany(ln => ln.DataObjects)
            .SelectMany(obj => obj.Attributes)
            .Where(a => string.Equals(
                    IedBrowserLiveValues.CanonicalReference(a.MmsReference),
                    canonical, StringComparison.Ordinal) &&
                string.Equals(a.FunctionalConstraint, constraint,
                    StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        if (matches.Length != 1) return [];
        var declared = matches[0];
        var btype = (declared.SclBType ?? "").Trim();
        var mmsType = (declared.MmsType ?? "").Trim();
        var compatible = kind == "Quality"
            ? btype.Equals("Quality", StringComparison.OrdinalIgnoreCase) ||
              mmsType.Equals("bit-string", StringComparison.OrdinalIgnoreCase)
            : btype.Equals("Timestamp", StringComparison.OrdinalIgnoreCase) ||
              btype.Equals("UtcTime", StringComparison.OrdinalIgnoreCase) ||
              mmsType.Equals("utc-time", StringComparison.OrdinalIgnoreCase);
        if (!compatible) return [];

        return [new IedBrowserReadTarget(declared.MmsReference, constraint,
            kind == "Quality" ? "Quality" : "UtcTime")];
    }
}
