using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Presentation-only RCB enablement proof. Occupancy, activation and received
/// reports are deliberately not evidence that the remote RptEna Boolean is true.
/// Read values remain owned exclusively by the per-association LiveValues ledger.
/// </summary>
public static class IedBrowserRcbEnabledPresentation
{
    // Build once per coalesced UI projection, O(number of model RCBs).
    // Ambiguous duplicate identities are never accepted as Boolean authority.
    public static IReadOnlyDictionary<string, IedBrowserReadTarget> VerifiedTargets(
        LiveIedModelDiscoveryDocument? verified)
    {
        var targets = new Dictionary<string, IedBrowserReadTarget>(StringComparer.Ordinal);
        if (verified is null) return targets;

        foreach (var group in verified.ReportControls.GroupBy(
                     item => IedBrowserLiveValues.CanonicalReference(item.Reference),
                     StringComparer.Ordinal))
        {
            if (group.Count() != 1) continue;
            var reference = group.Key;
            var fc = reference.Contains(".BR.", StringComparison.Ordinal) ? "BR"
                : reference.Contains(".RP.", StringComparison.Ordinal) ? "RP"
                : "";
            if (fc.Length == 0) continue;
            targets.Add(reference, new IedBrowserReadTarget(
                reference + ".RptEna", fc, "Boolean"));
        }
        return targets;
    }

    public static bool TryVerifiedTrue(IedBrowserLiveValues ledger,
        RcbExportRow? row, IReadOnlyDictionary<string, IedBrowserReadTarget> targets,
        bool connected, out string tooltip)
    {
        tooltip = "";
        if (!connected || ledger.IsOffline || row is null) return false;
        var reference = IedBrowserLiveValues.CanonicalReference(row.Reference);
        if (!targets.TryGetValue(reference, out var target) ||
            target.FunctionalConstraint != (row.Buffered ? "BR" : "RP") ||
            !string.Equals(target.DataType, "Boolean", StringComparison.Ordinal) ||
            !ledger.TryGet(target.Reference, target.FunctionalConstraint,
                out var observed, preferReport: false) ||
            observed.IsStale ||
            observed.Association != ledger.Association ||
            !string.Equals(observed.Source, "MMS Read", StringComparison.Ordinal) ||
            !bool.TryParse(observed.Value, out var enabled) || !enabled)
            return false;

        tooltip = "RptEna: true · verified MMS Read · " +
            observed.ObservedUtc.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'",
                System.Globalization.CultureInfo.InvariantCulture) +
            " · association " + observed.Association.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }
}
