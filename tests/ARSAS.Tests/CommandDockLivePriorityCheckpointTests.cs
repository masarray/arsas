using System.Collections.Specialized;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class CommandDockLivePriorityCheckpointTests
{
    private static SignalDefinition Control(
        string reference, string cdc, string model, bool verified = false)
    {
        var slash = reference.LastIndexOf('/');
        var dot = reference.LastIndexOf('.');
        var signal = new SignalDefinition
        {
            Name = reference[(dot + 1)..],
            LogicalNode = reference[(slash + 1)..dot],
            ObjectReference = reference,
            FunctionalConstraint = "CO",
            IsControlSignal = true,
            IsSelected = true,
            ControlCdc = cdc,
            ControlModelText = model
        };
        signal.ControlSemanticVerified = verified;
        return signal;
    }

    private const string Direct = "Direct Operate (DO) • Normal security";

    [Fact]
    public void TapFirst_StatusOnlyLast_WithoutGrantingAnyNewCommandPermission()
    {
        var device = new Iec61850MonitorDevice();
        var status = Control("ZIVAVRLD1/CSWI2.Pos", "DPC", "Status only");
        var unknown = Control("ZIVAVRLD1/CSWI4.Pos", "DPC", "Auto-detect");
        var cswi = Control("ZIVAVRLD1/CSWI1.Pos", "DPC", Direct, verified: true);
        var xcb = Control("SG7_BCUSystem/Bkr0XCBR1.Pos", "DPC", Direct, verified: true);
        var tap = Control("ZIVAVRLD1/ATCC1.TapChg", "BSC", Direct, verified: true);
        foreach (var signal in new[] { status, unknown, xcb, cswi, tap })
            device.Signals.Add(signal);
        device.RecountSelectedSignals();

        Assert.Equal(5, device.CommandSignals.Count);
        Assert.Same(tap, device.CommandSignals[0]);
        Assert.True(device.CommandSignals.IndexOf(cswi) < device.CommandSignals.IndexOf(xcb));
        Assert.True(device.CommandSignals.IndexOf(xcb) < device.CommandSignals.IndexOf(unknown));
        Assert.Same(status, device.CommandSignals[^1]);
        Assert.False(status.ControlSupportsOperate);
        Assert.False(unknown.ControlSupportsOperate);
        Assert.Equal(Iec61850ControlModelKind.StatusOnly, status.ControlModelKind);
        Assert.Equal(5, device.SelectedControlSignalCount);
        Assert.Equal(3, device.OperableCommandSignals.Count);
        Assert.Same(tap, device.OperableCommandSignals[0]);
    }

    [Fact]
    public void StatusOnlyTapDoesNotJumpAboveLiveCswi_WhenModelChangesReorderInPlace()
    {
        var device = new Iec61850MonitorDevice();
        var cswi = Control("IED/CSWI1.Pos", "DPC", Direct, verified: true);
        var tap = Control("IED/ATCC1.TapChg", "BSC", "Auto-detect");
        device.Signals.Add(cswi);
        device.Signals.Add(tap);
        device.RecountSelectedSignals();

        Assert.Same(cswi, device.CommandSignals[0]);
        var resets = 0;
        device.CommandSignals.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset)
                resets++;
        };

        tap.ControlModelText = Direct;
        tap.ControlSemanticVerified = true;
        device.RefreshCommandSignalProjection();
        Assert.Same(tap, device.CommandSignals[0]);
        Assert.Same(cswi, device.CommandSignals[1]);

        tap.ControlModelText = "Status only";
        device.RefreshCommandSignalProjection();
        Assert.Same(cswi, device.CommandSignals[0]);
        Assert.Same(tap, device.CommandSignals[1]);
        Assert.False(tap.ControlSupportsOperate);
        Assert.Equal(0, resets);
    }

    [Fact]
    public void IdenticalReportRefreshDoesNotRecreateRowsOrChangeOrder()
    {
        var device = new Iec61850MonitorDevice();
        var tap = Control("IED/ATCC1.TapChg", "BSC", Direct, verified: true);
        var cswi = Control("IED/CSWI1.Pos", "DPC", Direct, verified: true);
        var readOnly = Control("IED/CSWI2.Pos", "DPC", "Status only");
        foreach (var signal in new[] { readOnly, cswi, tap })
            device.Signals.Add(signal);
        device.RecountSelectedSignals();
        var snapshots = device.CommandSignals.ToArray();
        var collectionChanges = 0;
        device.CommandSignals.CollectionChanged += (_, _) => collectionChanges++;
        for (var i = 0; i < 100; i++)
            device.RefreshCommandSignalProjection();
        Assert.True(device.CommandSignals.SequenceEqual(
            snapshots, ReferenceEqualityComparer.Instance));
        Assert.Equal(0, collectionChanges);
    }

    [Fact]
    public void ExistingSemanticRolesStayUnmodified_OnlyPresentationOrderChanges()
    {
        var tap = Control("IED/ATCC1.TapChg", "BSC", Direct, verified: true);
        var cswi = Control("IED/CSWI1.Pos", "DPC", Direct, verified: true);
        var status = Control("IED/CSWI2.Pos", "DPC", "Status only");
        Assert.True(Iec61850ControlIdentity.CommandDockPriority(tap) >
                    Iec61850ControlIdentity.CommandDockPriority(cswi));
        Assert.True(Iec61850ControlIdentity.CommandDockDisplayPriority(tap) <
                    Iec61850ControlIdentity.CommandDockDisplayPriority(cswi));
        Assert.True(Iec61850ControlIdentity.CommandDockDisplayPriority(status) >
                    Iec61850ControlIdentity.CommandDockDisplayPriority(cswi));
        Assert.Contains("Tap changer", tap.ControlDockRole);
        Assert.Equal(0, Iec61850ControlIdentity.CountSelected(
            new[] { Control("IED/CSWI3.Pos", "DPC", Direct, verified: true)
                }.Where(s => !s.IsSelected)));
    }
}
