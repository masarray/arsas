using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Single synchronous publication point for inspection evidence. The native
/// client owns a detached signal; only an exact current association and control
/// object may update the operator's GUI model. Never infer process state from
/// inspection status, and never turn StatusOnly into command authority.
/// </summary>
public static class Iec61850ControlInspectionPublication
{
    public static bool TryApply(
        SignalDefinition guiSignal, Iec61850ControlCapabilities capabilities,
        Guid expectedAssociation, Func<Guid, bool> isCurrentAssociation)
    {
        ArgumentNullException.ThrowIfNull(guiSignal);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(isCurrentAssociation);
        if (expectedAssociation == Guid.Empty ||
            !isCurrentAssociation(expectedAssociation) ||
            guiSignal.ControlCommandBusy ||
            !string.Equals(guiSignal.ObjectReference, capabilities.ObjectReference,
                StringComparison.Ordinal))
            return false;

        // Do not preserve an old typed CDC after a failed/changed descriptor.
        // IsOperationallyReady alone is insufficient for a read-only model.
        var verified = capabilities.SupportsOperate &&
            !string.IsNullOrWhiteSpace(capabilities.ControlCdc);
        if (!verified ||
            (guiSignal.VerifiedLiveControlCdc.Length > 0 &&
             !string.Equals(guiSignal.VerifiedLiveControlCdc,
                 capabilities.ControlCdc, StringComparison.OrdinalIgnoreCase)))
            guiSignal.ClearLiveControlAuthority();

        // All mutations here are synchronous on the invoking UI context:
        // a native late callback never owns an ObservableObject reference.
        guiSignal.ClearControlConfirmation();
        if (verified)
            guiSignal.AcceptVerifiedLiveControlCdc(capabilities.ControlCdc);
        else
            guiSignal.ControlCdc = capabilities.ControlCdc;
        guiSignal.ControlValueType = capabilities.ControlValueType;
        guiSignal.ControlStatusReference = capabilities.StatusReference;
        guiSignal.ControlModelReference = capabilities.ControlModelReference;
        guiSignal.ControlModelText = capabilities.ControlModelText;
        guiSignal.ControlCurrentValue = capabilities.CurrentValue;
        guiSignal.ControlInspectionEvidence =
            $"{capabilities.ControlModelReference} | {capabilities.ControlModelText} | " +
            $"{capabilities.EngineControlServiceStatus} | {capabilities.DiscoveryEvidence}";
        // Operability is published last, never before the typed evidence.
        guiSignal.ControlSemanticVerified = verified;
        guiSignal.ControlLastResult = capabilities.EngineControlServiceStatus;
        return true;
    }
}
