using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoTestTransitionEvaluatorTests
{
    private readonly IoTestTransitionEvaluator _evaluator = new();

    [Fact]
    public void OffOnOffSequence_PassesWithOrderedEvidence()
    {
        var point = CreatePoint();
        _evaluator.StartAttempt(point, Observation(false, 1));

        var on = _evaluator.Observe(point, Observation(true, 2));
        var off = _evaluator.Observe(point, Observation(false, 3));

        Assert.Equal(IoTestPointState.OnCaptured, on.State);
        Assert.Equal(IoEvidenceTransition.On, on.Evidence?.Transition);
        Assert.Equal(IoTestPointState.Passed, off.State);
        Assert.Equal(IoEvidenceTransition.Off, off.Evidence?.Transition);
        Assert.NotNull(point.Runtime.OnEvidence);
        Assert.NotNull(point.Runtime.OffEvidence);
        Assert.True(point.Runtime.OffEvidence!.CapturedAt > point.Runtime.OnEvidence!.CapturedAt);
    }

    [Fact]
    public void InitiallyOn_DoesNotCreateOnEvidenceUntilNewCycle()
    {
        var point = CreatePoint();
        var start = _evaluator.StartAttempt(point, Observation(true, 1));

        Assert.Equal(IoTestPointState.WaitingForOffBaseline, start.State);
        Assert.Null(point.Runtime.OnEvidence);

        var baselineOff = _evaluator.Observe(point, Observation(false, 2));
        var on = _evaluator.Observe(point, Observation(true, 3));

        Assert.Equal(IoTestPointState.ArmedForOn, baselineOff.State);
        Assert.Equal(IoTestPointState.OnCaptured, on.State);
        Assert.Equal(3, point.Runtime.OnEvidence?.Sequence);
    }

    [Fact]
    public void DuplicateValues_DoNotCreateEvidence()
    {
        var point = CreatePoint();
        _evaluator.StartAttempt(point, Observation(false, 1));

        var duplicateOff = _evaluator.Observe(point, Observation(false, 2));
        var on = _evaluator.Observe(point, Observation(true, 3));
        var duplicateOn = _evaluator.Observe(point, Observation(true, 4));

        Assert.False(duplicateOff.StateChanged);
        Assert.Null(duplicateOff.Evidence);
        Assert.Equal(IoTestPointState.OnCaptured, on.State);
        Assert.False(duplicateOn.StateChanged);
        Assert.Null(duplicateOn.Evidence);
        Assert.Null(point.Runtime.OffEvidence);
    }

    [Fact]
    public void ReconnectAfterOnEvidence_ForcesReviewInsteadOfFalsePass()
    {
        var point = CreatePoint();
        _evaluator.StartAttempt(point, Observation(false, 1, generation: 10));
        _evaluator.Observe(point, Observation(true, 2, generation: 10));

        var firstImageAfterReconnect = _evaluator.Observe(
            point,
            Observation(false, 1, generation: 11));

        Assert.Equal(IoTestPointState.Review, firstImageAfterReconnect.State);
        Assert.Null(point.Runtime.OffEvidence);
        Assert.Contains("continuity cannot be proven", point.Runtime.StatusReason);
    }

    [Fact]
    public void QuestionableOnQuality_CapturesEvidenceButFinalResultNeedsReview()
    {
        var point = CreatePoint();
        _evaluator.StartAttempt(point, Observation(false, 1));

        var on = _evaluator.Observe(point, Observation(true, 2, quality: "Questionable"));
        var off = _evaluator.Observe(point, Observation(false, 3, quality: "Good"));

        Assert.Equal(IoEvidenceVerdict.Review, on.Evidence?.Verdict);
        Assert.Equal(IoTestPointState.Review, off.State);
        Assert.NotNull(point.Runtime.OnEvidence);
        Assert.NotNull(point.Runtime.OffEvidence);
    }

    [Fact]
    public void InvalidQuality_DoesNotAdvanceTheStateMachine()
    {
        var point = CreatePoint();
        _evaluator.StartAttempt(point, Observation(false, 1));

        var invalidOn = _evaluator.Observe(point, Observation(true, 2, quality: "Invalid"));

        Assert.Equal(IoTestPointState.ArmedForOn, invalidOn.State);
        Assert.Equal(IoEvidenceVerdict.Rejected, invalidOn.Evidence?.Verdict);
        Assert.Null(point.Runtime.OnEvidence);
    }

    [Fact]
    public void OutOfOrderSequence_IsIgnored()
    {
        var point = CreatePoint();
        _evaluator.StartAttempt(point, Observation(false, 10));

        var stale = _evaluator.Observe(point, Observation(true, 9));

        Assert.False(stale.StateChanged);
        Assert.Null(stale.Evidence);
        Assert.Equal(IoTestPointState.ArmedForOn, stale.State);
    }

    private static IoTestPointPlan CreatePoint()
    {
        return new IoTestPointPlan
        {
            TestPointId = "CCPP-AA1C1F03R4-0001",
            IedName = "AA1C1F03R4",
            IpAddress = "192.168.81.70",
            SignalName = "CB closed",
            ObjectReference = "AA1C1F03R4ADD/GGIO6.CBClsd.stVal",
            FunctionalConstraint = "ST",
            ExpectedOnText = "Active",
            ExpectedOffText = "InActive",
            ImportReady = true,
            BindingStatus = "CID_DATASET_EXACT"
        };
    }

    private static IoTestObservation Observation(
        bool state,
        long sequence,
        long generation = 1,
        string quality = "Good")
    {
        var captured = new DateTimeOffset(2026, 7, 28, 8, 0, 0, TimeSpan.Zero)
            .AddMilliseconds(sequence * 100);
        return new IoTestObservation(
            state,
            state ? "true" : "false",
            captured,
            captured.AddMilliseconds(-5),
            quality,
            "BRCB",
            sequence,
            generation);
    }
}
