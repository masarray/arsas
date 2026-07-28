using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record IoTestEvaluationResult(
    bool StateChanged,
    IoTestPointState State,
    IoTestTransitionEvidence? Evidence,
    string Reason);

public sealed class IoTestTransitionEvaluator
{
    public IoTestEvaluationResult StartAttempt(IoTestPointPlan point, IoTestObservation baseline)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(baseline);

        var runtime = point.Runtime;
        runtime.ResetAttempt();
        runtime.ApplyObservation(baseline);
        runtime.ConnectionGeneration = baseline.ConnectionGeneration;
        runtime.LastSequence = baseline.Sequence;
        runtime.LastObservedState = baseline.NormalizedState;

        if (!point.TestEnabled)
        {
            runtime.State = IoTestPointState.NotStarted;
            runtime.StatusReason = "Signal is disabled in the FAT test plan";
            return Result(runtime, changed: true, evidence: null);
        }

        if (!point.ImportReady || string.IsNullOrWhiteSpace(point.ObjectReference))
        {
            runtime.State = IoTestPointState.Review;
            runtime.StatusReason = "Signal is not safely bound to an IEC 61850 object reference";
            return Result(runtime, changed: true, evidence: null);
        }

        ApplyBaseline(runtime, baseline.NormalizedState);
        return Result(runtime, changed: true, evidence: null);
    }

    public IoTestEvaluationResult Observe(IoTestPointPlan point, IoTestObservation observation)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(observation);

        var runtime = point.Runtime;
        if (!point.TestEnabled)
        {
            runtime.ApplyObservation(observation);
            return Result(runtime, changed: false, evidence: null, "Signal is disabled");
        }

        if (runtime.State == IoTestPointState.NotStarted)
        {
            runtime.ApplyObservation(observation);
            return Result(runtime, changed: false, evidence: null, "Test attempt has not been started");
        }

        if (runtime.ConnectionGeneration != observation.ConnectionGeneration)
        {
            runtime.ApplyObservation(observation);
            return HandleConnectionGenerationChange(runtime, observation);
        }

        if (observation.Sequence <= runtime.LastSequence)
            return Result(runtime, changed: false, evidence: null, "Duplicate or out-of-order observation ignored");

        runtime.ApplyObservation(observation);
        var previous = runtime.LastObservedState;
        runtime.LastSequence = observation.Sequence;
        runtime.LastObservedState = observation.NormalizedState;

        if (observation.NormalizedState is null)
        {
            runtime.StatusReason = "Observation has no normalized digital state";
            return Result(runtime, changed: false, evidence: null);
        }

        return runtime.State switch
        {
            IoTestPointState.WaitingForBaseline => HandleBaseline(runtime, observation),
            IoTestPointState.WaitingForOffBaseline => HandleWaitingForOffBaseline(runtime, observation),
            IoTestPointState.ArmedForOn => HandleArmedForOn(runtime, observation, previous),
            IoTestPointState.OnCaptured => HandleOnCaptured(runtime, observation, previous),
            _ => Result(runtime, changed: false, evidence: null, "Observation retained without changing the completed test state")
        };
    }

    private static IoTestEvaluationResult HandleConnectionGenerationChange(
        IoTestPointRuntime runtime,
        IoTestObservation observation)
    {
        runtime.ConnectionGeneration = observation.ConnectionGeneration;
        runtime.LastSequence = observation.Sequence;
        runtime.LastObservedState = observation.NormalizedState;

        if (runtime.OnEvidence != null && runtime.OffEvidence == null)
        {
            runtime.State = IoTestPointState.Review;
            runtime.StatusReason = "IED connection changed after ON evidence; OFF transition continuity cannot be proven";
            return Result(runtime, changed: true, evidence: null);
        }

        runtime.State = IoTestPointState.WaitingForBaseline;
        runtime.StatusReason = "Connection changed; first image is baseline only";
        ApplyBaseline(runtime, observation.NormalizedState);
        return Result(runtime, changed: true, evidence: null);
    }

    private static IoTestEvaluationResult HandleBaseline(
        IoTestPointRuntime runtime,
        IoTestObservation observation)
    {
        ApplyBaseline(runtime, observation.NormalizedState);
        return Result(runtime, changed: true, evidence: null);
    }

    private static IoTestEvaluationResult HandleWaitingForOffBaseline(
        IoTestPointRuntime runtime,
        IoTestObservation observation)
    {
        if (observation.NormalizedState != false)
            return Result(runtime, changed: false, evidence: null, "Waiting for OFF baseline before arming ON evidence");

        runtime.State = IoTestPointState.ArmedForOn;
        runtime.StatusReason = "OFF baseline confirmed; waiting for a new ON transition";
        return Result(runtime, changed: true, evidence: null);
    }

    private static IoTestEvaluationResult HandleArmedForOn(
        IoTestPointRuntime runtime,
        IoTestObservation observation,
        bool? previous)
    {
        if (previous != false || observation.NormalizedState != true)
            return Result(runtime, changed: false, evidence: null, "Waiting for OFF to ON transition");

        var evidence = CreateEvidence(IoEvidenceTransition.On, previous, observation);
        if (evidence.Verdict == IoEvidenceVerdict.Rejected)
        {
            runtime.StatusReason = $"ON transition rejected: {evidence.VerdictReason}";
            return Result(runtime, changed: false, evidence);
        }

        runtime.OnEvidence = evidence;
        runtime.State = IoTestPointState.OnCaptured;
        runtime.StatusReason = evidence.Verdict == IoEvidenceVerdict.Accepted
            ? "ON evidence captured; waiting for OFF transition"
            : "ON evidence captured for review; waiting for OFF transition";
        return Result(runtime, changed: true, evidence);
    }

    private static IoTestEvaluationResult HandleOnCaptured(
        IoTestPointRuntime runtime,
        IoTestObservation observation,
        bool? previous)
    {
        if (previous != true || observation.NormalizedState != false)
            return Result(runtime, changed: false, evidence: null, "Waiting for ON to OFF transition");

        var evidence = CreateEvidence(IoEvidenceTransition.Off, previous, observation);
        if (evidence.Verdict == IoEvidenceVerdict.Rejected)
        {
            runtime.StatusReason = $"OFF transition rejected: {evidence.VerdictReason}";
            return Result(runtime, changed: false, evidence);
        }

        runtime.OffEvidence = evidence;
        var onAccepted = runtime.OnEvidence?.Verdict == IoEvidenceVerdict.Accepted;
        var offAccepted = evidence.Verdict == IoEvidenceVerdict.Accepted;
        runtime.State = onAccepted && offAccepted
            ? IoTestPointState.Passed
            : IoTestPointState.Review;
        runtime.StatusReason = runtime.State == IoTestPointState.Passed
            ? "PASS: ON and OFF transitions captured in order"
            : "ON and OFF evidence captured, but one or both transitions require review";
        return Result(runtime, changed: true, evidence);
    }

    private static void ApplyBaseline(IoTestPointRuntime runtime, bool? state)
    {
        runtime.State = state switch
        {
            false => IoTestPointState.ArmedForOn,
            true => IoTestPointState.WaitingForOffBaseline,
            null => IoTestPointState.WaitingForBaseline
        };
        runtime.StatusReason = state switch
        {
            false => "OFF baseline confirmed; waiting for a new ON transition",
            true => "Signal was already ON; waiting for OFF baseline before testing",
            null => "Waiting for a trustworthy digital baseline"
        };
    }

    private static IoTestTransitionEvidence CreateEvidence(
        IoEvidenceTransition transition,
        bool? previous,
        IoTestObservation observation)
    {
        var (verdict, reason) = EvaluateQuality(observation.Quality);
        return new IoTestTransitionEvidence(
            Guid.NewGuid(),
            transition,
            previous,
            observation.NormalizedState!.Value,
            observation.RawValue,
            observation.CapturedAt,
            observation.IedTimestamp,
            observation.Quality,
            observation.AcquisitionSource,
            observation.Sequence,
            observation.ConnectionGeneration,
            verdict,
            reason);
    }

    internal static (IoEvidenceVerdict Verdict, string Reason) EvaluateQuality(string? quality)
    {
        var normalized = (quality ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Contains("invalid") ||
            normalized.Contains("bad") ||
            normalized.Contains("failure") ||
            normalized.Contains("blocked"))
        {
            return (IoEvidenceVerdict.Rejected, "IEC 61850 quality is invalid or bad");
        }

        if (normalized.Contains("good") || normalized == "valid")
            return (IoEvidenceVerdict.Accepted, "Quality accepted");

        return (IoEvidenceVerdict.Review, "Quality is missing, unknown, or questionable");
    }

    private static IoTestEvaluationResult Result(
        IoTestPointRuntime runtime,
        bool changed,
        IoTestTransitionEvidence? evidence,
        string? reason = null)
    {
        return new IoTestEvaluationResult(
            changed,
            runtime.State,
            evidence,
            reason ?? runtime.StatusReason);
    }
}
