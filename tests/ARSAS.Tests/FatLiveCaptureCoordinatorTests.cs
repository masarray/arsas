using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class FatLiveCaptureCoordinatorTests
{
    [Fact]
    public void DiscreteEdge_CapturesGenericValuePairWithoutOnOffAssumption()
    {
        var signal = Signal("digital-a", FatSignalKind.Discrete, FatCaptureMode.AutomaticTransition);
        var project = Project(signal);
        var coordinator = new FatLiveCaptureCoordinator(project);

        coordinator.Observe(
            signal.RuntimeReference,
            new[] { "IED1" },
            "open (01)",
            isValueEdge: true,
            Observation("closed (10)", sequence: 9));

        Assert.Equal("open (01)", signal.Value1Evidence?.RawValue);
        Assert.Equal("closed (10)", signal.Value2Evidence?.RawValue);
        Assert.Equal(FatEvidenceCaptureKind.AutomaticTransition, signal.Value1Evidence?.CaptureKind);
        Assert.Equal(FatEvidenceCaptureKind.AutomaticTransition, signal.Value2Evidence?.CaptureKind);
        Assert.Equal(2, coordinator.History.Count);
        Assert.Equal(FatValueSlot.Value1, coordinator.History[0].Evidence.Slot);
        Assert.Equal(FatValueSlot.Value2, coordinator.History[1].Evidence.Slot);
    }

    [Fact]
    public void DiscreteRecapture_AppendsHistoryBeforeReplacingCurrentPair()
    {
        var signal = Signal("digital-a", FatSignalKind.Discrete, FatCaptureMode.AutomaticTransition);
        var coordinator = new FatLiveCaptureCoordinator(Project(signal));

        coordinator.Observe(signal.RuntimeReference, new[] { "IED1" }, "0", true, Observation("1", 1));
        var firstV1 = signal.Value1Evidence!.EvidenceId;
        var firstV2 = signal.Value2Evidence!.EvidenceId;

        coordinator.Observe(signal.RuntimeReference, new[] { "IED1" }, "1", true, Observation("0", 2));

        Assert.Equal(4, coordinator.History.Count);
        Assert.NotEqual(firstV1, signal.Value1Evidence!.EvidenceId);
        Assert.NotEqual(firstV2, signal.Value2Evidence!.EvidenceId);
        Assert.Equal("1", signal.Value1Evidence.RawValue);
        Assert.Equal("0", signal.Value2Evidence.RawValue);
        Assert.Equal(firstV1, coordinator.History[0].Evidence.EvidenceId);
        Assert.Equal(firstV2, coordinator.History[1].Evidence.EvidenceId);
    }

    [Fact]
    public void DuplicateStaticMemberships_ReceiveTheSameLiveEdgeAsDistinctEvidenceRows()
    {
        var first = Signal("membership-a", FatSignalKind.Discrete, FatCaptureMode.AutomaticTransition);
        var second = Signal("membership-b", FatSignalKind.Discrete, FatCaptureMode.AutomaticTransition);
        var coordinator = new FatLiveCaptureCoordinator(Project(first, second));

        var matched = coordinator.Observe(
            first.RuntimeReference,
            new[] { "IED1" },
            "false",
            true,
            Observation("true", 3));

        Assert.Equal(2, matched.Count);
        Assert.Equal("false", first.Value1Evidence?.RawValue);
        Assert.Equal("true", first.Value2Evidence?.RawValue);
        Assert.Equal("false", second.Value1Evidence?.RawValue);
        Assert.Equal("true", second.Value2Evidence?.RawValue);
        Assert.NotEqual(first.Value1Evidence?.EvidenceId, second.Value1Evidence?.EvidenceId);
        Assert.Equal(4, coordinator.History.Count);
    }

    [Fact]
    public void RemovedRow_ReceivesNoLiveImageOrEvidenceUntilRestored()
    {
        var signal = Signal("digital-a", FatSignalKind.Discrete, FatCaptureMode.AutomaticTransition);
        var project = Project(signal);
        var coordinator = new FatLiveCaptureCoordinator(project);
        project.RemoveSignal(signal.SignalId);

        coordinator.Observe(signal.RuntimeReference, new[] { "IED1" }, "0", true, Observation("1", 1));

        Assert.Null(signal.Value1Evidence);
        Assert.Null(signal.Value2Evidence);
        Assert.Null(coordinator.GetLatestObservation(signal.SignalId));
        Assert.Empty(coordinator.History);

        project.RestoreSignal(signal.SignalId);
        coordinator.Observe(signal.RuntimeReference, new[] { "IED1" }, "1", true, Observation("0", 2));
        Assert.NotNull(signal.Value1Evidence);
        Assert.NotNull(signal.Value2Evidence);
    }

    [Fact]
    public void AnalogOperatorCapture_UsesCurrentLiveReadingAndAppendsEveryRecapture()
    {
        var signal = Signal("analog-a", FatSignalKind.Analog, FatCaptureMode.OperatorSnapshot);
        var coordinator = new FatLiveCaptureCoordinator(Project(signal));

        coordinator.Observe(signal.RuntimeReference, new[] { "IED1" }, "12.3", false, Observation("12.3", 1));
        coordinator.CaptureOperatorSnapshot(signal.SignalId, FatValueSlot.Value1);
        var first = signal.Value1Evidence!.EvidenceId;

        coordinator.Observe(signal.RuntimeReference, new[] { "IED1" }, "15.8", false, Observation("15.8", 2));
        coordinator.CaptureOperatorSnapshot(signal.SignalId, FatValueSlot.Value2);
        coordinator.CaptureOperatorSnapshot(signal.SignalId, FatValueSlot.Value1);

        Assert.Equal("15.8", signal.Value1Evidence?.RawValue);
        Assert.Equal("15.8", signal.Value2Evidence?.RawValue);
        Assert.NotEqual(first, signal.Value1Evidence?.EvidenceId);
        Assert.Equal(3, coordinator.History.Count);
        Assert.Equal(first, coordinator.History[0].Evidence.EvidenceId);
    }

    [Fact]
    public void WrongIedAlias_DoesNotCrossBindEqualRuntimeReference()
    {
        var signal = Signal("analog-a", FatSignalKind.Analog, FatCaptureMode.OperatorSnapshot);
        var coordinator = new FatLiveCaptureCoordinator(Project(signal));

        var matched = coordinator.Observe(
            signal.RuntimeReference,
            new[] { "OTHER_IED" },
            "5",
            false,
            Observation("5", 1));

        Assert.Empty(matched);
        Assert.Null(coordinator.GetLatestObservation(signal.SignalId));
    }

    private static FatVerificationProject Project(params FatVerificationSignal[] signals)
        => new() { Signals = signals.ToList() };

    private static FatVerificationSignal Signal(
        string id,
        FatSignalKind kind,
        FatCaptureMode mode)
        => new()
        {
            SignalId = id,
            IedName = "IED1",
            AccessPointName = "S1",
            DataSetReference = "IED1LD0/LLN0$FAT",
            DataSetMemberIndex = id.GetHashCode(),
            StaticMemberReference = "IED1ADD/GGIO1.Dig01.stVal",
            RuntimeReference = "IED1ADD/GGIO1.Dig01.stVal",
            SignalName = id,
            FunctionalConstraint = kind == FatSignalKind.Analog ? "MX" : "ST",
            DataType = kind == FatSignalKind.Analog ? "FLOAT32" : "BOOLEAN",
            SignalKind = kind,
            CaptureMode = mode
        };

    private static FatLiveValueObservation Observation(string raw, long sequence)
        => new(
            raw,
            DateTimeOffset.Parse("2026-09-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-09-01T09:59:59Z"),
            "good",
            "Report",
            sequence,
            1);
}
