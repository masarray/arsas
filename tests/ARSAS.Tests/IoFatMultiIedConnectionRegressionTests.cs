using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatMultiIedConnectionRegressionTests
{
    [Fact]
    public void BindIed_DoesNotOverwriteAnotherIedsProvenBinding()
    {
        var service = new IoTestLiveBindingService();
        var targetPoint = Point("P-A", "IED_A", "192.168.81.10", "IED_ALD0/GGIO1.Ind1.stVal");
        var target = Ied("IED_A", "192.168.81.10", targetPoint);
        var untouchedPoint = Point("P-B", "IED_B", "192.168.81.11", "IED_BLD0/GGIO1.Ind2.stVal");
        var untouched = Ied("IED_B", "192.168.81.11", untouchedPoint);

        untouched.ApplyLiveDeviceBinding("stable-device", "Monitoring · report", true, true);
        untouchedPoint.ApplyLiveBinding(
            IoTestLiveBindingState.LivePointReady,
            "Previously proven live binding",
            "stable-device",
            untouchedPoint.ObjectReference);

        var targetDevice = new Iec61850MonitorDevice
        {
            Name = "IED_A",
            SclIedName = "IED_A",
            IpAddress = "192.168.81.10",
            Port = 102
        };
        targetDevice.Signals.Add(new SignalDefinition
        {
            Name = "Ind1",
            ObjectReference = targetPoint.ObjectReference,
            FunctionalConstraint = "ST"
        });

        var result = service.BindIed(target, new[] { targetDevice });

        Assert.Equal(1, result.IedCount);
        Assert.Equal("stable-device", untouched.LiveDeviceId);
        Assert.True(untouched.IsLiveConnected);
        Assert.True(untouched.IsLiveMonitoring);
        Assert.Equal(IoTestLiveBindingState.LivePointReady, untouchedPoint.LiveBindingState);
        Assert.Equal("stable-device", untouchedPoint.LiveDeviceId);
    }

    [Fact]
    public void IedConnectionAction_IsOwnedByEachIed()
    {
        var a = Ied("IED_A", "192.168.81.10", Point("P-A", "IED_A", "192.168.81.10", "IED_ALD0/GGIO1.Ind1.stVal"));
        var b = Ied("IED_B", "192.168.81.11", Point("P-B", "IED_B", "192.168.81.11", "IED_BLD0/GGIO1.Ind2.stVal"));

        Assert.Equal("Connect", a.ConnectionActionText);
        Assert.Equal("Connect", b.ConnectionActionText);

        a.SetPreparationState(true, "Connecting A");

        Assert.False(a.CanPrepareConnection);
        Assert.Equal("Connecting…", a.ConnectionActionText);
        Assert.True(b.CanPrepareConnection);
        Assert.Equal("Connect", b.ConnectionActionText);

        b.ApplyLiveDeviceBinding("device-b", "Monitoring", true, true);
        Assert.Equal("Refresh", b.ConnectionActionText);
    }

    [Fact]
    public void MultipleIeds_CanRetainIndependentConnectedMonitoringState()
    {
        var a = Ied("IED_A", "192.168.81.10", Point("P-A", "IED_A", "192.168.81.10", "IED_ALD0/GGIO1.Ind1.stVal"));
        var b = Ied("IED_B", "192.168.81.11", Point("P-B", "IED_B", "192.168.81.11", "IED_BLD0/GGIO1.Ind2.stVal"));

        a.ApplyLiveDeviceBinding("device-a", "Monitoring · fast MMS", true, true);
        b.ApplyLiveDeviceBinding("device-b", "Monitoring · report", true, true);

        Assert.True(a.IsLiveConnected);
        Assert.True(a.IsLiveMonitoring);
        Assert.Equal("device-a", a.LiveDeviceId);
        Assert.True(b.IsLiveConnected);
        Assert.True(b.IsLiveMonitoring);
        Assert.Equal("device-b", b.LiveDeviceId);
    }

    [Fact]
    public void P1_ConnectAndAcquisitionDoNotUseTestCheckboxAsAuthority()
    {
        var autoConnect = ReadRepoFile("MainWindow.IoTesting.AutoConnect.cs");
        var contextUx = ReadRepoFile("IoListTestingWindow.ContextUx.cs");
        var monitor = ReadRepoFile("MainWindow.IoTesting.MultiIedMonitor.cs");

        Assert.Contains("requestedPointsOverride is null || point.TestEnabled", autoConnect, StringComparison.Ordinal);
        Assert.Contains("point.IsIncludedInFat", autoConnect, StringComparison.Ordinal);
        Assert.Contains("point.ImportReady", autoConnect, StringComparison.Ordinal);
        Assert.Contains("var acquisitionSignals = selection.Matches", autoConnect, StringComparison.Ordinal);
        Assert.Contains("StartIoFatDeviceMonitorAsync(device, acquisitionSignals)", autoConnect, StringComparison.Ordinal);

        // Connect passes no operator checkbox subset. Start FAT still creates captureScope,
        // but its preparation also uses the full IED acquisition scope; only Session.Start
        // receives the selected live evidence rows.
        Assert.DoesNotContain("var enabledReady = targetIed.TestPoints", contextUx, StringComparison.Ordinal);
        Assert.Contains("private Task<IoTestSessionActionResult> PrepareIndependentIedConnectionAsync", contextUx, StringComparison.Ordinal);
        Assert.Contains("Project,\n            targetIed,\n            progress);", contextUx, StringComparison.Ordinal);
        Assert.Contains("var captureScope = selectedIed.TestPoints", contextUx, StringComparison.Ordinal);
        Assert.Contains("point.IsIncludedInFat && point.TestEnabled && point.ImportReady", contextUx, StringComparison.Ordinal);
        Assert.Contains("var result = Session.Start(selectedIed, liveCaptureScope);", contextUx, StringComparison.Ordinal);

        // Runtime StartMonitoringAsync still expects IsSelected. P1 therefore uses a
        // device-local temporary bulk arm, then restores the real Engineering/TEST state.
        Assert.Contains("device.BeginBulkSignalSelection();", monitor, StringComparison.Ordinal);
        Assert.Contains("signal.IsSelected = acquisitionSet.Contains(signal);", monitor, StringComparison.Ordinal);
        Assert.Contains("signal.IsSelected = wasSelected;", monitor, StringComparison.Ordinal);
        Assert.Contains("device.EndBulkSignalSelection();", monitor, StringComparison.Ordinal);
        Assert.Contains("_runtime.StartMonitoringAsync", monitor, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveSignalSelectionMemory(device)", monitor, StringComparison.Ordinal);
    }

    [Fact]
    public void P1_SourceContract_UsesPerIedPreparationWithoutWeakeningEvidenceIsolation()
    {
        var autoConnect = ReadRepoFile("MainWindow.IoTesting.AutoConnect.cs");
        var contextUx = ReadRepoFile("IoListTestingWindow.ContextUx.cs");
        var progressUx = ReadRepoFile("IoListTestingWindow.RealPreparationProgress.cs");
        var connectUx = ReadRepoFile("IoListTestingWindow.MultiIedConnectionUx.cs");
        var printPreview = ReadRepoFile("IoListTestingWindow.PrintPreview.cs");
        var session = ReadRepoFile("Services/IoTesting/IoTestSessionController.cs");

        Assert.Contains("_ioTestLiveBindingService.BindIed(ied, Devices)", autoConnect, StringComparison.Ordinal);
        Assert.DoesNotContain("_ioTestLiveBindingService.Bind(project, Devices)", autoConnect, StringComparison.Ordinal);
        Assert.Contains("requestedPointsOverride", autoConnect, StringComparison.Ordinal);
        Assert.Contains("selectDevice: false", autoConnect, StringComparison.Ordinal);
        Assert.Contains("other IED monitors remain active", autoConnect, StringComparison.Ordinal);
        Assert.DoesNotContain("Session.IsSessionActive", autoConnect, StringComparison.Ordinal);
        Assert.Contains("private async void ConnectIed_Click", contextUx, StringComparison.Ordinal);
        Assert.Contains("targetIed.IsPreparing", contextUx, StringComparison.Ordinal);
        Assert.Contains("SelectedIed.IsPreparing", contextUx, StringComparison.Ordinal);
        Assert.Contains("var active = ied.IsPreparing;", progressUx, StringComparison.Ordinal);
        Assert.DoesNotContain("_preparingIed", progressUx, StringComparison.Ordinal);
        Assert.DoesNotContain("_preparingIed", printPreview, StringComparison.Ordinal);
        Assert.DoesNotContain("ClearStalePreparationFlags", printPreview, StringComparison.Ordinal);
        Assert.Contains("Other IED connection workflows keep running", connectUx, StringComparison.Ordinal);
        Assert.Contains("new Binding(nameof(SelectedIed))", connectUx, StringComparison.Ordinal);

        // P1 parallelizes connection/monitoring only. The evidence controller remains
        // intentionally single-active so relay transitions cannot enter the wrong journal.
        Assert.Contains("Stop the active FAT session before starting another IED.", session, StringComparison.Ordinal);
        Assert.Contains("entry.DeviceId.Equals(activeDevice.DeviceId", session, StringComparison.Ordinal);
    }

    private static IoTestPointPlan Point(string id, string iedName, string ipAddress, string reference) => new()
    {
        TestPointId = id,
        IedName = iedName,
        IpAddress = ipAddress,
        SignalName = id,
        ObjectReference = reference,
        FunctionalConstraint = "ST",
        ExpectedOnText = "ON",
        ExpectedOffText = "OFF",
        LogicalDevice = iedName + "LD0",
        LogicalNode = "GGIO1",
        DataObject = "Ind1",
        DataAttribute = "stVal",
        SourceIecReference = reference,
        EventLogSearchReference = reference,
        ReportDisplayReference = reference + " [ST]",
        TestEnabled = true,
        ImportReady = true
    };

    private static IoTestIedPlan Ied(string name, string ipAddress, params IoTestPointPlan[] points) => new()
    {
        IedName = name,
        IpAddress = ipAddress,
        IedRole = "Protection IED",
        TestPoints = points.ToList()
    };

    private static string ReadRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
