namespace ARSAS.Tests;

public sealed class SclTaskFirstQuickStartRegressionTests
{
    [Fact]
    public void QuickStart_SeparatesOfflineEngineeringFromMonitoringIntent()
    {
        var xaml = File.ReadAllText(FindRepoFile("SclSignalSelectionModeWindow.xaml"));
        var code = File.ReadAllText(FindRepoFile("SclSignalSelectionModeWindow.xaml.cs"));

        Assert.Contains("ContextHeading", xaml, StringComparison.Ordinal);
        Assert.Contains("Workspace opened offline", code, StringComparison.Ordinal);
        Assert.Contains("Static DataSet", xaml, StringComparison.Ordinal);
        Assert.Contains("Select Signals", xaml, StringComparison.Ordinal);
        Assert.Contains("RCB Engineering", xaml, StringComparison.Ordinal);
        Assert.Contains("Download COMTRADE", xaml, StringComparison.Ordinal);
        Assert.Contains("Browse Offline", xaml, StringComparison.Ordinal);
        Assert.Contains("MonitorStaticDataSet_Click", code, StringComparison.Ordinal);
        Assert.Contains("MonitorManual_Click", code, StringComparison.Ordinal);
        Assert.Contains("OpenSelectedSclRcbEngineering", code, StringComparison.Ordinal);
        Assert.Contains("OpenSelectedSclComtradeDownload", code, StringComparison.Ordinal);
    }

    [Fact]
    public void OfflineAndFileQuickActions_RemainTaskScoped_WhenReusableChooserCanAlsoStartMonitoring()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.SclQuickActions.cs"));
        var rcb = Slice(source, "internal void OpenSclRcbEngineering", "internal void OpenSelectedSclComtradeDownload");
        var comtrade = Slice(source, "internal void OpenSclComtradeDownload", "internal async Task OpenIedWorkspaceActionsAsync");

        Assert.Contains("IedEditRcb_Click", rcb, StringComparison.Ordinal);
        Assert.DoesNotContain("StartDeviceMonitorAsync", rcb, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectUsingSavedModelAsync", rcb, StringComparison.Ordinal);

        Assert.Contains(
            "new FaultRecordWindow(device.Name, device.IpAddress, device.Port)",
            comtrade,
            StringComparison.Ordinal);
        Assert.DoesNotContain("new FaultRecordWindow(device,", comtrade, StringComparison.Ordinal);
        Assert.DoesNotContain("StartDeviceMonitorAsync", comtrade, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectUsingSavedModelAsync", comtrade, StringComparison.Ordinal);
        Assert.DoesNotContain("StartMonitoring", comtrade, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineeringAndFileActions_CloseChooserBeforeOpeningExactTargetWorkflow()
    {
        var code = File.ReadAllText(FindRepoFile("SclSignalSelectionModeWindow.xaml.cs"));

        Assert.Contains("DialogResult = false;", code, StringComparison.Ordinal);
        Assert.Contains("OpenSclRcbEngineering(_targetDevice)", code, StringComparison.Ordinal);
        Assert.Contains("OpenSelectedSclRcbEngineering()", code, StringComparison.Ordinal);
        Assert.Contains("OpenSclComtradeDownload(_targetDevice)", code, StringComparison.Ordinal);
        Assert.Contains("OpenSelectedSclComtradeDownload()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void IedCard_EditAction_ReopensSameChooser_AndPinsActionsToClickedIed()
    {
        var behavior = File.ReadAllText(FindRepoFile("IedWorkspaceActionsCardBehavior.cs"));
        var actions = File.ReadAllText(FindRepoFile("MainWindow.SclQuickActions.cs"));
        var chooser = File.ReadAllText(FindRepoFile("SclSignalSelectionModeWindow.xaml.cs"));

        Assert.Contains("IED Actions — Static DataSet, Select Signals, RCB Engineering, COMTRADE, Browse Offline", behavior, StringComparison.Ordinal);
        Assert.Contains("args.Handled = true;", behavior, StringComparison.Ordinal);
        Assert.Contains("OpenIedWorkspaceActionsFromCardAsync(button)", behavior, StringComparison.Ordinal);
        Assert.Contains("new SclSignalSelectionModeWindow(1, device)", actions, StringComparison.Ordinal);
        Assert.Contains("if (dialog.ShowDialog() != true)", actions, StringComparison.Ordinal);
        Assert.Contains("Iec61850MonitoringModeRegistry.UseHybrid(device)", actions, StringComparison.Ordinal);
        Assert.Contains("private readonly Iec61850MonitorDevice? _targetDevice", chooser, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingMonitoringModes_RemainTheOnlyAcceptedSignalSelectionModes()
    {
        var sharedWorkspace = File.ReadAllText(FindRepoFile("MainWindow.SharedSclWorkspace.cs"));

        Assert.Contains("StaticDataSet", sharedWorkspace, StringComparison.Ordinal);
        Assert.Contains("Manual", sharedWorkspace, StringComparison.Ordinal);
        Assert.DoesNotContain("EditRcb,", sharedWorkspace, StringComparison.Ordinal);
        Assert.DoesNotContain("Comtrade,", sharedWorkspace, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + 1, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not slice '{startMarker}' -> '{endMarker}'.");
        return source[start..end];
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
        throw new FileNotFoundException(relativePath);
    }
}
