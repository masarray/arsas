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
        if (liveModel is null || !TryExactWireIdentity(requestedRcb, out var requested))
            return false;

        var candidates = liveModel.ReportControls
            .Where(rcb => rcb.Buffered &&
                TryExactWireIdentity(rcb.Reference, out var candidate) &&
                candidate == requested)
            .Take(2).ToArray();
        if (candidates.Length != 1)
            return false;

        exactRcbReference = candidates[0].Reference;
        return true;
    }

    /// <summary>
    /// Compare exact MMS identities rather than a display-canonical string.
    /// Browser inventory can represent the SAME BRCB as LN.BR.name or
    /// LN$BR$name; the generic process-value canonicalizer strips FC and
    /// therefore must not be used for RCB permission checks.
    /// </summary>
    public static bool SameExactBrcbReference(string? left, string? right)
        => TryExactWireIdentity(left, out var a) &&
           TryExactWireIdentity(right, out var b) && a == b;

    private static bool TryExactWireIdentity(
        string? reference, out MmsObjectReference wire)
    {
        wire = default;
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 256)
            return false;

        var candidate = MmsObjectReference.FromIec61850Reference(
            reference + ".PurgeBuf", "BR");
        var members = candidate.Item.Split('(bool isBuffered, bool isDynamicPlan)
        => isBuffered && isDynamicPlan;
}
);
        // Exact LD / LN $ BR $ BRCB $ PurgeBuf, no URCB,
        // guessed suffix, wildcard or nested data attribute.
        if (string.IsNullOrWhiteSpace(candidate.Domain) ||
            members.Length != 4 ||
            string.IsNullOrWhiteSpace(members[0]) ||
            members[1] != "BR" ||
            string.IsNullOrWhiteSpace(members[2]) ||
            members[3] != "PurgeBuf")
            return false;
        wire = candidate;
        return true;
    }

    public static bool RejectAutomaticBufferedMutation(bool isBuffered, bool isDynamicPlan)
        => isBuffered && isDynamicPlan;
}
