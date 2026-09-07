using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class FatCurrentEvidenceAssessmentRegressionTests
{
    [Fact]
    public void GenericCurrentPair_FalseToTrue_IsPass()
    {
        var point = NewDiscretePoint("FALSE-TRUE");
        SetCurrentPair(point, "False", 10, "True", 11);

        var assessment = FatCurrentEvidenceAssessmentService.Apply(point);

        Assert.Equal(IoTestPointState.Passed, assessment.State);
        Assert.Equal(IoTestPointState.Passed, point.Runtime.State);
        Assert.Equal("✔ PASS", point.FatResultText);
        Assert.Contains("FALSE -> TRUE", assessment.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericCurrentPair_TrueToFalse_IsPass_NotLegacyWaitingState()
    {
        var point = NewDiscretePoint("TRUE-FALSE");
        SetCurrentPair(point, "True", 20, "False", 21);
        point.Runtime.State = IoTestPointState.ArmedForOn;

        var assessment = FatCurrentEvidenceAssessmentService.Apply(point);

        Assert.True(point.IsFatEvidenceComplete);
        Assert.Equal(IoTestPointState.Passed, assessment.State);
        Assert.Equal(IoTestPointState.Passed, point.Runtime.State);
        Assert.Equal("COMPLETE", point.FatStatusText);
        Assert.Equal("✔ PASS", point.FatResultText);
        Assert.Contains("TRUE -> FALSE", assessment.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericCurrentPair_SameState_IsReview()
    {
        var point = NewDiscretePoint("SAME");
        SetCurrentPair(point, "True", 30, "True", 31);

        var assessment = FatCurrentEvidenceAssessmentService.Apply(point);

        Assert.Equal(IoTestPointState.Review, assessment.State);
        Assert.Equal("⚠ REVIEW", point.FatResultText);
        Assert.Contains("same discrete state", assessment.Reason, StringComparison.Ordinal);
        Assert.Contains("does not prove both FAT states", assessment.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericCurrentPair_DifferentConnectionGeneration_RemainsPassWhenStatesAreOpposite()
    {
        var point = NewDiscretePoint("GENERATION");
        point.Runtime.SetFatValueEvidence(Evidence(FatValueSlot.Value1, "False", 40, generation: 1));
        point.Runtime.SetFatValueEvidence(Evidence(FatValueSlot.Value2, "True", 41, generation: 2));

        var assessment = FatCurrentEvidenceAssessmentService.Apply(point);

        Assert.Equal(IoTestPointState.Passed, assessment.State);
        Assert.Equal("✔ PASS", point.FatResultText);
        Assert.Contains("FALSE -> TRUE", assessment.Reason, StringComparison.Ordinal);
        Assert.Contains("provenance only", assessment.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RecapturedValue1_NewerThanRetainedValue2_InvalidatesStalePass()
    {
        var point = NewDiscretePoint("STALE-PASS");
        SetCurrentPair(point, "False", 50, "True", 51);
        Assert.Equal(IoTestPointState.Passed, FatCurrentEvidenceAssessmentService.Apply(point).State);

        point.Runtime.SetFatValueEvidence(Evidence(
            FatValueSlot.Value1,
            "True",
            52,
            captureKind: FatEvidenceCaptureKind.OperatorRecapture));

        var assessment = FatCurrentEvidenceAssessmentService.Apply(point);

        Assert.Equal(IoTestPointState.Review, assessment.State);
        Assert.Equal("⚠ REVIEW", point.FatResultText);
        Assert.Contains("same discrete state", assessment.Reason, StringComparison.Ordinal);
        Assert.Contains("does not prove both FAT states", assessment.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyOnlyCompletedTransition_PreservesLegacyPassAuthority()
    {
        var point = NewDiscretePoint("LEGACY");
        var evaluator = new IoTestTransitionEvaluator();

        evaluator.StartAttempt(point, Observation(false, 1));
        evaluator.Observe(point, Observation(true, 2));
        evaluator.Observe(point, Observation(false, 3));

        Assert.Null(point.Runtime.Value1Evidence);
        Assert.Null(point.Runtime.Value2Evidence);
        Assert.True(point.IsFatEvidenceComplete);
        Assert.Equal(IoTestPointState.Passed, point.Runtime.State);

        var assessment = FatCurrentEvidenceAssessmentService.Apply(point);

        Assert.Equal(IoTestPointState.Passed, assessment.State);
        Assert.Equal(IoTestPointState.Passed, point.Runtime.State);
        Assert.Equal("✔ PASS", point.FatResultText);
    }

    [Fact]
    public void GenericCurrentPair_QuestionableQuality_IsReview()
    {
        var point = NewDiscretePoint("QUALITY");
        point.Runtime.SetFatValueEvidence(Evidence(FatValueSlot.Value1, "False", 60, quality: "Good"));
        point.Runtime.SetFatValueEvidence(Evidence(FatValueSlot.Value2, "True", 61, quality: "Unknown"));

        var assessment = FatCurrentEvidenceAssessmentService.Apply(point);

        Assert.Equal(IoTestPointState.Review, assessment.State);
        Assert.Equal("⚠ REVIEW", point.FatResultText);
        Assert.Contains("quality is not fully accepted", assessment.Reason, StringComparison.Ordinal);
    }

    private static void SetCurrentPair(
        IoTestPointPlan point,
        string value1,
        long sequence1,
        string value2,
        long sequence2)
    {
        point.Runtime.SetFatValueEvidence(Evidence(FatValueSlot.Value1, value1, sequence1));
        point.Runtime.SetFatValueEvidence(Evidence(FatValueSlot.Value2, value2, sequence2));
    }

    private static IoTestPointPlan NewDiscretePoint(string id) => new()
    {
        TestPointId = id,
        IedName = "IED1",
        IpAddress = "192.0.2.10",
        SignalName = id,
        ObjectReference = $"IED1LD0/GGIO1.{id}.stVal",
        FunctionalConstraint = "ST",
        ExpectedOnText = "True",
        ExpectedOffText = "False",
        ExpectedOnRaw = 1,
        ExpectedOffRaw = 0,
        SignalKind = FatSignalKind.Discrete,
        CaptureMode = FatCaptureMode.AutomaticTransition,
        WorkspaceSelected = true,
        TestEnabled = true,
        ImportReady = true,
        BindingStatus = IoTestSignalSelectionService.SclWorkspaceAuthorityBindingStatus
    };

    private static FatValueEvidence Evidence(
        FatValueSlot slot,
        string raw,
        long sequence,
        long generation = 1,
        string quality = "Good",
        FatEvidenceCaptureKind captureKind = FatEvidenceCaptureKind.AutomaticValue)
    {
        var captured = new DateTimeOffset(2026, 9, 3, 7, 0, 0, TimeSpan.Zero)
            .AddMilliseconds(sequence * 10);
        return new FatValueEvidence(
            Guid.NewGuid(),
            slot,
            captureKind,
            raw,
            captured,
            captured.AddMilliseconds(-2),
            quality,
            "BRCB",
            sequence,
            generation);
    }

    private static IoTestObservation Observation(bool state, long sequence)
    {
        var captured = new DateTimeOffset(2026, 9, 3, 7, 0, 0, TimeSpan.Zero)
            .AddMilliseconds(sequence * 10);
        return new IoTestObservation(
            state,
            state ? "True" : "False",
            captured,
            captured.AddMilliseconds(-2),
            "Good",
            "BRCB",
            sequence,
            1);
    }
}