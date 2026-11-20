using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Small exact-current-MMS RCB inspect plan. Neither a Report frame nor an
/// SCL-only RCB authorizes the state of RptEna. No whole-IED RCB probe.
/// </summary>
public static class IedBrowserRcbFocusedReadPlan
{
    public static IReadOnlyList<IedBrowserReadTarget> Build(
        LiveIedModelDiscoveryDocument? verified, RcbExportRow? selected)
    {
        if (verified is null || selected is null) return [];
        var reference = IedBrowserLiveValues.CanonicalReference(selected.Reference);
        var matches = verified.ReportControls.Where(rcb =>
            string.Equals(IedBrowserLiveValues.CanonicalReference(rcb.Reference),
                reference, StringComparison.Ordinal)).Take(2).ToArray();
        if (matches.Length != 1) return [];
        var model = matches[0];
        var fc = selected.Buffered ? "BR" : "RP";
        var wire = IedBrowserLiveValues.CanonicalReference(model.Reference);
        // Explicit FC name is part of the RCB's observed object identity.
        if (!wire.Contains("." + fc + ".", StringComparison.Ordinal))
            return [];
        return
        [
            new IedBrowserReadTarget(wire + ".RptEna", fc, "Boolean"),
            new IedBrowserReadTarget(wire + ".Resv", fc, "Boolean"),
            new IedBrowserReadTarget(wire + ".ResvTms", fc, "UInt32")
        ];
    }
}
