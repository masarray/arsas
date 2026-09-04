namespace ARSAS.Tests;

public sealed class SclTaskFirstQuickStartRegressionTests
{
    [Fact]
    public void QuickStart_SeparatesOfflineEngineeringFromMonitoringIntent()
    {
        var xaml = File.ReadAllText(FindRepoFile("SclSignalSelectionModeWindow.xaml"));
        var code = File.ReadAllText(FindRepoFile("SclSignalSelectionModeWindow.xaml.cs"));

        Assert.Contains("Workspace opened offline", xaml, StringComparison.Ordinal);
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
    public void OfflineAndFileQuickActions_DoNotStartMonitoringPipeline()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.SclQuickActions.cs"));

        Assert.Contains("IedEditRcb_Click", source, StringComparison.Ordinal);
        Assert.Contains("new FaultRecordWindow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartDeviceMonitorAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectUsingSavedModelAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartMonitoring", source, StringComparison.Ordinal);
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
