using System.Collections.Specialized;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// WPF Command Dock must preserve row containers and edited checkboxes when
/// monitoring, read-only inspection or workspace navigation republishes the
/// same verified control catalog.
/// </summary>
public sealed class P109UStableCommandProjectionTests
{
    private static SignalDefinition Verified(string reference) 
    {
        var signal = new SignalDefinition
        {
            Name = reference[(reference.LastIndexOf('.') + 1)..],
            ObjectReference = reference,
            FunctionalConstraint = "CO",
            IsSelected = true,
            IsControlSignal = true,
            ControlCdc = "DPC",
            ControlModelText = "Select Before Operate (SBO) • Enhanced security"
        };
        signal.AcceptVerifiedLiveControlCdc("DPC");
        signal.ControlSemanticVerified = true;
        return signal;
    }

    [Fact]
    public void IdenticalVerifiedCatalog_NeverEmitsResetOrRecreatesRows()
    {
        var first = Verified("LD0/CSWI1.Pos");
        var second = Verified("LD0/CSWI2.Pos");
        var device = new Iec61850MonitorDevice();
        device.Signals.ReplaceAll([first, second]);
        device.RecountSelectedSignals();

        Assert.Equal(2, device.CommandSignals.Count);
        var events = new List<NotifyCollectionChangedAction>();
        device.CommandSignals.CollectionChanged += (_, e) => events.Add(e.Action);
        var initial = device.CommandSignals.ToArray();

        first.ControlCurrentValue = "Open";
        first.ControlLastResult = "Feedback confirmed";
        first.ControlInterlockCheck = false; // explicit operator choice
        for (var iteration = 0; iteration < 25; iteration++)
            device.RefreshCommandSignalProjection();

        Assert.Empty(events);
        Assert.True(initial.SequenceEqual(device.CommandSignals));
        Assert.False(first.ControlInterlockCheck);
    }

    [Fact]
    public void GenuineAuthorityRevocation_DisablesOnlyAffectedRowWithoutResettingGrid()
    {
        var first = Verified("LD0/CSWI1.Pos");
        var second = Verified("LD0/CSWI2.Pos");
        var device = new Iec61850MonitorDevice();
        device.Signals.ReplaceAll([first, second]);
        device.RecountSelectedSignals();
        Assert.Equal(2, device.CommandSignals.Count);

        var events = new List<NotifyCollectionChangedAction>();
        device.CommandSignals.CollectionChanged += (_, e) => events.Add(e.Action);
        first.ClearLiveControlAuthority();
        device.RefreshCommandSignalProjection();

        Assert.Single(device.CommandSignals);
        Assert.Same(second, device.CommandSignals[0]);
        Assert.False(first.ControlSupportsOperate);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Remove }, events);

        // Fresh live-typed authority is mandatory before the control can return.
        first.AcceptVerifiedLiveControlCdc("DPC");
        first.ControlSemanticVerified = true;
        events.Clear();
        device.RefreshCommandSignalProjection();
        Assert.Equal(2, device.CommandSignals.Count);
        Assert.True(first.ControlSupportsOperate);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Add }, events);
    }

    [Fact]
    public void StatusOnlyAndUnknownObjectsRemainInInventoryNotInCommandDock()
    {
        var valid = Verified("LD0/CSWI1.Pos");
        var readonlyControl = Verified("LD0/CSWI2.Pos");
        readonlyControl.ControlModelText = "Status only";
        var unknownControl = Verified("LD0/CSWI3.Pos");
        unknownControl.ControlModelText = "Auto-detect";

        var device = new Iec61850MonitorDevice();
        device.Signals.ReplaceAll([valid, readonlyControl, unknownControl]);
        device.RecountSelectedSignals();

        Assert.Single(device.CommandSignals);
        Assert.Contains(valid, device.CommandSignals);
        Assert.DoesNotContain(readonlyControl, device.CommandSignals);
        Assert.DoesNotContain(unknownControl, device.CommandSignals);
        Assert.Single(Iec61850ControlIdentity.DistinctOperable(device.Signals));
        Assert.False(readonlyControl.ControlSupportsOperate);
        Assert.False(unknownControl.ControlSupportsOperate);
    }
}
