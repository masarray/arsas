using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Promote an uncertain discovery CDC to BSC only when the *same* DataObject
/// has an exact ST valWTr.posVal signal. This affects the presentation/declared
/// semantic candidate only; live ctlModel and typed CO verification must still
/// succeed independently before any control write.
/// </summary>
public static class Iec61850ControlStatusEvidence
{
    public static int ApplyExactStepStatus(ICollection<SignalDefinition> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        var exactStepStatuses = signals
            .Where(s => !s.IsControlSignal &&
                        s.FunctionalConstraint.Equals("ST", StringComparison.OrdinalIgnoreCase) &&
                        s.ObjectReference.EndsWith(".valWTr.posVal", StringComparison.OrdinalIgnoreCase))
            .Select(s => Iec61850ControlIdentity.Normalize(s.ObjectReference))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var corrected = 0;
        foreach (var control in signals.Where(s => s.IsValidControlObject))
        {
            if (control.ControlCdc.Equals("BSC", StringComparison.OrdinalIgnoreCase) ||
                !(string.IsNullOrWhiteSpace(control.ControlCdc) ||
                  control.ControlCdc.Equals("SPC", StringComparison.OrdinalIgnoreCase)))
                continue;

            var exactStatus = $"{Iec61850ControlIdentity.Normalize(control.ObjectReference)}.valWTr.posVal";
            if (!exactStepStatuses.Contains(exactStatus))
                continue;

            control.ControlCdc = "BSC";
            control.ControlValueType = "ValWithTrans";
            control.ControlStatusReference = exactStatus;
            // Never change ControlModelText, ControlModelResolved or ControlSemanticVerified.
            corrected++;
        }
        return corrected;
    }
}
