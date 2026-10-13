using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P109LControlRecoveryTests
{
    private static SignalDefinition Control(string reference, string cdc, bool selected = true)
        => new()
        {
            Name = reference[(reference.LastIndexOf('.') + 1)..],
            ObjectReference = reference,
            FunctionalConstraint = "CO",
            IsControlSignal = true,
            IsSelected = selected,
            ControlCdc = cdc,
            ControlModelText = "Auto-detect"
        };

    [Fact]
    public void FiveZivControlsRemainVisibleBeforeInspection()
    {
        var device = new Iec61850MonitorDevice();
        foreach (var (reference, cdc) in new[] {
            ("ZIVAVRLD1/ATCC1.TapChg", "BSC"),
            ("ZIVAVRLD1/CSWI1.Pos", "DPC"),
            ("ZIVAVRLD1/CSWI2.Pos", "DPC"),
            ("ZIVAVRLD1/CSWI3.Pos", "DPC"),
            ("ZIVAVRLD1/CSWI4.Pos", "DPC")
        })
            device.Signals.Add(Control(reference, cdc));
        device.RecountSelectedSignals();

        Assert.Equal(5, device.SelectedControlSignalCount);
        Assert.Equal(5, device.CommandSignals.Count);
        Assert.All(device.CommandSignals, signal =>
        {
            Assert.False(signal.ControlSupportsOperate);
            Assert.False(signal.ControlSemanticVerified);
        });
        Assert.True(device.CommandSignals.Single(s => s.ControlCdc == "BSC").IsRaiseLowerControl);
        Assert.All(device.CommandSignals.Where(s => s.ControlCdc == "DPC"),
            s => Assert.True(s.IsPositionControl));
    }

    [Fact]
    public void UninspectedAndStatusOnlyRemainInCatalogWithAllWritesBlocked()
    {
        var device = new Iec61850MonitorDevice();
        var pending = Control("LD0/CSWI1.Pos", "DPC");
        var readOnly = Control("LD0/ATCC1.TapChg", "BSC");
        readOnly.ControlModelText = "Status only";
        device.Signals.Add(pending);
        device.Signals.Add(readOnly);
        device.RecountSelectedSignals();

        Assert.Equal(2, device.CommandSignals.Count);
        Assert.All(device.CommandSignals, s => Assert.False(s.ControlSupportsOperate));
        Assert.Single(Iec61850ControlIdentity.InspectionRepresentatives(device.Signals));
    }

    [Fact]
    public void SelectedCatalogUsesExactDataObjectAndIgnoresUnselectedObjects()
    {
        var a = Control("LD0/CSWI1.Pos", "DPC");
        var duplicate = Control("ld0/cswi1.pos", "DPC");
        var unselected = Control("LD0/CSWI2.Pos", "DPC", selected: false);
        var catalog = Iec61850ControlIdentity.DistinctSelected(new[] { a, duplicate, unselected });
        Assert.Single(catalog);
        Assert.Same(a, catalog[0]);
    }

    [Fact]
    public void EngineeringCtlModelIsNotAuthorizationWithoutLiveSemanticEvidence()
    {
        var tap = Control("ZIVAVRLD1/ATCC1.TapChg", "BSC");
        tap.ControlModelText = "ctlModel=4";
        Assert.True(tap.ControlModelResolved);
        Assert.False(tap.ControlSupportsOperate);
    }
}
