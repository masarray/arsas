using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class FatAnalogAutoCaptureAssessmentIntegrationTests
{
    [Fact]
    public void StableAutoCapturedAnalogPair_WithMeaningfulChange_IsPass()
    {
        var point = NewAnalogPoint();
        var coordinator = new FatAutoCaptureCoordinator();
        long sequence = 0;

        Feed(point, coordinator, ref sequence, "0", "0", "0");
        Assert.Equal("0", point.Value1Text);
        Assert.Equal("WAITING V2", point.FatStatusText);

        Feed(
            point,
            coordinator,
            ref sequence,
            "18.412",
            "43.920",
            "61.850",
            "65.702",
            "65.746",
            "65.748",
            "65.748",
            "65.748");

        Assert.Equal("65.748", point.Value2Text);
        Assert.True(point.IsFatEvidenceComplete);

        var assessment = FatCurrentEvidenceAssessmentService.Apply(point);

        Assert.Equal(IoTestPointState.Passed, assessment.State);
        Assert.Equal(IoTestPointState.Passed, point.Runtime.State);
        Assert.Equal("COMPLETE", point.FatStatusText);
        Assert.Equal("✔ PASS", point.FatResultText);
    }

    [Fact]
    public void AnalogAutoCapture_DoesNotCreateValue2FromNoiseInsideSettlingTolerance()
    {
        var point = NewAnalogPoint();
        var coordinator = new FatAutoCaptureCoordinator();
        long sequence = 0;

        Feed(point, coordinator, ref sequence, "65.748", "65.748", "65.748");
        Assert.Equal("65.748", point.Value1Text);

        Feed(point, coordinator, ref sequence, "65.749", "65.749", "65.749", "65.749");

        Assert.Null(point.Runtime.Value2Evidence);
        Assert.False(point.IsFatEvidenceComplete);
        Assert.Equal("WAITING V2", point.FatStatusText);
        Assert.Equal("—", point.FatResultText);
    }

    private static void Feed(
        IoTestPointPlan point,
        FatAutoCaptureCoordinator coordinator,
        ref long sequence,
        params string[] values)
    {
        foreach (var raw in values)
        {
            var decision = coordinator.Observe(point, Observation(raw, ++sequence));
            if (decision.Evidence is null)
                continue;

            point.Runtime.SetFatValueEvidence(decision.Evidence);
            point.Runtime.AutoCaptureStage = decision.Stage;
        }
    }

    private static IoTestPointPlan NewAnalogPoint() => new()
    {
        TestPointId = "AN-AUTO-ASSESS",
        IedName = "IED1",
        IpAddress = "192.0.2.10",
        SignalName = "Current",
        ObjectReference = "IED1LD0/MMXU1.A.phsA.cVal.mag.f",
        FunctionalConstraint = "MX",
        ExpectedOnText = "Value 2",
        ExpectedOffText = "Value 1",
        SignalKind = FatSignalKind.Analog,
        CaptureMode = FatCaptureMode.OperatorSnapshot,
        WorkspaceSelected = true,
        TestEnabled = true,
        ImportReady = true,
        BindingStatus = IoTestSignalSelectionService.SclWorkspaceAuthorityBindingStatus
    };

    private static IoTestObservation Observation(string raw, long sequence)
    {
        var timestamp = new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero)
            .AddMilliseconds(sequence * 100);
        return new IoTestObservation(
            null,
            raw,
            timestamp,
            timestamp.AddMilliseconds(-2),
            "Good",
            "MMS-POLL",
            sequence,
            1);
    }
}
