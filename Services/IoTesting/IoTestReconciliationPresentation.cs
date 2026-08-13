using AR.Iec61850.Discovery;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Presentation-only mapping from ARIEC61850 reconciliation verdicts to the existing
/// FAT live-binding state. Protocol semantics, reference canonicalization and probe
/// failure interpretation remain entirely in ARIEC61850.
/// </summary>
public static class IoTestReconciliationPresentation
{
    public static IoTestReconciliationPresentationResult FromEnginePoint(
        Iec61850DesignLivePointReconciliation point)
    {
        ArgumentNullException.ThrowIfNull(point);

        var state = point.Status switch
        {
            Iec61850DesignLiveStatus.Exact => IoTestLiveBindingState.BoundExact,
            Iec61850DesignLiveStatus.Compatible => IoTestLiveBindingState.BoundNormalized,
            Iec61850DesignLiveStatus.RecoveredByProbe => IoTestLiveBindingState.BoundExact,
            // SignalNotFound is deliberately reserved for an engine-confirmed Absent verdict.
            Iec61850DesignLiveStatus.Absent => IoTestLiveBindingState.SignalNotFound,
            _ => IoTestLiveBindingState.NotEvaluated
        };

        var statusText = point.Status switch
        {
            Iec61850DesignLiveStatus.Exact => "ARIEC verified exact design/live binding",
            Iec61850DesignLiveStatus.Compatible => "ARIEC verified compatible design/live binding",
            Iec61850DesignLiveStatus.RecoveredByProbe => "ARIEC verified by exact MMS probe",
            Iec61850DesignLiveStatus.DesignOnly => "ARIEC design-only · exact verification required",
            Iec61850DesignLiveStatus.LiveOnly => "ARIEC live-only point",
            Iec61850DesignLiveStatus.FunctionalConstraintMismatch => "ARIEC functional-constraint mismatch",
            Iec61850DesignLiveStatus.TypeMismatch => "ARIEC type mismatch",
            Iec61850DesignLiveStatus.Ambiguous => "ARIEC reconciliation ambiguous",
            Iec61850DesignLiveStatus.InvalidTarget => "ARIEC exact target invalid",
            Iec61850DesignLiveStatus.Unreadable => "ARIEC exact target unreadable",
            Iec61850DesignLiveStatus.Absent => "ARIEC confirmed signal absent",
            Iec61850DesignLiveStatus.TransportFailure => "ARIEC probe transport failure",
            Iec61850DesignLiveStatus.UnresolvedDesign => "ARIEC design target unresolved",
            _ => $"ARIEC {point.Status}"
        };

        var evidence = new List<string> { statusText };
        foreach (var item in point.Evidence.Where(item => !string.IsNullOrWhiteSpace(item)))
            evidence.Add(item.Trim());

        if (point.Probe != null)
        {
            var probe = point.Probe;
            evidence.Add($"Exact probe: {probe.Status}");
            if (!string.IsNullOrWhiteSpace(probe.MmsReference))
                evidence.Add($"Target: {probe.MmsReference}");
            if (!string.IsNullOrWhiteSpace(probe.Message))
                evidence.Add(probe.Message.Trim());
            if (!string.IsNullOrWhiteSpace(probe.ValueSummary))
                evidence.Add($"Value: {probe.ValueSummary}");
            if (probe.FailureCode.HasValue)
                evidence.Add($"Engine failure code: {probe.FailureCode.Value}");
        }

        return new IoTestReconciliationPresentationResult(
            state,
            string.Join(" · ", evidence.Distinct(StringComparer.OrdinalIgnoreCase)),
            FirstNonEmpty(point.ObservedReference, point.Reference),
            point.Status == Iec61850DesignLiveStatus.Absent);
    }

    private static string FirstNonEmpty(string first, string second)
        => string.IsNullOrWhiteSpace(first) ? second : first;
}

public sealed record IoTestReconciliationPresentationResult(
    IoTestLiveBindingState State,
    string Reason,
    string Reference,
    bool IsConfirmedAbsent);
