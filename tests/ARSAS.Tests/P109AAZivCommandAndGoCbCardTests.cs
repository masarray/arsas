using System.Collections.Specialized;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// Regression from ZIV AVR physical diagnostic 2026-10-20:
/// five selected CO roots with live ctlModel=0 must remain visible for
/// explicit model commissioning, while ALL wire commands remain prohibited.
/// </summary>
public sealed class P109AAZivCommandAndGoCbCardTests
{
    private static SignalDefinition Row(string fullReference, string cdc, string model = "Status only")
        => new()
        {
            Name = fullReference.Split('.').Last(),
            ObjectReference = fullReference,
            FunctionalConstraint = "CO",
            IsControlSignal = true,
            IsSelected = true,
            ControlCdc = cdc,
            ControlModelText = model
        };

    [Fact]
    public void FiveZivStatusOnlyRootsStayStableAndCannotSendWireCommands()
    {
        var device = new Iec61850MonitorDevice { Name = "ZIVAVR" };
        var refs = new[] {
            "ZIVAVRLD1/ATCC1.TapChg",
            "ZIVAVRLD1/CSWI1.Pos",
            "ZIVAVRLD1/CSWI2.Pos",
            "ZIVAVRLD1/CSWI3.Pos",
            "ZIVAVRLD1/CSWI4.Pos" };
        var controls = refs.Select((id,i)=>Row(id,i==0?"BSC":"DPC")).ToArray();
        device.Signals.ReplaceAll(controls);
        device.RecountSelectedSignals();
        Assert.Equal(5, device.SelectedControlSignalCount);
        Assert.Equal(5, device.CommandSignals.Count);
        Assert.Empty(Iec61850ControlIdentity.DistinctOperable(device.Signals));
        var changes = new List<NotifyCollectionChangedAction>();
        device.CommandSignals.CollectionChanged += (_,e)=>changes.Add(e.Action);
        for (var index=0;index<25;index++) device.RefreshCommandSignalProjection();
        Assert.Empty(changes);
        foreach(var control in controls)
        {
            Assert.Contains(control,device.CommandSignals);
            Assert.Equal(Iec61850ControlModelKind.StatusOnly,control.ControlModelKind);
            Assert.False(control.ControlSupportsOperate);
            Assert.False(control.TryStageControlConfirmation("Open [01]","Open",out _));
            Assert.False(control.TryBeginDirectControlCommand("Open [01]","Open",out _,out _));
        }
        // A detached SCL/ctlModel label alone must not grant authorization.
        controls[1].ControlModelText = "Select Before Operate (SBO) • Enhanced security";
        device.RefreshCommandSignalProjection();
        Assert.Equal(5,device.CommandSignals.Count);
        Assert.False(controls[1].ControlSupportsOperate);
        Assert.Empty(Iec61850ControlIdentity.DistinctOperable(device.Signals));
    }

    [Fact]
    public void GoCbAvailabilityIsPerIedAndLiveDiscoveryOverridesStaleScl()
    {
        var noGoose = new Iec61850MonitorDevice { Name = "NO_GOCB" };
        var withGoose = new Iec61850MonitorDevice { Name = "ZIVAVR" };
        noGoose.IsConnected = true;
        withGoose.IsConnected = true;
        var changes = new List<string?>();
        withGoose.PropertyChanged += (_,e)=>changes.Add(e.PropertyName);
        noGoose.LiveDiscoveryModel = new LiveIedModelDiscoveryDocument();
        withGoose.LiveDiscoveryModel = new LiveIedModelDiscoveryDocument
        {
            GooseControlBlocks = [new LiveIedControlBlockModel
            {
                Name = "gcb1", Kind = "GOOSE", Domain = "LD1",
                LogicalNode = "LLN0", Reference = "LD1/LLN0.GO.gcb1"
            }]
        };
        Assert.False(noGoose.HasGooseControlBlock);
        Assert.True(withGoose.HasGooseControlBlock);
        Assert.Contains(nameof(Iec61850MonitorDevice.HasGooseControlBlock),changes);
        withGoose.LiveDiscoveryModel = new LiveIedModelDiscoveryDocument();
        Assert.False(withGoose.HasGooseControlBlock);
        Assert.False(noGoose.HasGooseControlBlock);
        // Do not reuse an old association's GoCB count after teardown.
        withGoose.IsConnected = false;
        Assert.False(withGoose.HasGooseControlBlock);
    }

    [Fact]
    public void ModelRemainsClickableAndBothGooseEntryPointsRequireGoCb()
    {
        string Read(string name)
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while(folder != null)
            {
                var target = Path.Combine(folder.FullName,name);
                if(File.Exists(target)) return File.ReadAllText(target);
                folder = folder.Parent;
            }
            throw new FileNotFoundException(name);
        }
        var xaml = Read("MainWindow.xaml");
        var ux = Read("MainWindow.CommandPanelUx.cs");
        var cap = Read("FaultRecordUxBehavior.cs");
        var legacy = Read("MainWindow.IedGooseQuickStart.cs");
        var models = Read("Models/MonitorModels.cs");
        Assert.Contains("DistinctSelected(Signals)",models,StringComparison.Ordinal);
        Assert.DoesNotContain("DistinctOperable(Signals)",models,StringComparison.Ordinal);
        Assert.Contains("if (content is \"Open\" or \"Close\")",ux,StringComparison.Ordinal);
        Assert.Contains("Content=\"ctlModel\"",xaml,StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource CommandDockCtlModelButton}\"",xaml,StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding ControlSupportsOperate}\"",xaml,StringComparison.Ordinal);
        Assert.Contains("hasGoose ? CapabilityState.Available : CapabilityState.Unavailable",cap,StringComparison.Ordinal);
        Assert.Contains("var hasGoose = device.HasGooseControlBlock;",cap,StringComparison.Ordinal);
        Assert.Contains("if (!device.HasGooseControlBlock)",cap,StringComparison.Ordinal);
        Assert.Contains("nameof(Iec61850MonitorDevice.HasGooseControlBlock)",legacy,StringComparison.Ordinal);
        Assert.Contains("device.HasGooseControlBlock)",legacy,StringComparison.Ordinal);
    }
}
