using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class FatOperatorSnapshotCaptureTests
{
    [Fact]
    public void AnalogOperatorCapture_StoresValue1ThenValue2_AndCompletesEvidence()
    {
        var signal = AnalogSignal();

        var value1 = FatOperatorSnapshotCaptureService.Capture(
            signal,
            FatValueSlot.Value1,
            Observation("99.72 A", 1));
        var value2 = FatOperatorSnapshotCaptureService.Capture(
            signal,
            FatValueSlot.Value2,
            Observation("498.61 A", 2));

        Assert.Equal("99.72 A", value1.RawValue);
        Assert.Equal("498.61 A", value2.RawValue);
        Assert.Same(value1, signal.Value1Evidence);
        Assert.Same(value2, signal.Value2Evidence);
        Assert.True(signal.HasCompleteEvidence);
        Assert.True(signal.IsIncludedInFat);
    }

    [Fact]
    public void RecapturingSameSlot_ReplacesCurrentPointer_WithoutChangingInclusion()
    {
        var signal = AnalogSignal();
        var first = FatOperatorSnapshotCaptureService.Capture(
            signal,
            FatValueSlot.Value1,
            Observation("99.72 A", 1));
        var replacement = FatOperatorSnapshotCaptureService.Capture(
            signal,
            FatValueSlot.Value1,
            Observation("100.03 A", 2));

        Assert.NotEqual(first.EvidenceId, replacement.EvidenceId);
        Assert.Same(replacement, signal.Value1Evidence);
        Assert.Null(signal.Value2Evidence);
        Assert.True(signal.IsIncludedInFat);
    }

    [Fact]
    public void RemovedSignal_CannotCaptureUntilExplicitlyRestored()
    {
        var signal = AnalogSignal();
        signal.RemoveFromFat();

        Assert.Throws<InvalidOperationException>(() =>
            FatOperatorSnapshotCaptureService.Capture(
                signal,
                FatValueSlot.Value1,
                Observation("99.72 A", 1)));

        signal.RestoreToFat();
        var captured = FatOperatorSnapshotCaptureService.Capture(
            signal,
            FatValueSlot.Value1,
            Observation("99.72 A", 2));
        Assert.Same(captured, signal.Value1Evidence);
    }

    [Fact]
    public void AutomaticDigitalSignal_CannotBeManuallySnapshotted()
    {
        var signal = AnalogSignal() withCaptureModeNotSupported();

        Assert.Throws<InvalidOperationException>(() =>
            FatOperatorSnapshotCaptureService.Capture(
                signal,
                FatValueSlot.Value1,
                Observation("true", 1)));
    }

    private static FatVerificationSignal withCaptureModeNotSupported()
        => new()
        {
            SignalId = "fat-digital",
            IedName = "IED1",
            DataSetReference = "IED1LD0/LLN0$DS",
            StaticMemberReference = "IED1ADD/GGIO1.Dig01",
            RuntimeReference = "IED1ADD/GGIO1.Dig01.stVal",
            SignalName = "Dig01",
            FunctionalConstraint = "ST",
            DataType = "BOOLEAN",
            SignalKind = FatSignalKind.Discrete,
            CaptureMode = FatCaptureMode.AutomaticTransition
        };

    private static FatVerificationSignal AnalogSignal()
        => new()
        {
            SignalId = "fat-analog",
            IedName = "IED1",
            DataSetReference = "IED1LD0/LLN0$DS",
            StaticMemberReference = "IED1MEAS/MMXU1.A.phsA",
            RuntimeReference = "IED1MEAS/MMXU1.A.phsA.cVal.mag.f",
            SignalName = "A.phsA",
            FunctionalConstraint = "MX",
            DataType = "FLOAT32",
            SignalKind = FatSignalKind.Analog,
            CaptureMode = FatCaptureMode.OperatorSnapshot
        };

    private static FatLiveValueObservation Observation(string value, long sequence)
        => new(
            value,
            new DateTimeOffset(2026, 8, 31, 10, 0, 0, TimeSpan.Zero).AddSeconds(sequence),
            new DateTimeOffset(2026, 8, 31, 9, 59, 59, TimeSpan.Zero).AddSeconds(sequence),
            "good",
            "MMS",
            sequence,
            1);
}
