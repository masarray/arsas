using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Makes one disposable, command-only copy of the operator's selected signal.
/// ARIEC may update the request's SignalDefinition after an asynchronous MMS
/// reply; a retired association must never mutate a WPF-owned signal model.
/// The copy carries no process-value authority or extra observation cache.
/// </summary>
public static class Iec61850ControlRequestSnapshot
{
    public static Iec61850ControlCommandRequest Create(Iec61850ControlCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new Iec61850ControlCommandRequest
        {
            Signal = CreateSignal(request.Signal),
            ValueText = request.ValueText,
            InterlockCheck = request.InterlockCheck,
            SynchroCheck = request.SynchroCheck,
            TestMode = request.TestMode,
            Originator = request.Originator,
            OriginCategory = request.OriginCategory,
            FeedbackTimeoutMs = request.FeedbackTimeoutMs,
            CommandTerminationTimeoutMs = request.CommandTerminationTimeoutMs
        };
    }

    /// <summary>
    /// Detached command/control inspection model created synchronously on the
    /// caller's thread. This is not a live value cache or a new MMS authority.
    /// No background native task may retain the GUI-owned SignalDefinition.
    /// </summary>
    public static SignalDefinition CreateSignal(SignalDefinition signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var copy = new SignalDefinition
        {
            Name = signal.Name,
            ObjectReference = signal.ObjectReference,
            FunctionalConstraint = signal.FunctionalConstraint,
            DataType = signal.DataType,
            Category = signal.Category,
            IsControlSignal = signal.IsControlSignal,
            ControlCdc = signal.ControlCdc,
            ControlStatusReference = signal.ControlStatusReference,
            ControlValueType = signal.ControlValueType,
            ControlModelReference = signal.ControlModelReference,
            ControlModelText = signal.ControlModelText,
            ControlCurrentValue = signal.ControlCurrentValue,
            ControlSemanticVerified = signal.ControlSemanticVerified
        };
        // Preserve only the authority already verified by the current MMS
        // association. Never promote DataSet/SCL advisory CDC to live proof.
        if (!string.IsNullOrWhiteSpace(signal.VerifiedLiveControlCdc))
            copy.AcceptVerifiedLiveControlCdc(signal.VerifiedLiveControlCdc);
        return copy;
    }
}
