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
    public void NativeMmsRead_PublishesToCanonicalBeforeUi_WithReportPrecedenceAndEpochFence()
    {
        var store = new IedBrowserLiveValues();
        var epoch = store.BeginAssociation();
        const string exact = "SG7_BCUMeter/ACsrcMMXU1.A.phsA.cVal.mag.f";
        var read = new ResolvedIecSignalRead(new Iec61850ReadValue
        {
            Value = 125.25d, Quality = "Good",
            DeviceTimestamp = "2026-10-23 12:00 UTC"
        }, exact);
        Assert.True(Iec61850MonitorRuntime.PublishVerifiedMmsReadToCanonical(
            store, epoch, read, "MX", "FLOAT32", "Good", "12:00"));
        Assert.True(store.TryGet(exact, "MX", out var initial));
        Assert.Equal("125.25", initial.Value);
        Assert.Equal("MMS Read", initial.Source);
        Assert.Equal(epoch, initial.Association);
        Assert.False(store.TryGet(exact.Replace(".cVal.", ".instCVal."), "MX", out _));

        Assert.True(store.PublishForAssociation(epoch, exact, "MX",
            "130.5", "questionable", "12:01", "Report"));
        Assert.True(Iec61850MonitorRuntime.PublishVerifiedMmsReadToCanonical(
            store, epoch, read, "MX", "FLOAT32", "Good", "12:02"));
        Assert.True(store.TryGet(exact, "MX", out var reported));
        Assert.Equal("130.5", reported.Value);
        Assert.Equal("Report", reported.Source);
        Assert.Equal("questionable", reported.Quality);

        store.BeginAssociation();
        Assert.False(Iec61850MonitorRuntime.PublishVerifiedMmsReadToCanonical(
            store, epoch, read, "MX", "FLOAT32", "Good", "12:03"));
        Assert.False(store.TryGet(exact, "MX", out _));
    }

    [Fact]
    public void NativeMmsRead_DoesNotPublishStructuresOrControlParameters()
    {
        var store = new IedBrowserLiveValues();
        var epoch = store.BeginAssociation();
        var structure = new ResolvedIecSignalRead(
            new Iec61850ReadValue { Value = "Structure(3)" },
            "SG7_BCUSystem/BkrCSWI1.Pos.stVal");
        Assert.False(Iec61850MonitorRuntime.PublishVerifiedMmsReadToCanonical(
            store, epoch, structure, "ST", "Dbpos", "", ""));
        Assert.False(Iec61850MonitorRuntime.PublishVerifiedMmsReadToCanonical(
            store, epoch, new ResolvedIecSignalRead(true,
                "SG7_BCUSystem/BkrCSWI1.Pos.Oper.ctlVal"),
            "CO", "BOOLEAN", "", ""));
        Assert.False(store.TryGet("SG7_BCUSystem/BkrCSWI1.Pos.stVal", "ST", out _));
    }

    [Fact]
    public void StaticInitialImage_UsesActualSameAssociationRead_NotReportOrMissing()
    {
        var store = new IedBrowserLiveValues();
        var epoch = store.BeginAssociation();
        const string valueRef = "SG7_BCUMeter/ACsrcMMXU1.A.phsA.cVal.mag.f";
        const string absent = "SG7_BCUMeter/ACsrcMMXU1.A.phsB.cVal.mag.f";
        Assert.False(Iec61850MonitorRuntime.TryGetStaticInitialRead(
            store, epoch, absent, "MX", out _));
        Assert.True(store.PublishForAssociation(epoch, valueRef, "MX",
            "123.5", "Good", "12:00", "MMS Read"));
        Assert.True(Iec61850MonitorRuntime.TryGetStaticInitialRead(
            store, epoch, valueRef, "MX", out var evidence));
        Assert.Equal("MMS Read", evidence.Source);
        Assert.Equal("123.5", evidence.Value);
        Assert.False(Iec61850MonitorRuntime.TryGetStaticInitialRead(
            store, epoch, valueRef, "CO", out _));
        Assert.True(store.PublishForAssociation(epoch, valueRef, "MX",
            "124", "Good", "12:01", "Report"));
        Assert.False(Iec61850MonitorRuntime.TryGetStaticInitialRead(
            store, epoch, valueRef, "MX", out _));
        store.BeginAssociation();
        Assert.False(Iec61850MonitorRuntime.TryGetStaticInitialRead(
            store, epoch, valueRef, "MX", out _));
    }

    [Fact]
    public void StaticMmsSnapshot_DpcPresentationUsesOperatorDbposWithoutChangingRawEvidence()
    {
        var point = new Iec61850MonitorPoint
        {
            IecReference = "SG7_BCUSystem/BkrCSWI1.Pos.stVal",
            FunctionalConstraint = "ST",
            IecDataType = "Dbpos", Category = "Position"
        };
        var store = new IedBrowserLiveValues();
        var epoch = store.BeginAssociation();
        Assert.True(store.PublishForAssociation(epoch, point.IecReference,
            "ST", "2", "Good", "12:01", "MMS Read"));
        Assert.True(Iec61850MonitorRuntime.TryGetStaticInitialRead(
            store, epoch, point.IecReference, "ST", out var raw));
        Assert.Equal("2", raw.Value);
        Assert.Equal("Close [10]",
            Iec61850MonitorRuntime.FormatStaticSnapshotRead(point, raw));
        Assert.Equal("2", raw.Value);
    }

    [Fact]
    public void StaticReportReadJournal_IsBounded_ReadOnlyAndNeverSynthesizesSoe()
    {
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");
        Assert.Contains("ConsumeStaticReadJournal(session);", runtime);
        Assert.Contains("ledger.ReadChanges(session.StaticReadJournalCursor, 32)", runtime);
        Assert.Contains("change.Kind != CanonicalValueChangeKind.Read", runtime);
        Assert.Contains("change.Association != association", runtime);
        Assert.Contains("state.ReportTrafficSeen", runtime);
        Assert.Contains("TryGetStaticInitialRead(session.Device.LiveValues, association", runtime);
        Assert.Contains("!string.Equals(sourceMode, \"MMS Snapshot Read\"", runtime);
        Assert.DoesNotContain("PublishReport(point", runtime);
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
