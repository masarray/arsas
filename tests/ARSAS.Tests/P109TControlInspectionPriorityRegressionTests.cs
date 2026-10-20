using System;
using System.IO;

namespace ARSAS.Tests;

public sealed class P109TControlInspectionPriorityRegressionTests
{
    [Fact]
    public void DockRefreshNeverQueuesParallelNativeInspectionsAheadOfACommand()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "MainWindow.xaml.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var main = File.ReadAllText(Path.Combine(root!.FullName, "MainWindow.xaml.cs"));

        var start = main.IndexOf(
            "private async Task RefreshControlValuesAsync(Iec61850MonitorDevice device, bool force = false)",
            StringComparison.Ordinal);
        var finish = main.IndexOf("private bool RejectCommandWhenIedFlightUnavailable(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && finish > start);

        var refresh = main[start..finish];
        Assert.Contains("_controlRefreshInProgress.Add(device)", refresh);
        Assert.Contains("_controlRefreshInProgress.Remove(device)", refresh);
        Assert.Contains("foreach (var signal in candidates)", refresh);
        Assert.Contains("await Dispatcher.Yield(DispatcherPriority.Background);", refresh);
        Assert.Contains("device.CommandSignals.Any(active => active.ControlCommandBusy)", refresh);
        Assert.Contains("Iec61850ControlIdentity.NeedsInspection(signal, force)", refresh);
        Assert.Contains("signal.ControlInspectionBusy = true;", refresh);
        Assert.Contains("signal.ControlInspectionBusy = false;", refresh);
        Assert.DoesNotContain("Task.WhenAll(candidates.Select", refresh);
        Assert.DoesNotContain("new SemaphoreSlim(3, 3)", refresh);
    }

    [Fact]
    public void ControlExecutionRetainsTheSameNativeSingleFlightAndSafetyChecks()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Services", "NativeIec61850Client.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var client = File.ReadAllText(Path.Combine(root!.FullName, "Services", "NativeIec61850Client.cs"));
        var start = client.IndexOf("public async Task<Iec61850ControlCommandResult> ExecuteControlAsync(", StringComparison.Ordinal);
        var end = client.IndexOf("private static Iec61850ControlCapabilities BuildControlCapabilities(", start, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var control = end > start ? client[start..end] : client[start..];

        Assert.Contains("_controlCommandGate.WaitAsync(cancellationToken)", control);
        Assert.Contains("InterlockCheck = request.InterlockCheck", control);
        Assert.Contains("SynchroCheck = request.SynchroCheck", control);
        Assert.Contains("AutoSelect = true", control);
        Assert.Contains("control.OperateAsync(nativeRequest, cancellationToken)", control);
        Assert.Contains("WaitForNativeControlFeedbackAsync(", control);
    }
}
