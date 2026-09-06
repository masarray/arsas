using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Assesses the exact Value 1 / Value 2 pair currently presented by FAT v2.
/// A complete coherent pair proves the transition it contains even when the operator later
/// resumes/reconnects the FAT workspace. Connection generation is a pair-coherency fence,
/// not a requirement that historic completed evidence must equal the newest runtime generation.
/// </summary>
public static class FatCurrentEvidenceAssessmentService
{
    public static FatCurrentEvidenceAssessment Evaluate(IoTestPointPlan point)
    {
        ArgumentNullException.ThrowIfNull(point);

        if (point.CaptureMode != FatCaptureMode.AutomaticTransition)
        {
            return new FatCurrentEvidenceAssessment(
                IoTestPointState.NotStarted,
                "Operator-snapshot rows are complete when both current value slots are captured; no digital PASS assessment is applied.");
        }

        if (!point.IsFatEvidenceComplete)
        {
            return new FatCurrentEvidenceAssessment(
                point.Runtime.State,
                "Current Value 1 / Value 2 evidence is incomplete.");
        }

        var hasGenericOverride = point.Runtime.Value1Evidence is not null || point.Runtime.Value2Evidence is not null;
        if (!hasGenericOverride)
        {
            return point.Runtime.State switch
            {
                IoTestPointState.Passed => new FatCurrentEvidenceAssessment(
                    IoTestPointState.Passed,
                    "PASS: current evidence is the accepted legacy OFF -> ON -> OFF transition pair."),
                IoTestPointState.Failed => new FatCurrentEvidenceAssessment(
                    IoTestPointState.Failed,
                    "FAIL: the current legacy transition evidence failed assessment."),
                IoTestPointState.Review => new FatCurrentEvidenceAssessment(
                    IoTestPointState.Review,
                    "REVIEW: the current legacy transition evidence requires operator review."),
                _ => new FatCurrentEvidenceAssessment(
                    IoTestPointState.Review,
                    "REVIEW: current transition evidence is complete but no terminal legacy assessment is available.")
            };
        }

        var value1 = EffectiveValue1(point);
        var value2 = EffectiveValue2(point);
        if (value1 is null || value2 is null)
        {
            return new FatCurrentEvidenceAssessment(
                IoTestPointState.Review,
                "REVIEW: both displayed FAT value slots are required for a current-pair assessment.");
        }

        // Resume/reconnect increments the live connection generation, but that must not turn
        // an already-complete True/False or Open/Closed FAT pair into REVIEW. What matters is
        // that V1 and V2 themselves belong to one coherent acquisition generation. A pair
        // straddling two generations can have missed an edge and therefore still fails closed.
        if (value1.ConnectionGeneration != value2.ConnectionGeneration)
        {
            return new FatCurrentEvidenceAssessment(
                IoTestPointState.Review,
                "REVIEW: current Value 1 and Value 2 belong to different IED connection generations; capture one coherent transition pair.");
        }

        var value1Quality = IoTestTransitionEvaluator.EvaluateQuality(value1.Quality);
        var value2Quality = IoTestTransitionEvaluator.EvaluateQuality(value2.Quality);
        if (value1Quality.Verdict != IoEvidenceVerdict.Accepted ||
            value2Quality.Verdict != IoEvidenceVerdict.Accepted)
        {
            return new FatCurrentEvidenceAssessment(
                IoTestPointState.Review,
                $"REVIEW: current evidence quality is not fully accepted (V1: {value1Quality.Reason}; V2: {value2Quality.Reason}).");
        }

        if (value2.Sequence <= value1.Sequence)
        {
            return new FatCurrentEvidenceAssessment(
                IoTestPointState.Review,
                "REVIEW: current Value 2 does not follow current Value 1 in the live evidence sequence; capture Value 2 after the intended condition change.");
        }

        var state1 = IoTestValueNormalizer.Normalize(point, value1.RawValue);
        var state2 = IoTestValueNormalizer.Normalize(point, value2.RawValue);
        if (state1 is null || state2 is null)
        {
            return new FatCurrentEvidenceAssessment(
                IoTestPointState.Review,
                "REVIEW: one or both current values cannot be normalized to an authoritative discrete state.");
        }

        if (state1 == state2)
        {
            return new FatCurrentEvidenceAssessment(
                IoTestPointState.Review,
                $"REVIEW: Value 1 and Value 2 both resolve to {StateLabel(state1.Value)}; the current pair does not prove a state change.");
        }

        return new FatCurrentEvidenceAssessment(
            IoTestPointState.Passed,
            $"PASS: current Value 1 -> Value 2 evidence proves a good-quality {StateLabel(state1.Value)} -> {StateLabel(state2.Value)} transition in one coherent connection generation.");
    }

    public static FatCurrentEvidenceAssessment Apply(IoTestPointPlan point)
    {
        ArgumentNullException.ThrowIfNull(point);
        var assessment = Evaluate(point);
        if (point.CaptureMode == FatCaptureMode.AutomaticTransition && point.IsFatEvidenceComplete)
        {
            point.Runtime.State = assessment.State;
            point.Runtime.StatusReason = assessment.Reason;
        }
        return assessment;
    }

    private static CurrentEvidence? EffectiveValue1(IoTestPointPlan point)
    {
        if (point.Runtime.Value1Evidence is { } generic)
            return CurrentEvidence.From(generic);
        if (point.Runtime.OnEvidence is { } legacy)
            return CurrentEvidence.From(legacy);
        return null;
    }

    private static CurrentEvidence? EffectiveValue2(IoTestPointPlan point)
    {
        if (point.Runtime.Value2Evidence is { } generic)
            return CurrentEvidence.From(generic);
        if (point.Runtime.OffEvidence is { } legacy)
            return CurrentEvidence.From(legacy);
        return null;
    }

    private static string StateLabel(bool state) => state ? "TRUE" : "FALSE";

    private sealed record CurrentEvidence(
        string RawValue,
        string Quality,
        long Sequence,
        long ConnectionGeneration)
    {
        public static CurrentEvidence From(FatValueEvidence evidence)
            => new(
                evidence.RawValue,
                evidence.Quality,
                evidence.Sequence,
                evidence.ConnectionGeneration);

        public static CurrentEvidence From(IoTestTransitionEvidence evidence)
            => new(
                evidence.RawValue,
                evidence.Quality,
                evidence.Sequence,
                evidence.ConnectionGeneration);
    }
}

public sealed record FatCurrentEvidenceAssessment(
    IoTestPointState State,
    string Reason);
