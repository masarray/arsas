using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Pure Browser toolbar decision. It never discovers, reads, selects or sends
/// an IEC command. Caller must establish the live DO and descriptor belong
/// to the SAME current IED association before evaluating this decision.
/// </summary>
public sealed record IedBrowserControlEligibility(bool CanControl, string Reason)
{
    public static IedBrowserControlEligibility Check(
        bool focusIsExactDo,
        string? focusedReference,
        LiveIedDataObjectModel? onlineDo,
        Iec61850ControlCapabilities? liveControl,
        bool currentAssociationVerified,
        bool operatorAuthorized)
    {
        static IedBrowserControlEligibility Off(string reason) => new(false, reason);

        if (!focusIsExactDo || string.IsNullOrWhiteSpace(focusedReference))
            return Off("Focus a verified controllable Data Object, not a status DA or folder.");
        if (!currentAssociationVerified || onlineDo is null)
            return Off("Current online IED association and model are not verified.");
        if (!string.Equals(onlineDo.Reference, focusedReference, StringComparison.Ordinal))
            return Off("Focused DO does not match the current online model.");
        if (!operatorAuthorized)
            return Off("Operator control authorization is required.");
        if (liveControl is null ||
            !string.Equals(liveControl.ObjectReference, focusedReference, StringComparison.Ordinal))
            return Off("No exact live control descriptor for this Data Object.");
        if (liveControl.ControlModel is Iec61850ControlModelKind.Unknown or
            Iec61850ControlModelKind.StatusOnly)
            return Off("Control model is unknown or StatusOnly (0).");

        if (!liveControl.SupportsOperate ||
            !liveControl.EngineControlServiceAvailable ||
            !liveControl.IsOperationallyReady)
            return Off("Native IEC Control did not verify an operable CO service.");

        if (!string.Equals(liveControl.ControlModelReference,
                focusedReference + ".ctlModel", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(liveControl.StatusReference) ||
            string.IsNullOrWhiteSpace(liveControl.CtlValSignature))
            return Off("Live ctlModel, typed ctlVal or process feedback descriptor is incomplete.");

        // A label such as Pos, CSWI or XCBR is never independent authority.
        // A missing CDC on model discovery can be supplemented by the native
        // live control descriptor; an explicit contradictory CDC must fail.
        var cdc = liveControl.ControlCdc.ToUpperInvariant();
        if (cdc is not ("SPC" or "DPC" or "INC" or "ENC" or "BSC" or "APC"))
            return Off("Control CDC is unsupported or not established from live typing.");
        var declaredCdc = (onlineDo.InferredCdc ?? "").ToUpperInvariant();
        if (declaredCdc.Length > 0 && declaredCdc != "UNKNOWN" &&
            declaredCdc != cdc)
            return Off("Declared DO CDC contradicts the live typed control descriptor.");

        return new(true, $"Verified {cdc} · {liveControl.ControlModel} · explicit operator command only.");
    }
}
