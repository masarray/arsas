using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Regression contract from real 2026-10-13 BCUGE/ZIVAVR field diagnostics:
/// StopDeviceAsync -> ResetAssociationControlAuthority ->
/// CommandSignals.ReplaceAll must not raise WPF CollectionView cross-thread
/// exceptions, and the offline explorer may not masquerade as a live monitor.
/// </summary>
public sealed class P10StopReconnectOfflineTests
{
    private static string Read(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null)
        {
            var path = Path.Combine(root.FullName, name);
            if (File.Exists(path)) return File.ReadAllText(path);
            root = root.Parent;
        }
        throw new FileNotFoundException(name);
    }

    [Fact]
    public void StopCanPreemptAnInProgressConnectOrMonitor()
    {
        var device = new Iec61850MonitorDevice { IsBusy = true };
        Assert.True(device.CanStopAction);
        device.IsBusy = false;
        device.IsConnected = true;
        Assert.True(device.CanStopAction);
        device.IsConnected = false;
        device.IsMonitoring = true;
        Assert.True(device.CanStopAction);
        device.IsMonitoring = false;
        Assert.False(device.CanStopAction);
    }

    [Fact]
    public async Task RepeatedStopWithoutASessionIsIdempotent()
    {
        await using var runtime = new ArIED61850Tester.Iec61850MonitorRuntime();
        await runtime.StopDeviceAsync("never-connected");
        await runtime.StopDeviceAsync("never-connected");
        // Concurrent double-clicks also must not create a faulty session.
        await Task.WhenAll(Enumerable.Range(0, 12).Select(
            _ => runtime.StopDeviceAsync("never-connected")));
        Assert.Equal(0, runtime.ConnectedDeviceCount);
        Assert.Equal(0, runtime.MonitoringDeviceCount);
    }

    [Fact]
    public void DisconnectFlightDisablesBothButtonsAndRecoversPlay()
    {
        var device = new Iec61850MonitorDevice
        {
            IsConnected = true,
            HasDiscoveryCache = true,
            IsStopping = true
        };
        Assert.False(device.CanPlayAction);
        Assert.False(device.CanStopAction);
        device.IsMonitoring = false;
        device.IsConnected = false;
        device.IsStopping = false;
        Assert.True(device.CanPlayAction);
        Assert.False(device.CanStopAction);
    }

    [Fact]
    public void ControlProjectionRefreshIsDispatchedBeforeTheStopGateReleases()
    {
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");
        var facade = Read("Services/UiResponsiveIec61850MonitorRuntimeFacade.cs");
        Assert.Contains("private static async Task ResetAssociationControlAuthorityAsync", runtime);
        Assert.Contains("System.Windows.Application.Current?.Dispatcher", runtime);
        Assert.Contains("dispatcher.InvokeAsync(", runtime);
        Assert.Contains("await ResetAssociationControlAuthorityAsync(session.Device)", runtime);
        Assert.Contains("await slot.StopGate.WaitAsync()", facade);
        Assert.Contains("if (slot.ActiveStopTask is { IsCompleted: false } inFlight)", facade);
        Assert.Contains("slot.ActiveStopTask = completion.Task;", facade);
        Assert.Contains("await stopOperation().ConfigureAwait(false)", facade);
        Assert.Contains("finally", runtime);
        Assert.Contains("session.Device.IsConnected = false", runtime);
    }

    [Fact]
    public void DisconnectPreservesOfflineRowsAndNeverLeavesAnActionButtonInEmptyCenter()
    {
        var main = Read("MainWindow.xaml.cs");
        var xaml = Read("MainWindow.xaml");
        var stopStart = main.IndexOf("private async Task StopDeviceConnectionAsync(", StringComparison.Ordinal);
        var stopEnd = main.IndexOf("private void MarkDevicePointsInactive(", stopStart, StringComparison.Ordinal);
        Assert.True(stopStart >= 0 && stopEnd > stopStart);
        var stop = main[stopStart..stopEnd];
        Assert.Contains("MarkDevicePointsInactive(device, disconnected: true)", stop);
        Assert.Contains("EnsureOfflinePointProjection(device)", stop);
        Assert.Contains("ResetControlReadinessForNewAssociation(device)", stop);
        Assert.DoesNotContain("RemoveDevicePoints(device.DeviceId)", stop);
        Assert.DoesNotContain("device.Points.Clear()", stop);

        var centerStart = xaml.IndexOf("<!-- Presentation only; Play/Stop", StringComparison.Ordinal);
        var centerEnd = xaml.IndexOf("</Grid>", centerStart, StringComparison.Ordinal);
        Assert.True(centerStart >= 0 && centerEnd > centerStart);
        var emptyView = xaml[centerStart..centerEnd];
        Assert.DoesNotContain("<Button", emptyView);
        Assert.Contains("Press Play on the IED card", emptyView);
        Assert.Contains("IsHitTestVisible=\"False\"", emptyView);
    }

    [Fact]
    public void OfflineSnapshotRetainsLastValueButNeverClaimsFreshIEDQuality()
    {
        var point = new Iec61850MonitorPoint
        {
            DeviceId = "IED-A",
            DeviceName = "BCUGE",
            IecReference = "BCUGEF650/CSWI1.Pos.stVal",
            IecDataType = "Dbpos",
            Value = "Closed [10]",
            Quality = "Good",
            DeviceTimestamp = "2026-10-13 10:59:19.029",
            SourceMode = "Static DataSet: StaticBrcb",
            IsOffline = false
        };
        point.IsOffline = true;
        point.Quality = "Offline";
        point.SourceMode = "Offline";
        Assert.Equal("Closed [10]", point.Value);
        Assert.Equal("2026-10-13 10:59:19.029", point.DeviceTimestamp);
        Assert.True(point.IsOffline);
        Assert.Equal("Offline", point.Quality);
    }

    [Fact]
    public void OfflineControlDockNeverCreatesAHiddenMmsAssociation()
    {
        var code = Read("MainWindow.xaml.cs");
        var xaml = Read("MainWindow.xaml");
        var start = code.IndexOf("private async void ControlDetails_Click(", StringComparison.Ordinal);
        var end = code.IndexOf("private void RemoveControlFeedbackIndex(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var modelHandler = code[start..end];
        Assert.Contains("!device.IsConnected || device.IsStopping || device.IsBusy", modelHandler);
        Assert.DoesNotContain("ConnectUsingSavedModelAsync(", modelHandler);
        Assert.DoesNotContain("ConnectAndConfigureDeviceAsync(", modelHandler);
        Assert.Contains("DataContext.SelectedDevice.IsConnected", xaml);
    }

    [Fact]
    public void BothExplorerGridsMuteOfflineRowsAndStartReplacesStaleProjection()
    {
        var xaml = Read("MainWindow.xaml");
        var code = Read("MainWindow.xaml.cs");
        Assert.Equal(2, xaml.Split("Binding=\"{Binding IsOffline}\"",
            StringSplitOptions.None).Length - 1);
        Assert.Contains("EnsureOfflinePointProjection(device);", code);
        Assert.Contains("point.IsOffline = false;", code);
        Assert.Contains("point.Status = disconnected ?", code);
        Assert.Contains("RemoveDevicePoints(device.DeviceId);", code); // on successful fresh Start
        Assert.Contains("if (device.IsBusy || device.IsConnected || device.IsMonitoring)", code);
    }
}
