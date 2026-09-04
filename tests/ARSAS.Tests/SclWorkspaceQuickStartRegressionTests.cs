namespace ARSAS.Tests;

public sealed class SclWorkspaceQuickStartRegressionTests
{
    [Fact]
    public void EngineeringPrompt_RequiresExplicitMonitoringIntent()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.SharedSclWorkspace.cs"));

        Assert.Contains("SclWorkspaceQuickStartWindow", source, StringComparison.Ordinal);
        Assert.Contains("case SclWorkspaceAction.MonitorStaticDataSet:", source, StringComparison.Ordinal);
        Assert.Contains("return SclSignalSelectionMode.StaticDataSet;", source, StringComparison.Ordinal);
        Assert.Contains("case SclWorkspaceAction.MonitorSelectedSignals:", source, StringComparison.Ordinal);
        Assert.Contains("return SclSignalSelectionMode.Manual;", source, StringComparison.Ordinal);

        Assert.Contains("case SclWorkspaceAction.RcbEngineering:", source, StringComparison.Ordinal);
        Assert.Contains("OpenRcbEngineeringQuickStart(selectedDevice)", source, StringComparison.Ordinal);
        Assert.Contains("case SclWorkspaceAction.DownloadComtrade:", source, StringComparison.Ordinal);
        Assert.Contains("OpenComtradeQuickStart(selectedDevice)", source, StringComparison.Ordinal);
        Assert.Contains("case SclWorkspaceAction.BrowseOffline:", source, StringComparison.Ordinal);
        Assert.Contains("no MMS connection or monitoring session was started", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FileAndRcbQuickStarts_DoNotStartLiveMonitor()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.SclWorkspaceQuickStart.cs"));

        Assert.Contains("new FaultRecordWindow(device.Name, device.IpAddress, device.Port)", source, StringComparison.Ordinal);
        Assert.Contains("file service only, monitoring unchanged", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IedEditRcb_Click", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectUsingSavedModelAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartDeviceMonitorAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenSignalSelectionWizardAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void QuickStartDialog_ExposesMonitoringEngineeringAndFileActions()
    {
        var xaml = File.ReadAllText(FindRepoFile("SclWorkspaceQuickStartWindow.xaml"));

        Assert.Contains("Tag=\"MonitorStaticDataSet\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"MonitorSelectedSignals\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"RcbEngineering\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"DownloadComtrade\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Keep Offline\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Opening SCL itself stays offline", xaml, StringComparison.Ordinal);
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
