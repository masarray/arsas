using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P109LControlDockDiscoveryRecoveryTests
{
    [Fact]
    public void SelectedDatasetControlObjectsRemainVisibleWhileInspectionIsPending()
    {
        var device = new Iec61850MonitorDevice { Name = "ZIVAVR" };
        device.Signals.Add(Control("ZIVAVRLD1/ATCC1.TapChg", "BSC"));
        for (var index = 1; index <= 4; index++)
            device.Signals.Add(Control($"ZIVAVRLD1/CSWI{index}.Pos", "DPC"));

        device.RecountSelectedSignals();

        Assert.Equal(5, device.SelectedControlSignalCount);
        Assert.Equal(5, device.CommandSignals.Count);
        Assert.All(device.CommandSignals, signal =>
        {
            Assert.True(signal.IsValidControlObject);
            Assert.False(signal.ControlSupportsOperate);
            Assert.False(signal.CanPublishAsSignal);
        });
        Assert.Empty(Iec61850ControlIdentity.DistinctOperable(device.Signals));
    }

    [Fact]
    public void StatusOnlyAndUnverifiedObjectsRemainInspectable_NotOperable()
    {
        var device = new Iec61850MonitorDevice();
        var statusOnly = Control("ZIVAVRLD1/ATCC1.TapChg", "BSC");
        statusOnly.ControlModelText = "StatusOnly";
        var pending = Control("ZIVAVRLD1/CSWI1.Pos", "DPC");
        device.Signals.Add(statusOnly);
        device.Signals.Add(pending);

        device.RecountSelectedSignals();

        Assert.Equal(2, device.CommandSignals.Count);
        Assert.True(statusOnly.ControlModelResolved);
        Assert.True(statusOnly.IsReadOnlyControl);
        Assert.False(statusOnly.ControlSupportsOperate);
        Assert.False(pending.ControlSupportsOperate);
        Assert.Empty(Iec61850ControlIdentity.DistinctOperable(device.Signals));
    }

    [Fact]
    public void StandardBscStepFeedbackRemainsNumeric_NotBoolean()
    {
        Assert.Equal("Int8", Iec61850StaticControlStatusProjectionService.ResolveDeclaredFeedbackDataType(
            "BSC", "boolean", "BOOLEAN"));
        Assert.Equal("Int8", Iec61850StaticControlStatusProjectionService.ResolveDeclaredFeedbackDataType(
            "BSC", "integer", "INT8"));
        Assert.Equal("Dbpos", Iec61850StaticControlStatusProjectionService.ResolveDeclaredFeedbackDataType(
            "DPC", "bit-string", "Dbpos"));
    }

    [Fact]
    public void CommandDockDeduplicatesExactTargetsWithoutAdmittingStatusLeaves()
    {
        var device = new Iec61850MonitorDevice();
        var first = Control("ZIVAVRLD1/CSWI1.Pos", "DPC");
        var duplicate = Control("ZIVAVRLD1/CSWI1.Pos", "DPC");
        var notSelected = Control("ZIVAVRLD1/CSWI2.Pos", "DPC");
        notSelected.IsSelected = false;
        device.Signals.Add(first);
        device.Signals.Add(duplicate);
        device.Signals.Add(notSelected);
        device.Signals.Add(new SignalDefinition
        {
            ObjectReference = "ZIVAVRLD1/CSWI1.Pos.stVal",
            FunctionalConstraint = "ST", IsSelected = true
        });

        device.RecountSelectedSignals();

        Assert.Single(device.CommandSignals);
        Assert.Equal("ZIVAVRLD1/CSWI1.Pos", device.CommandSignals[0].ObjectReference);
        Assert.False(device.CommandSignals[0].ControlSupportsOperate);
    }

    private static SignalDefinition Control(string reference, string cdc) => new()
    {
        Name = reference[(reference.LastIndexOf('.') + 1)..],
        ObjectReference = reference,
        DisplayReference = reference,
        FunctionalConstraint = "CO",
        ControlCdc = cdc,
        DataType = cdc,
        IsControlSignal = true,
        IsSelected = true
    };
}
