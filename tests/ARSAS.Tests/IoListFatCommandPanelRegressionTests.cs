namespace ARSAS.Tests;

public sealed class IoListFatCommandPanelRegressionTests
{
    [Fact]
    public void FatCommandPanel_UsesSharedEngineeringDeviceAndCommandCollection()
    {
        var bridge = File.ReadAllText(FindRepoFile("MainWindow.IoFatCommandBridge.cs"));
        var panel = File.ReadAllText(FindRepoFile("IoListTestingWindow.CommandPanel.cs"));

        Assert.Contains("ResolveIoTestDevice(ied.LiveDeviceId)", bridge, StringComparison.Ordinal);
        Assert.Contains("ResolveIoTestDevice(ied.IpAddress)", bridge, StringComparison.Ordinal);
        Assert.Contains("ResolveIoTestDevice(ied.IedName)", bridge, StringComparison.Ordinal);
        Assert.Contains("device.RefreshCommandSignalProjection()", bridge, StringComparison.Ordinal);
        Assert.Contains("_signalOwners[signal] = device", bridge, StringComparison.Ordinal);
        Assert.Contains("device.CommandSignals", panel, StringComparison.Ordinal);
        Assert.Contains("shared Engineering command backend", panel, StringComparison.Ordinal);

        // FAT is only another operating surface. It must never construct a second MMS
        // control client or bypass the Engineering control execution/evidence pipeline.
        Assert.DoesNotContain("ExecuteControlAsync", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtime.", panel, StringComparison.Ordinal);
        Assert.Contains("ExecuteIoFatControlClaimAsync", panel, StringComparison.Ordinal);
        Assert.Contains("return ExecuteClaimedControlAsync(signal, claim)", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void FatCommandPanel_PreservesEngineeringSafetyAndControlActions()
    {
        var panel = File.ReadAllText(FindRepoFile("IoListTestingWindow.CommandPanel.cs"));

        Assert.Contains("TryStageControlConfirmation", panel, StringComparison.Ordinal);
        Assert.Contains("TryClaimControlConfirmation", panel, StringComparison.Ordinal);
        Assert.Contains("TryBeginDirectControlCommand", panel, StringComparison.Ordinal);
        Assert.Contains("\"Open [01]\"", panel, StringComparison.Ordinal);
        Assert.Contains("\"Closed [10]\"", panel, StringComparison.Ordinal);
        Assert.Contains("\"True\"", panel, StringComparison.Ordinal);
        Assert.Contains("\"False\"", panel, StringComparison.Ordinal);
        Assert.Contains("\"Raise\"", panel, StringComparison.Ordinal);
        Assert.Contains("\"Lower\"", panel, StringComparison.Ordinal);
        Assert.Contains("ControlSetPointText", panel, StringComparison.Ordinal);
        Assert.Contains("ControlInterlockCheck", panel, StringComparison.Ordinal);
        Assert.Contains("ControlSynchroCheck", panel, StringComparison.Ordinal);
        Assert.Contains("ControlTestMode", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void FatCommandPanel_RemainsFailClosedForStatusOnlyOrUnsupportedControls()
    {
        var bridge = File.ReadAllText(FindRepoFile("MainWindow.IoFatCommandBridge.cs"));
        var panel = File.ReadAllText(FindRepoFile("IoListTestingWindow.CommandPanel.cs"));
        var monitorModels = File.ReadAllText(FindRepoFile("Models/MonitorModels.cs"));

        Assert.Contains("live ctlModel", bridge, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("StatusOnly stays read-only", bridge, StringComparison.Ordinal);
        Assert.Contains("device.CommandSignals", bridge, StringComparison.Ordinal);
        Assert.Contains("ControlSupportsOperate", monitorModels, StringComparison.Ordinal);
        Assert.Contains("IsGenericControl", monitorModels, StringComparison.Ordinal);
        Assert.Contains("Status-only controls remain read-only", panel, StringComparison.Ordinal);
        Assert.Contains("unsupported generic types stay fail-closed", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void FatSignalGridAndCommandPanelRemainSeparateSurfaces()
    {
        var panel = File.ReadAllText(FindRepoFile("IoListTestingWindow.CommandPanel.cs"));
        var runtime = File.ReadAllText(FindRepoFile("Services/Iec61850MonitorRuntime.cs"));

        Assert.Contains("SelectedIed.TestPoints", panel, StringComparison.Ordinal);
        Assert.Contains("IED COMMAND PANEL", panel, StringComparison.Ordinal);
        Assert.Contains("CommandSignals", panel, StringComparison.Ordinal);
        Assert.Contains("signal.IsSelected && signal.CanPublishToRuntime", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("CanPublishToRuntime || signal.IsControlSignal", runtime, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
