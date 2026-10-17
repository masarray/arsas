using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Renders an IEC LN NamPlt Data Object from the SAME association-scoped
/// FC=DC observations as its expanded children. The parent is not an MMS
/// scalar: only exact model-declared, unique child leaf references are used.
/// No SCL declared text is promoted to a live value.
/// </summary>
public static class IedBrowserNameplatePresentation
{
    public sealed record Projection(string Value, string Source, string Provenance);

    private static readonly string[] PreferredLeaves =
        ["vendor", "d", "swRev", "configRev"];

    public static IReadOnlyList<string> BuildReferences(LiveIedDataObjectModel obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (!string.Equals(obj.Name, "NamPlt", StringComparison.Ordinal))
            return [];
        var owner = IedBrowserLiveValues.CanonicalReference(obj.Reference);
        // Never turn a prefix or different FC into a Nameplate association.
        if (owner.Length == 0) return [];
        var result = new List<string>(4);
        foreach (var leaf in PreferredLeaves)
        {
            var reference = owner + "." + leaf;
            var exact = obj.Attributes.Where(attr =>
                string.Equals(attr.FunctionalConstraint, "DC", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(IedBrowserLiveValues.CanonicalReference(attr.MmsReference),
                    reference, StringComparison.Ordinal)).Take(2).ToArray();
            if (exact.Length == 1)
                result.Add(reference);
        }
        return result;
    }

    public static bool TryProject(IedBrowserDetailRow row,
        IedBrowserLiveValues ledger, IedBrowserCfEvidencePresentation engineering,
        out Projection projection)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(engineering);
        projection = null!;
        if (!row.IsDataObject || row.NameplateDcReferences.Count == 0)
            return false;

        foreach (var reference in row.NameplateDcReferences)
        {
            // Same exact FC and association; never read sibling or guess vendor.
            if (ledger.TryGet(reference, "DC", out var observed))
            {
                // MarkOffline retires the association but preserves the
                // last observation; render it visibly stale. A new connected
                // association always starts with a cleared canonical ledger.
                if (!observed.IsStale && observed.Association != ledger.Association ||
                    string.IsNullOrWhiteSpace(observed.Value))
                    continue;
                projection = new Projection(
                    observed.IsStale ? "Offline · " + observed.Value : observed.Value,
                    observed.IsStale ? "Stale " + observed.Source : observed.Source,
                    $"{reference} [DC] · {observed.Source} · observed " +
                    $"{observed.ObservedUtc:yyyy-MM-dd HH:mm:ss} UTC · association {observed.Association}");
                return true;
            }
        }
        // A verified canonical discovery-instance value is an explicitly
        // HISTORICAL read, not a fresh MMS observation or an SCL declaration.
        foreach (var reference in row.NameplateDcReferences)
        {
            if (!engineering.TryGet(reference, "DC", out var value,
                    out var evidence, out var isLive) ||
                string.IsNullOrWhiteSpace(value))
                continue;
            projection = new Projection(value,
                isLive ? "MMS Read" : "Discovery snapshot",
                reference + " [DC] · " + evidence);
            return true;
        }
        return false;
    }
}
