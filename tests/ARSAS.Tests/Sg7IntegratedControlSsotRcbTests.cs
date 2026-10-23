using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// SG7_BCU one-release-gate P0 contracts: discovered controls are VISIBLE,
/// but exact typed live evidence remains required for physical authorization.
/// No fake MMS ingress, SOE, or Operate is generated.
/// </summary>
public sealed class Sg7IntegratedControlSsotRcbTests
{
    private static SignalDefinition Candidate(int n) => new()
    {
        ObjectReference = $"SG7_BCUSystem/BkrCSWI{n}.Pos",
        Name = $"BkrCSWI{n}.Pos",
        FunctionalConstraint = "CO",
        IsControlSignal = true,
        HasControlServiceEvidence = true,
        ControlCdc = "DPC",
        ControlModelText = "SBO • Enhanced security"
    };

    [Fact]
    public void FourUnverifiedCswi_AppearFirstInDock_ButNeverInOperable()
    {
        var cswi = Enumerable.Range(1, 4).Select(Candidate).ToArray();
        var ggio = new SignalDefinition
        {
            ObjectReference = "SG7_BCUMaster/GGIO2.SPCSO001",
            Name = "SPCSO001",
            FunctionalConstraint = "CO", IsControlSignal = true,
            HasControlServiceEvidence = true,
            ControlCdc = "SPC", IsSelected = true,
            ControlModelText = "SBO • Enhanced security"
        };
        ggio.AcceptVerifiedLiveControlCdc("SPC");
        ggio.ControlSemanticVerified = true;
        var device = new Iec61850MonitorDevice
        {
            IsConnected = true,
            LiveDiscoveryModel = new LiveIedModelDiscoveryDocument()
        };
        device.Signals.ReplaceAll([ggio, .. cswi]);
        device.RecountSelectedSignals();

        Assert.Equal(cswi, device.CommandSignals.Take(4));
        Assert.Same(ggio, device.CommandSignals[4]);
        Assert.All(cswi, s =>
        {
            Assert.False(s.ControlSupportsOperate);
            Assert.DoesNotContain(s, device.OperableCommandSignals);
        });
        Assert.Contains(ggio, device.OperableCommandSignals);
        Assert.Empty(Iec61850ControlIdentity.VerifiedIndependentCswiControls(cswi));
    }

    [Fact]
    public void NameOnlyCswiWithoutCoProof_IsNotDiscoveredControl()
    {
        var nameOnly = Candidate(5);
        nameOnly.HasControlServiceEvidence = false;
        Assert.Empty(Iec61850ControlIdentity.DiscoveredIndependentCswiControls([nameOnly]));
        Assert.Equal(0, Iec61850ControlIdentity.CommandDockPriority(Candidate(1)));
    }

    [Fact]
    public void CanonicalReportBridge_IsAssociationFenced_AndCannotReplaceTypedReport()
    {
        var store = new IedBrowserLiveValues();
        var old = store.BeginAssociation();
        const string reference = "SG7_BCUMaster/GGIO1.Ind002.stVal";
        Assert.True(store.MirrorRoutedReportIfMissingForAssociation(
            old, reference, "ST", "true", "", ""));
        Assert.True(store.TryGet(reference, "ST", out var initial));
        Assert.Equal("true", initial.Value);
        Assert.True(store.PublishForAssociation(old, reference, "ST",
            "false", "questionable", "2026-10-23 12:00 UTC", "Report"));
        Assert.False(store.MirrorRoutedReportIfMissingForAssociation(
            old, reference, "ST", "true", "Good", "2020-01-01"));
        Assert.True(store.TryGet(reference, "ST", out var typed));
        Assert.Equal("false", typed.Value);
        Assert.Equal("questionable", typed.Quality);
        store.BeginAssociation();
        Assert.False(store.MirrorRoutedReportIfMissingForAssociation(
            old, reference, "ST", "true", "", ""));
        Assert.False(store.TryGet(reference, "ST", out _));
    }

    [Fact]
    public void IntegrationContract_OneIngressWriter_NoWpfPublishOrBypassedOperate()
    {
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");
        var window = Read("MainWindow.xaml.cs");
        var xaml = Read("MainWindow.xaml");
        var control = Read("Models/MonitorModels.cs");
        Assert.Contains("MirrorRoutedReportIfMissingForAssociation(", runtime);
        Assert.DoesNotContain("observedOwner.LiveValues.PublishReport(point", window);
        Assert.Contains("SelectedDevice.CommandSignals", xaml);
        Assert.Contains("IsEnabled=\"{Binding ControlSupportsOperate}\"", xaml);
        Assert.Contains("DiscoveredIndependentCswiControls(Signals)", control);
        Assert.Contains("RCB setup accepted:", runtime);
        Assert.Contains("awaiting first routed InformationReport", runtime);
        Assert.DoesNotContain("ExecuteControlAsync(", Read("Services/Iec61850ControlIdentity.cs"));
    }

    private static string Read(string path)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory);
             dir is not null; dir = dir.Parent)
        {
            var full = Path.Combine(dir.FullName, path);
            if (File.Exists(full)) return File.ReadAllText(full);
        }
        throw new FileNotFoundException(path);
    }
}
