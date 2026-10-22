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
    public void RepeatSemanticAndReportingEvidenceDoesNotFlickerVerifiedCommandAuthority()
    {
        var signal = Verified("LD0/Bkr0XCBR2.Pos");
        var notifications = new List<string>();
        signal.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
                notifications.Add(args.PropertyName);
        };
        signal.ControlInterlockCheck = false; // operator-owned choice
        notifications.Clear();

        for (var repeat = 0; repeat < 100; repeat++)
        {
            signal.AcceptVerifiedLiveControlCdc("DPC");
            signal.ControlSemanticVerified = true;
            signal.ApplyDataSetAdvisoryControlMetadata("DPC",
                "LD0/Bkr0XCBR2.Pos.stVal");
        }
        Assert.True(signal.ControlSupportsOperate);
        Assert.Equal("DPC", signal.VerifiedLiveControlCdc);
        Assert.False(signal.ControlInterlockCheck);
        Assert.DoesNotContain(nameof(SignalDefinition.VerifiedLiveControlCdc),
            notifications);
        Assert.Equal(1, notifications.Count(name =>
            name == nameof(SignalDefinition.DataSetAdvisoryCdc)));

        notifications.Clear();
        signal.ClearLiveControlAuthority();
        Assert.False(signal.ControlSupportsOperate);
        Assert.Contains(nameof(SignalDefinition.VerifiedLiveControlCdc),
            notifications);
        notifications.Clear();
        signal.ClearLiveControlAuthority(); // duplicate teardown is silent
        Assert.Empty(notifications);
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

        Assert.Equal(2, device.CommandSignals.Count);
        Assert.Same(first, device.CommandSignals[0]);
        Assert.Same(second, device.CommandSignals[1]);
        Assert.False(first.ControlSupportsOperate);
        Assert.Empty(events);

        // Fresh live-typed authority is mandatory before the control can return.
        first.AcceptVerifiedLiveControlCdc("DPC");
        first.ControlSemanticVerified = true;
        device.RefreshCommandSignalProjection();
        Assert.Equal(2, device.CommandSignals.Count);
        Assert.True(first.ControlSupportsOperate);
        Assert.Empty(events);
    }

    [Fact]
    public void StatusOnlyAndUnknownObjectsRemainVisibleButCannotOperate()
    {
        var valid = Verified("LD0/CSWI1.Pos");
        var readonlyControl = Verified("LD0/CSWI2.Pos");
        readonlyControl.ControlModelText = "Status only";
        var unknownControl = Verified("LD0/CSWI3.Pos");
        unknownControl.ControlModelText = "Auto-detect";

        var device = new Iec61850MonitorDevice();
        device.Signals.ReplaceAll([valid, readonlyControl, unknownControl]);
        device.RecountSelectedSignals();

        Assert.Equal(3, device.CommandSignals.Count);
        Assert.Contains(valid, device.CommandSignals);
        Assert.Contains(readonlyControl, device.CommandSignals);
        Assert.Contains(unknownControl, device.CommandSignals);
        Assert.Single(Iec61850ControlIdentity.DistinctOperable(device.Signals));
        Assert.False(readonlyControl.ControlSupportsOperate);
        Assert.False(unknownControl.ControlSupportsOperate);
    }
}
