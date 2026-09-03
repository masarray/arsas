using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoTestRollingCaptureCoordinatorTests
{
    [Fact]
    public void ExistingPass_IsPreservedUntilCompleteNewCycleThenAtomicallyReplaced()
    {
        var point = Point();
        var evaluator = new IoTestTransitionEvaluator();
        evaluator.StartAttempt(point, Observation(false, 1));
        evaluator.Observe(point, Observation(true, 2));
        evaluator.Observe(point, Observation(false, 3));
        var oldOn = point.Runtime.OnEvidence!.EvidenceId;
        var oldOff = point.Runtime.OffEvidence!.EvidenceId;

        var coordinator = new IoTestRollingCaptureCoordinator(new IoTestTransitionEvaluator());
        coordinator.Start(point, Observation(false, 10));

        Assert.Equal(IoTestPointState.Passed, point.Runtime.State);
        Assert.Equal(oldOn, point.Runtime.OnEvidence!.EvidenceId);
        Assert.Equal(oldOff, point.Runtime.OffEvidence!.EvidenceId);

        var candidateOn = coordinator.Observe(point, Observation(true, 11));

        Assert.Equal(IoEvidenceTransition.On, candidateOn.Evidence?.Transition);
        Assert.Equal(oldOn, point.Runtime.OnEvidence!.EvidenceId);
        Assert.Equal(oldOff, point.Runtime.OffEvidence!.EvidenceId);

        var candidateOff = coordinator.Observe(point, Observation(false, 12));

        Assert.Equal(IoEvidenceTransition.Off, candidateOff.Evidence?.Transition);
        Assert.Equal(IoTestPointState.Passed, point.Runtime.State);
        Assert.NotEqual(oldOn, point.Runtime.OnEvidence!.EvidenceId);
        Assert.NotEqual(oldOff, point.Runtime.OffEvidence!.EvidenceId);
        Assert.Equal(11, point.Runtime.OnEvidence.Sequence);
        Assert.Equal(12, point.Runtime.OffEvidence.Sequence);
        Assert.Contains("capture remains armed", point.Runtime.StatusReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InterruptedRecaptureAfterCandidateOn_PreservesCurrentEvidenceAndRearms()
    {
        var point = PassedPoint();
        var oldOn = point.Runtime.OnEvidence!.EvidenceId;
        var oldOff = point.Runtime.OffEvidence!.EvidenceId;
        var coordinator = new IoTestRollingCaptureCoordinator(new IoTestTransitionEvaluator());
        coordinator.Start(point, Observation(false, 10, generation: 1));
        coordinator.Observe(point, Observation(true, 11, generation: 1));

        var interrupted = coordinator.Observe(point, Observation(false, 1, generation: 2));

        Assert.True(interrupted.StateChanged);
        Assert.Equal(IoTestPointState.Passed, point.Runtime.State);
        Assert.Equal(oldOn, point.Runtime.OnEvidence!.EvidenceId);
        Assert.Equal(oldOff, point.Runtime.OffEvidence!.EvidenceId);
        Assert.Contains("preserved", point.Runtime.StatusReason, StringComparison.OrdinalIgnoreCase);

        coordinator.Observe(point, Observation(true, 2, generation: 2));
        coordinator.Observe(point, Observation(false, 3, generation: 2));

        Assert.NotEqual(oldOn, point.Runtime.OnEvidence!.EvidenceId);
        Assert.NotEqual(oldOff, point.Runtime.OffEvidence!.EvidenceId);
    }

    [Fact]
    public void FirstCapture_KeepsLegacyProgressiveVisibilityThenRemainsArmedForNewerCycles()
    {
        var point = Point();
        var coordinator = new IoTestRollingCaptureCoordinator(new IoTestTransitionEvaluator());

        var started = coordinator.Start(point, Observation(false, 1));
        var on = coordinator.Observe(point, Observation(true, 2));
        var off = coordinator.Observe(point, Observation(false, 3));

        Assert.Equal(IoTestPointState.ArmedForOn, started.State);
        Assert.Equal(IoTestPointState.OnCaptured, on.State);
        Assert.NotNull(point.Runtime.OnEvidence);
        Assert.Equal(IoTestPointState.Passed, off.State);
        Assert.NotNull(point.Runtime.OffEvidence);

        var firstOn = point.Runtime.OnEvidence!.EvidenceId;
        coordinator.Observe(point, Observation(true, 4));
        Assert.Equal(firstOn, point.Runtime.OnEvidence!.EvidenceId);
        coordinator.Observe(point, Observation(false, 5));
        Assert.NotEqual(firstOn, point.Runtime.OnEvidence!.EvidenceId);
    }

    private static IoTestPointPlan PassedPoint()
    {
        var point = Point();
        var evaluator = new IoTestTransitionEvaluator();
        evaluator.StartAttempt(point, Observation(false, 1));
        evaluator.Observe(point, Observation(true, 2));
        evaluator.Observe(point, Observation(false, 3));
        return point;
    }

    private static IoTestPointPlan Point() => new()
    {
        TestPointId = "TP-ROLLING-001",
        IedName = "IED1",
        IpAddress = "192.0.2.10",
        SignalName = "Trip",
        ObjectReference = "IED1LD0/GGIO1.Ind1.stVal",
        FunctionalConstraint = "ST",
        ExpectedOnText = "Active",
        ExpectedOffText = "Inactive",
        ImportReady = true,
        BindingStatus = "SCL_DATASET_EXACT"
    };

    private static IoTestObservation Observation(bool state, long sequence, long generation = 1)
    {
        var captured = new DateTimeOffset(2026, 9, 1, 1, 0, 0, TimeSpan.Zero)
            .AddMilliseconds(sequence * 100);
        return new IoTestObservation(
            state,
            state ? "True" : "False",
            captured,
            captured.AddMilliseconds(-3),
            "Good",
            "BRCB",
            sequence,
            generation);
    }
}
