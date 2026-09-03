using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Assesses the exact Value 1 / Value 2 pair currently presented by FAT v2.
///
/// The legacy transition state machine remains responsible for collecting historical
/// OFF -> ON -> OFF evidence. Once generic FAT evidence overrides either current slot,
/// this service becomes the assessment authority only while that pair belongs to the
/// current connection generation. A purely automatic pair that predates or straddles a
/// reconnect must not erase the legacy continuity verdict established by Resume/rebind.
/// Explicit operator Recapture remains authoritative and therefore fails closed to REVIEW
/// when its displayed pair is not coherent.
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

        var hasOperatorOverride = value1.IsOperatorOverride || value2.IsOperatorOverride;
        var runtimeGeneration = point.Runtime.ConnectionGeneration;
        var pairIsSameGeneration = value1.ConnectionGeneration == value2.ConnectionGeneration;
        var pairMatchesCurrentGeneration =
            pairIsSameGeneration &&
            (runtimeGeneration <= 0 || value1.ConnectionGeneration == runtimeGeneration);

        if (!pairMatchesCurrentGeneration)
        {
            if (!hasOperatorOverride)
            {
                // Resume/rebind deliberately establishes a new continuity authority. Old
                // automatic V1/V2 pointers may remain visible for audit, but they must not
                // overwrite REVIEW after a potentially missed edge or overwrite a later
                // PASS earned by a complete new legacy cycle. If no terminal continuity
                // verdict exists yet, fail closed to REVIEW rather than showing COMPLETE
                // with a blank/non-terminal Result.
                return PreserveRuntimeAssessment(point);
            }

            return new FatCurrentEvidenceAssessment(
                IoTestPointState.Review,
                pairIsSameGeneration
                    ? "REVIEW: the operator-selected Value 1 / Value 2 pair belongs to an earlier IED connection generation; recapture a coherent current pair."
                    : "REVIEW: the operator-selected Value 1 and Value 2 belong to different IED connection generations; recapture a coherent current pair.");
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
                "REVIEW: current Value 2 does not follow current Value 1 in the live evidence sequence; recapture Value 2 after the intended condition change.");
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
            $"PASS: current Value 1 -> Value 2 evidence proves a good-quality {StateLabel(state1.Value)} -> {StateLabel(state2.Value)} transition in one connection generation.");
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

    private static FatCurrentEvidenceAssessment PreserveRuntimeAssessment(IoTestPointPlan point)
    {
        var terminalState = point.Runtime.State is IoTestPointState.Passed or IoTestPointState.Failed or IoTestPointState.Review;
        if (terminalState)
        {
            return new FatCurrentEvidenceAssessment(
                point.Runtime.State,
                string.IsNullOrWhiteSpace(point.Runtime.StatusReason)
                    ? "Automatic current Value 1 / Value 2 evidence predates or straddles the active IED connection generation; the existing live transition continuity verdict remains authoritative."
                    : point.Runtime.StatusReason);
        }

        return new FatCurrentEvidenceAssessment(
            IoTestPointState.Review,
            "REVIEW: automatic current Value 1 / Value 2 evidence predates or straddles the active IED connection generation and no terminal live transition continuity verdict is available.");
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
        long ConnectionGeneration,
        bool IsOperatorOverride)
    {
        public static CurrentEvidence From(FatValueEvidence evidence)
            => new(
                evidence.RawValue,
                evidence.Quality,
                evidence.Sequence,
                evidence.ConnectionGeneration,
                evidence.CaptureKind != FatEvidenceCaptureKind.AutomaticValue);

        public static CurrentEvidence From(IoTestTransitionEvidence evidence)
            => new(
                evidence.RawValue,
                evidence.Quality,
                evidence.Sequence,
                evidence.ConnectionGeneration,
                false);
    }
}

public sealed record FatCurrentEvidenceAssessment(
    IoTestPointState State,
    string Reason);
