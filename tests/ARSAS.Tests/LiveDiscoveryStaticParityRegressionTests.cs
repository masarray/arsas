namespace ARSAS.Tests;

public sealed class LiveDiscoveryStaticParityRegressionTests
{
    [Fact]
    public void Discovery_EntersSharedPostModelChooser()
    {
        var source = Read("MainWindow.xaml.cs");
        var start = source.IndexOf(
            "private async Task<bool> ConnectAndConfigureDeviceAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private async Task<bool> ConnectUsingSavedModelAsync",
            start,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);

        var discovery = source[start..end];
        Assert.Contains("await OpenIedWorkspaceActionsAsync(device);", discovery, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenSignalSelectionWizardAsync(device, restoredCount)", discovery, StringComparison.Ordinal);
    }

    [Fact]
    public void DataSetSignals_UsesCanonicalMembersAndConfiguredStaticActivation()
    {
        var staticPath = Read("Services/NativeIec61850Client.StaticDataSetReporting.cs");

        Assert.Contains("BuildModelDataSetDirectories", staticPath, StringComparison.Ordinal);
        Assert.Contains("Members = modelDirectory.Members", staticPath, StringComparison.Ordinal);
        Assert.Contains("discovery.DataSetDirectories", staticPath, StringComparison.Ordinal);
        Assert.Contains("TryVerifyStaticDataSetMemberOrder", staticPath, StringComparison.Ordinal);
        Assert.Contains("StartConfiguredStaticReportMonitorAsync", staticPath, StringComparison.Ordinal);
        Assert.DoesNotContain("GetDataSetDirectoriesAsync", staticPath, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPersistentReportMonitorClientCompatibleAsync", staticPath, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticProjection_UsesSameAuthorityPrecedenceAsPlanning()
    {
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");

        Assert.Contains(
            "session.Device.SclWorkspace?.DesignModel ?? session.Device.LiveDiscoveryModel",
            runtime,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "session.Device.LiveDiscoveryModel ?? session.Device.SclWorkspace?.DesignModel",
            runtime,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Chooser_UsesSourceNeutralLabels()
    {
        var xaml = Read("SclSignalSelectionModeWindow.xaml");

        Assert.Contains("DataSet Signals", xaml, StringComparison.Ordinal);
        Assert.Contains("Static report-only", xaml, StringComparison.Ordinal);
        Assert.Contains("Signal Catalog", xaml, StringComparison.Ordinal);
    }

    private static string Read(string relativePath)
        => File.ReadAllText(FindRepoFile(relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

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
