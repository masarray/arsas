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
            Iec61850DesignLiveStatus.RecoveredByAlternateProbe => IoTestLiveBindingState.BoundNormalized,
            // SignalNotFound is deliberately reserved for an engine-confirmed Absent verdict.
            Iec61850DesignLiveStatus.Absent => IoTestLiveBindingState.SignalNotFound,
            _ => IoTestLiveBindingState.NotEvaluated
        };

        var statusText = point.Status switch
        {
            Iec61850DesignLiveStatus.Exact => "ARIEC status: Exact",
            Iec61850DesignLiveStatus.Compatible => "ARIEC status: Compatible",
            Iec61850DesignLiveStatus.RecoveredByProbe => "ARIEC status: RecoveredByProbe",
            Iec61850DesignLiveStatus.RecoveredByAlternateProbe => "ARIEC status: RecoveredByAlternateProbe",
            Iec61850DesignLiveStatus.DesignOnly => "ARIEC status: DesignOnly · exact verification required",
            Iec61850DesignLiveStatus.LiveOnly => "ARIEC status: LiveOnly",
            Iec61850DesignLiveStatus.FunctionalConstraintMismatch => "ARIEC status: FunctionalConstraintMismatch",
            Iec61850DesignLiveStatus.TypeMismatch => "ARIEC status: TypeMismatch",
            Iec61850DesignLiveStatus.Ambiguous => "ARIEC status: Ambiguous",
            Iec61850DesignLiveStatus.InvalidTarget => "ARIEC status: InvalidTarget",
            Iec61850DesignLiveStatus.Unreadable => "ARIEC status: Unreadable",
            Iec61850DesignLiveStatus.Absent => "ARIEC status: Absent · protocol-confirmed signal absence",
            Iec61850DesignLiveStatus.TransportFailure => "ARIEC status: TransportFailure",
            Iec61850DesignLiveStatus.UnresolvedDesign => "ARIEC status: UnresolvedDesign",
            _ => $"ARIEC status: {point.Status}"
        };

        var evidence = new List<string> { statusText };

        var canonical = FirstNonEmpty(point.CanonicalMmsReference, point.MmsReference);
        var effective = FirstNonEmpty(
            point.EffectiveMmsReference,
            point.ObservedMmsReference,
            point.ObservedReference,
            canonical);

        if (!string.IsNullOrWhiteSpace(canonical))
            evidence.Add($"Canonical: {canonical}");
        if (!string.IsNullOrWhiteSpace(effective))
            evidence.Add($"Effective: {effective}");

        foreach (var item in point.Evidence.Where(item => !string.IsNullOrWhiteSpace(item)))
            evidence.Add(item.Trim());

        if (point.Probe != null)
            AppendProbeEvidence(evidence, "Final probe", point.Probe);

        for (var i = 0; i < point.ProbeAttempts.Count; i++)
        {
            var attempt = point.ProbeAttempts[i];
            var kind = attempt.IsCanonical
                ? "canonical"
                : attempt.AlternateStrategy.HasValue
                    ? $"alternate/{attempt.AlternateStrategy.Value}"
                    : "alternate";
            evidence.Add($"Probe attempt {i + 1}: {kind}");
            if (!string.IsNullOrWhiteSpace(attempt.Explanation))
                evidence.Add(attempt.Explanation.Trim());
            AppendProbeEvidence(evidence, $"Attempt {i + 1}", attempt.Probe);
        }

        return new IoTestReconciliationPresentationResult(
            state,
            string.Join(" · ", evidence.Distinct(StringComparer.OrdinalIgnoreCase)),
            effective,
            point.Status == Iec61850DesignLiveStatus.Absent);
    }

    private static void AppendProbeEvidence(
        ICollection<string> evidence,
        string label,
        Iec61850ExactProbeEvidence probe)
    {
        evidence.Add($"{label}: {probe.Status}");
        if (!string.IsNullOrWhiteSpace(probe.MmsReference))
            evidence.Add($"{label} target: {probe.MmsReference}");
        if (!string.IsNullOrWhiteSpace(probe.Message))
            evidence.Add(probe.Message.Trim());
        if (!string.IsNullOrWhiteSpace(probe.ValueSummary))
            evidence.Add($"{label} value: {probe.ValueSummary}");
        if (probe.FailureCode.HasValue)
            evidence.Add($"{label} engine failure code: {probe.FailureCode.Value}");
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}

public sealed record IoTestReconciliationPresentationResult(
    IoTestLiveBindingState State,
    string Reason,
    string Reference,
    bool IsConfirmedAbsent);
