using AR.Iec61850.Discovery;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace ArIED61850Tester.Models;

/// <summary>
/// Presentation-only RCB enablement proof. Occupancy, activation and received
/// reports are deliberately not evidence that the remote RptEna Boolean is true.
/// Read values remain owned exclusively by the per-association LiveValues ledger.
/// </summary>
public static class IedBrowserRcbEnabledPresentation
{
    // Declarations never change within one live model identity. Build once,
    // not on every 200 ms value flush. Weak keys release obsolete IED models.
    private static readonly ConditionalWeakTable<LiveIedModelDiscoveryDocument,
        IReadOnlyDictionary<string, IedBrowserReadTarget>> VerifiedIndexCache = new();
    private static readonly IReadOnlyDictionary<string, IedBrowserReadTarget> EmptyIndex =
        new ReadOnlyDictionary<string, IedBrowserReadTarget>(
            new Dictionary<string, IedBrowserReadTarget>(StringComparer.Ordinal));

    public static IReadOnlyDictionary<string, IedBrowserReadTarget> VerifiedTargets(
        LiveIedModelDiscoveryDocument? verified)
        => verified is null ? EmptyIndex : VerifiedIndexCache.GetValue(verified, BuildTargets);

    // Ambiguous duplicate RCB identities still fail closed.
    private static IReadOnlyDictionary<string, IedBrowserReadTarget> BuildTargets(
        LiveIedModelDiscoveryDocument verified)
    {
        var targets = new Dictionary<string, IedBrowserReadTarget>(StringComparer.Ordinal);
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
        return new ReadOnlyDictionary<string, IedBrowserReadTarget>(targets);
    }

    public static bool TryVerifiedTrue(IedBrowserLiveValues ledger,
        RcbExportRow? row, IReadOnlyDictionary<string, IedBrowserReadTarget> targets,
        bool connected, out string tooltip, DateTime minObservedUtc = default)
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
            observed.ObservedUtc < minObservedUtc ||
            !string.Equals(observed.Source, "MMS Read", StringComparison.Ordinal) ||
            observed.TypedBoolean is not true ||
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
