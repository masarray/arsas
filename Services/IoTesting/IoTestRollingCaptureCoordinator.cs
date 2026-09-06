using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Runs live FAT capture attempts without making a completed result destructive.
///
/// The legacy transition evaluator remains the authority for OFF -> ON -> OFF ordering,
/// quality and timestamp verdicts. This coordinator gives that evaluator a short-lived
/// shadow point for each active capture cycle. When a point already has current evidence,
/// candidate ON/OFF evidence stays in the shadow until a complete cycle is available;
/// only then is the current project evidence atomically replaced. An interrupted or
/// rejected recapture therefore leaves the last completed/current evidence untouched.
/// </summary>
public sealed class IoTestRollingCaptureCoordinator
{
    private readonly IoTestTransitionEvaluator _evaluator;
    private readonly Dictionary<IoTestPointPlan, CaptureSlot> _slots =
        new(ReferenceEqualityComparer.Instance);

    public IoTestRollingCaptureCoordinator(IoTestTransitionEvaluator evaluator)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    }

    public IoTestEvaluationResult Start(IoTestPointPlan point, IoTestObservation baseline)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(baseline);

        var hadCurrentEvidence = HasCurrentEvidence(point.Runtime);
        var shadow = CreateShadow(point);
        var evaluation = _evaluator.StartAttempt(shadow, baseline);
        var slot = new CaptureSlot(shadow, hadCurrentEvidence);
        _slots[point] = slot;

        point.Runtime.Attempt++;
        ApplyLiveObservation(point.Runtime, shadow.Runtime, baseline);

        if (hadCurrentEvidence)
        {
            point.Runtime.StatusReason = PreserveReason(evaluation.Reason);
            return ProjectResult(evaluation, point.Runtime, point.Runtime.StatusReason);
        }

        CopyCaptureResult(shadow.Runtime, point.Runtime);
        if (shadow.Runtime.IsComplete)
            CommitOrRearm(point, slot, baseline);
        return ProjectResult(evaluation, point.Runtime, point.Runtime.StatusReason);
    }

    /// <summary>
    /// Re-arms a transition capture after an explicit pause/reconnect continuity gap.
    /// Existing partial evidence remains visible for audit, but it cannot be completed
    /// by an edge that may have occurred while capture was paused.
    /// </summary>
    public IoTestEvaluationResult RearmAfterContinuityGap(IoTestPointPlan point, IoTestObservation baseline)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(baseline);

        var shadow = CreateShadow(point);
        _evaluator.StartAttempt(shadow, baseline);
        _slots[point] = new CaptureSlot(shadow, hasCurrentEvidence: true);

        point.Runtime.Attempt++;
        ApplyLiveObservation(point.Runtime, shadow.Runtime, baseline);
        point.Runtime.State = IoTestPointState.Review;
        point.Runtime.StatusReason =
            "Capture continuity cannot be proven across pause/reconnect while only one transition edge was recorded; partial evidence is preserved and capture is re-armed from the current live baseline.";

        return new IoTestEvaluationResult(
            true,
            point.Runtime.State,
            null,
            point.Runtime.StatusReason);
    }

    public IoTestEvaluationResult Observe(IoTestPointPlan point, IoTestObservation observation)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(observation);

        if (!_slots.TryGetValue(point, out var slot))
            return _evaluator.Observe(point, observation);

        if (slot.AttemptNeedsIncrement)
        {
            point.Runtime.Attempt++;
            slot.AttemptNeedsIncrement = false;
        }

        var evaluation = _evaluator.Observe(slot.Shadow, observation);
        ApplyLiveObservation(point.Runtime, slot.Shadow.Runtime, observation);

        if (!slot.HasCurrentEvidence)
        {
            // First evidence for a point keeps the legacy progressive UI semantics.
            // ON is visible immediately; a terminal first attempt becomes the current
            // result even when it is REVIEW with only partial evidence.
            CopyCaptureResult(slot.Shadow.Runtime, point.Runtime);
            if (slot.Shadow.Runtime.IsComplete)
            {
                slot.HasCurrentEvidence = HasCurrentEvidence(point.Runtime);
                Rearm(point, slot, observation);
            }

            return ProjectResult(evaluation, point.Runtime, point.Runtime.StatusReason);
        }

        if (HasCompleteCycle(slot.Shadow.Runtime))
        {
            // Atomic promotion: both candidate transitions become the new current pair
            // together. The previous pair remains current until this exact point.
            CopyCaptureResult(slot.Shadow.Runtime, point.Runtime);
            slot.HasCurrentEvidence = true;
            var promotedReason = point.Runtime.StatusReason;
            Rearm(point, slot, observation);
            point.Runtime.StatusReason = promotedReason + "; capture remains armed for newer evidence until Stop";
            return ProjectResult(evaluation, point.Runtime, point.Runtime.StatusReason);
        }

        if (slot.Shadow.Runtime.IsComplete)
        {
            // A rejected OFF, reconnect after candidate ON, or other terminal candidate
            // must be present in the append-only journal but must not replace the last
            // completed/current evidence shown in the project/report.
            var rejectedReason = evaluation.Reason;
            Rearm(point, slot, observation);
            point.Runtime.StatusReason =
                $"New capture was not promotable ({rejectedReason}); current evidence preserved and capture re-armed";
            return ProjectResult(evaluation, point.Runtime, point.Runtime.StatusReason);
        }

        // Steady telemetry must not rewrite the operator-facing reason on every poll.
        // Before P3, live values were deliberately coalesced below input priority. P3's
        // rolling-capture path accidentally reintroduced a UI notification storm by
        // assigning an equivalent/preserved StatusReason for every unchanged sample,
        // even though the current FAT evidence pair had not changed. Update the reason
        // only when the shadow evaluator actually advances or emits new evidence.
        if (evaluation.StateChanged || evaluation.Evidence is not null)
            point.Runtime.StatusReason = PreserveReason(evaluation.Reason);

        return ProjectResult(evaluation, point.Runtime, point.Runtime.StatusReason);
    }

    public void Clear() => _slots.Clear();

    private void CommitOrRearm(IoTestPointPlan point, CaptureSlot slot, IoTestObservation observation)
    {
        slot.HasCurrentEvidence = HasCurrentEvidence(point.Runtime);
        if (slot.HasCurrentEvidence)
            Rearm(point, slot, observation);
    }

    private void Rearm(IoTestPointPlan point, CaptureSlot slot, IoTestObservation baseline)
    {
        slot.Shadow = CreateShadow(point);
        _evaluator.StartAttempt(slot.Shadow, baseline);
        slot.AttemptNeedsIncrement = true;
        point.Runtime.LastObservedState = slot.Shadow.Runtime.LastObservedState;
        point.Runtime.LastSequence = slot.Shadow.Runtime.LastSequence;
        point.Runtime.ConnectionGeneration = slot.Shadow.Runtime.ConnectionGeneration;
    }

    private static bool HasCurrentEvidence(IoTestPointRuntime runtime)
        => runtime.IsComplete || runtime.OnEvidence != null || runtime.OffEvidence != null;

    private static bool HasCompleteCycle(IoTestPointRuntime runtime)
        => runtime.IsComplete && runtime.OnEvidence != null && runtime.OffEvidence != null;

    private static string PreserveReason(string reason)
        => $"{reason}; current evidence is preserved until a complete newer OFF -> ON -> OFF cycle is captured";

    private static IoTestEvaluationResult ProjectResult(
        IoTestEvaluationResult evaluation,
        IoTestPointRuntime runtime,
        string reason)
        => new(evaluation.StateChanged, runtime.State, evaluation.Evidence, reason);

    private static void ApplyLiveObservation(
        IoTestPointRuntime target,
        IoTestPointRuntime shadow,
        IoTestObservation observation)
    {
        target.ApplyObservation(observation);
        target.LastObservedState = shadow.LastObservedState;
        target.LastSequence = shadow.LastSequence;
        target.ConnectionGeneration = shadow.ConnectionGeneration;
    }

    private static void CopyCaptureResult(IoTestPointRuntime source, IoTestPointRuntime target)
    {
        target.State = source.State;
        target.LastObservedState = source.LastObservedState;
        target.LastSequence = source.LastSequence;
        target.ConnectionGeneration = source.ConnectionGeneration;
        target.OnEvidence = source.OnEvidence;
        target.OffEvidence = source.OffEvidence;
        target.StatusReason = source.StatusReason;
    }

    private static IoTestPointPlan CreateShadow(IoTestPointPlan point) => new()
    {
        TestPointId = point.TestPointId,
        IedName = point.IedName,
        IpAddress = point.IpAddress,
        SignalName = point.SignalName,
        ObjectReference = point.ObjectReference,
        FunctionalConstraint = point.FunctionalConstraint,
        ExpectedOnText = point.ExpectedOnText,
        ExpectedOffText = point.ExpectedOffText,
        ExpectedOnRaw = point.ExpectedOnRaw,
        ExpectedOffRaw = point.ExpectedOffRaw,
        DataType = point.DataType,
        ImportReady = point.ImportReady,
        BindingStatus = point.BindingStatus,
        TestEnabled = true
    };

    private sealed class CaptureSlot
    {
        public CaptureSlot(IoTestPointPlan shadow, bool hasCurrentEvidence)
        {
            Shadow = shadow;
            HasCurrentEvidence = hasCurrentEvidence;
        }

        public IoTestPointPlan Shadow { get; set; }
        public bool HasCurrentEvidence { get; set; }
        public bool AttemptNeedsIncrement { get; set; }
    }
}