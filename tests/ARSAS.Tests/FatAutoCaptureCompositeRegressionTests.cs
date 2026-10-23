using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class FatAutoCaptureCompositeRegressionTests
{
    [Fact]
    public void CompositeThreePhaseValue_RollsWhenOnlyPhaseCChanges()
    {
        var point = new IoTestPointPlan
        {
            TestPointId = "thda-parent",
            IedName = "IED1",
            IpAddress = "192.0.2.10",
            SignalName = "ThdA",
            ObjectReference = "IED1LD0/MHAI1.ThdA",
            FunctionalConstraint = "MX",
            ExpectedOnText = string.Empty,
            ExpectedOffText = string.Empty,
            SignalKind = FatSignalKind.Other,
            CaptureMode = FatCaptureMode.AutomaticTransition,
            ImportReady = true
        };

        point.Runtime.SetFatValueEvidence(Evidence(
            FatValueSlot.Value1,
            "A=0, B=0, C=0",
            sequence: 1));
        point.Runtime.SetFatValueEvidence(Evidence(
            FatValueSlot.Value2,
            "A=12, B=13, C=0",
            sequence: 2));

        var coordinator = new FatAutoCaptureCoordinator();
        var decision = coordinator.Observe(
            point,
            new IoTestObservation(
                NormalizedState: null,
                RawValue: "A=12, B=13, C=14",
                CapturedAt: DateTimeOffset.UtcNow,
                IedTimestamp: DateTimeOffset.UtcNow,
                Quality: "good",
                AcquisitionSource: "BRCB report",
                Sequence: 3,
                ConnectionGeneration: 1));

        Assert.NotNull(decision.Evidence);
        Assert.Equal(FatValueSlot.Value2, decision.Evidence!.Slot);
        Assert.Equal("A=12, B=13, C=14", decision.Evidence.RawValue);
        Assert.NotNull(decision.ShiftedValue1Evidence);
        Assert.Equal("A=12, B=13, C=0", decision.ShiftedValue1Evidence!.RawValue);
        Assert.Equal(FatAutoCaptureStage.Complete, decision.Stage);
    }

    private static FatValueEvidence Evidence(FatValueSlot slot, string value, long sequence)
        => new(
            Guid.NewGuid(),
            slot,
            FatEvidenceCaptureKind.AutomaticValue,
            value,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "good",
            "BRCB report",
            sequence,
            1);
}
