namespace ArIED61850Tester.Models;

/// <summary>
/// Determines the authoritative observed value of one already model-verified
/// Browser row. The two IEC references are supplied by structured projection;
/// this selector cannot invent aliases or read from MMS. Report authority wins
/// across BOTH aliases during monitoring, not only within a single ledger key.
/// </summary>
public static class IedBrowserDetailEvidenceResolver
{
    public static bool TryResolve(
        IedBrowserLiveValues values, IedBrowserDetailRow row,
        IedBrowserSchemaIndex? schema, bool monitoring,
        out IedBrowserObservedValue evidence, out string observedReference)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(row);
        observedReference = "";
        evidence = null!;
        var primary = IedBrowserLiveValues.CanonicalReference(row.ValueReference);
        var alternate = IedBrowserLiveValues.CanonicalReference(row.FallbackValueReference);
        if (primary.Length == 0 || string.IsNullOrWhiteSpace(row.FunctionalConstraint))
            return false;

        var a = values.TryGet(primary, row.FunctionalConstraint, out var first,
            preferReport: monitoring, schema: schema, requiredEvidence: row.EvidenceKind);
        if (alternate.Length == 0 || alternate == primary ||
            row.EvidenceKind is "Quality" or "Timestamp")
        {
            if (!a) return false;
            observedReference = primary;
            evidence = first;
            return true;
        }

        var b = values.TryGet(alternate, row.FunctionalConstraint, out var second,
            preferReport: monitoring, schema: schema);
        if (!a && !b) return false;
        if (!a || b && PreferAlternate(first, second, monitoring))
        {
            evidence = second;
            observedReference = alternate;
            return true;
        }
        evidence = first;
        observedReference = primary;
        return true;
    }

    private static bool PreferAlternate(
        IedBrowserObservedValue primary, IedBrowserObservedValue alternate,
        bool monitoring)
    {
        if (monitoring)
        {
            var primaryReported = primary.Source.StartsWith("Report", StringComparison.Ordinal);
            var alternateReported = alternate.Source.StartsWith("Report", StringComparison.Ordinal);
            if (primaryReported != alternateReported)
                return alternateReported;
        }
        // Within the same acquisition authority choose the latest observed
        // evidence, not the position of a UI row or the order of aliases.
        return alternate.Revision > primary.Revision;
    }
}
