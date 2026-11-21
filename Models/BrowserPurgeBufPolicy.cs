using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace ArIED61850Tester.Models;

/// <summary>
/// Explicit operator-only BRCB PurgeBuf identity admission. This is NOT a
/// generic BR/RP write permission and never participates in report startup.
/// A cached SCL-only RCB or a synthetic display name is not a wire authority.
/// </summary>
public static class BrowserPurgeBufPolicy
{
    public static bool TryResolve(
        LiveIedModelDiscoveryDocument? liveModel, string? requestedRcb,
        out string exactRcbReference)
    {
        exactRcbReference = string.Empty;
        if (liveModel is null || string.IsNullOrWhiteSpace(requestedRcb) ||
            requestedRcb.Length > 256)
            return false;

        var canonical = IedBrowserLiveValues.CanonicalReference(requestedRcb);
        var slash = canonical.IndexOf('/');
        if (slash < 1 || slash >= canonical.Length - 1)
            return false;

        var wire = MmsObjectReference.FromIec61850Reference(
            canonical + ".PurgeBuf", "BR");
        var members = wire.Item.Split('$');
        // Must be the exact domain / LN $ BR $ BRCB $ PurgeBuf MMS
        // variable. Deny URCB, forged nesting, wildcard and DO/DA paths.
        if (string.IsNullOrWhiteSpace(wire.Domain) ||
            members.Length != 4 ||
            string.IsNullOrWhiteSpace(members[0]) ||
            !string.Equals(members[1], "BR", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(members[2]) ||
            !string.Equals(members[3], "PurgeBuf", StringComparison.Ordinal))
            return false;

        var candidates = liveModel.ReportControls
            .Where(rcb => rcb.Buffered &&
                string.Equals(
                    IedBrowserLiveValues.CanonicalReference(rcb.Reference),
                    canonical, StringComparison.Ordinal))
            .Take(2).ToArray();
        if (candidates.Length != 1)
            return false;

        exactRcbReference = candidates[0].Reference;
        return true;
    }

    public static bool RejectAutomaticBufferedMutation(bool isBuffered, bool isDynamicPlan)
        => isBuffered && isDynamicPlan;
}
