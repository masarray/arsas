using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class FatAutoCaptureOperatorRollingTests
{
    [Fact]
    public void OperatorOwnedCompletePair_DoesNotFreezeLatestAutomaticProcessPair()
    {
        var point = new IoTestPointPlan
        {
            TestPointId = "THDA-ROLLING",
            IedName = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            SignalName = "ThdA",
            ObjectReference = "AA1E1F06R4VT3p1_THDHarmonics/I_MHAI1.ThdA",
            FunctionalConstraint = "MX",
            SignalKind = FatSignalKind.Analog,
            CaptureMode = FatCaptureMode.OperatorSnapshot,
            WorkspaceSelected = true,
            TestEnabled = true,
            ImportReady = true,
            BindingStatus = "CID_DATASET_EXACT"
        };

        var t0 = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
        point.Runtime.SetFatValueEvidence(new FatValueEvidence(
            Guid.NewGuid(), FatValueSlot.Value1, FatEvidenceCaptureKind.OperatorRecapture,
            "A=0,B=0,C=0", t0, t0, "Good", "BRCB", 1, 1));
        point.Runtime.SetFatValueEvidence(new FatValueEvidence(
            Guid.NewGuid(), FatValueSlot.Value2, FatEvidenceCaptureKind.OperatorRecapture,
            "A=11,B=12,C=0", t0.AddMilliseconds(10), t0.AddMilliseconds(10), "Good", "BRCB", 2, 1));

        var coordinator = new FatAutoCaptureCoordinator();
        var decision = coordinator.Observe(point, new IoTestObservation(
            null,
            "A=11,B=12,C=13",
            t0.AddMilliseconds(20),
            t0.AddMilliseconds(20),
            "Good",
            "InformationReport/BRCB",
            3,
            1));

        Assert.NotNull(decision.Evidence);
        Assert.Equal(FatValueSlot.Value2, decision.Evidence!.Slot);
        Assert.Equal("A=11,B=12,C=13", decision.Evidence.RawValue);
        Assert.NotNull(decision.ShiftedValue1Evidence);
        Assert.Equal("A=11,B=12,C=0", decision.ShiftedValue1Evidence!.RawValue);
    }
}
