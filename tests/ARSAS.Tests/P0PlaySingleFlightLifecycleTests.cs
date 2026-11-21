using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// SG7_BCU 2026-11-22 physical regression: the saved-model Connect helper
/// releases IsBusy before StartMonitoringAsync completes. A second Play must
/// never be offered during this handoff. Stop remains preemptive.
/// </summary>
public sealed class P0PlaySingleFlightLifecycleTests
{
    [Fact]
    public void PlayIsDisabledAcrossTheWholeConnectToMonitorHandoff()
    {
        var device = new Iec61850MonitorDevice { HasDiscoveryCache = true };
        Assert.True(device.CanPlayAction);

        device.IsPlayWorkflowActive = true;
        device.IsBusy = true;
        Assert.False(device.CanPlayAction);
        Assert.True(device.CanStopAction);

        // The real fast-connect helper clears Busy while the Play click
        // is still awaiting StartMonitoringAsync. This was the observed gap.
        device.IsConnected = true;
        device.IsBusy = false;
        Assert.False(device.CanPlayAction);
        Assert.True(device.CanStopAction);

        device.IsMonitoring = true;
        device.IsPlayWorkflowActive = false;
        Assert.False(device.CanPlayAction);
        Assert.True(device.CanStopAction);
    }

    [Fact]
    public void FailedConnectReleasesPlayWithoutLeavingAFalseBusyState()
    {
        var device = new Iec61850MonitorDevice();
        device.IsPlayWorkflowActive = true;
        device.IsBusy = true;
        device.IsBusy = false;
        device.IsConnected = false;
        Assert.False(device.CanPlayAction);

        device.IsPlayWorkflowActive = false;
        Assert.True(device.CanPlayAction);
        Assert.False(device.CanStopAction);
    }

    [Fact]
    public void StopCanPreemptAndInvalidateAwaitingPlay()
    {
        var device = new Iec61850MonitorDevice();
        device.IsPlayWorkflowActive = true;
        Assert.True(device.CanStopAction);

        device.IsPlayWorkflowCanceled = true;
        device.IsStopping = true;
        Assert.False(device.CanPlayAction);
        Assert.False(device.CanStopAction);

        device.IsStopping = false;
        Assert.False(device.CanPlayAction); // no premature second Start
        device.IsPlayWorkflowActive = false;
        device.IsPlayWorkflowCanceled = false;
        Assert.True(device.CanPlayAction);
    }

    [Fact]
    public void WpfPlayUsesSingleOwnerAndRejectsLateStopCompletion()
    {
        static string Read(string name)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var path = Path.Combine(directory.FullName, name);
                if (File.Exists(path)) return File.ReadAllText(path);
                directory = directory.Parent;
            }
            throw new FileNotFoundException(name);
        }

        var main = Read("MainWindow.xaml.cs");
        var start = main.IndexOf("private async void IedPlayAction_Click(", StringComparison.Ordinal);
        var stop = main.IndexOf("private async void IedStopAction_Click(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && stop > start);
        var handler = main[start..stop];
        Assert.Contains("!device.CanPlayAction", handler);
        Assert.Contains("device.IsPlayWorkflowActive = true;", handler);
        Assert.Contains("device.IsPlayWorkflowCanceled || device.IsStopping", handler);
        Assert.Contains("await StartDeviceMonitorAsync(device);", handler);
        Assert.Contains("device.IsPlayWorkflowActive = false;", handler);
        Assert.True(handler.IndexOf("IsPlayWorkflowActive = true;", StringComparison.Ordinal) <
                    handler.IndexOf("await ConnectUsingSavedModelAsync(device)", StringComparison.Ordinal));
        Assert.Contains("device.IsPlayWorkflowCanceled = true;", main[stop..]);
        Assert.Contains("device.IsPlayWorkflowCanceled ||", main[main.IndexOf("private async Task<bool> StartDeviceMonitorAsync(", StringComparison.Ordinal)..]);
        Assert.Contains("IsEnabled=\"{Binding CanPlayAction}\"", Read("MainWindow.xaml"));
    }
}
