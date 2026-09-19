namespace ARSAS.Tests;

public sealed class LiveDiscoveryPostModelWorkflowRegressionTests
{
    [Fact]
    public void SuccessfulDiscovery_UsesSharedPostModelChooser_InsteadOfOpeningManualCatalogDirectly()
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

        Assert.Contains("device.LiveDiscoveryModel", Read("Services/Iec61850MonitorRuntime.cs"), StringComparison.Ordinal);
        Assert.Contains("await OpenIedWorkspaceActionsAsync(device);", discovery, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenSignalSelectionWizardAsync(device, restoredCount)", discovery, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedPostModelChooser_RoutesDataSetSignalsToExactStaticReportOnlyAuthority()
    {
        var actions = Read("MainWindow.SclQuickActions.cs");
        var sharedAuthority = Read("MainWindow.SharedSclWorkspace.cs");
        var staticRuntime = Read("Services/NativeIec61850Client.StaticDataSetReporting.cs");

        var staticStart = actions.IndexOf("if (dialog.UseStaticDataSet)", StringComparison.Ordinal);
        var manualStart = actions.IndexOf("var accepted = await OpenSignalSelectionWizardAsync", staticStart, StringComparison.Ordinal);
        Assert.True(staticStart >= 0 && manualStart > staticStart);
        var staticChoice = actions[staticStart..manualStart];

        Assert.Contains("ApplyStaticDataSetSelection(device);", staticChoice, StringComparison.Ordinal);
        Assert.Contains("await StartDeviceMonitorAsync(device)", staticChoice, StringComparison.Ordinal);
        Assert.DoesNotContain("UseHybrid", staticChoice, StringComparison.Ordinal);

        Assert.Contains("Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device)", sharedAuthority, StringComparison.Ordinal);
        Assert.Contains("Iec61850StaticDataSetAuthoritySelection.Build(device)", sharedAuthority, StringComparison.Ordinal);
        Assert.Contains("Iec61850MonitoringModeRegistry.UseStaticDataSetReportOnly(device)", sharedAuthority, StringComparison.Ordinal);

        Assert.Contains("device.SclWorkspace?.DesignModel ?? device.LiveDiscoveryModel", staticRuntime, StringComparison.Ordinal);
        Assert.Contains("AllowDynamicDataSetWrites = false", staticRuntime, StringComparison.Ordinal);
        Assert.Contains("PollingPointKeys = Array.Empty<string>()", staticRuntime, StringComparison.Ordinal);
        Assert.Contains("PollingFallbackSignalCount = 0", staticRuntime, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedPostModelChooser_RoutesSignalCatalogToHybridOnlyAfterManualSelectionIsAccepted()
    {
        var actions = Read("MainWindow.SclQuickActions.cs");
        var manualStart = actions.IndexOf("var accepted = await OpenSignalSelectionWizardAsync", StringComparison.Ordinal);
        var methodEnd = actions.IndexOf("internal async Task OpenIedWorkspaceActionsFromCardAsync", manualStart, StringComparison.Ordinal);
        Assert.True(manualStart >= 0 && methodEnd > manualStart);
        var manualChoice = actions[manualStart..methodEnd];

        var acceptedGuard = manualChoice.IndexOf("if (!accepted)", StringComparison.Ordinal);
        var hybridSwitch = manualChoice.IndexOf("Iec61850MonitoringModeRegistry.UseHybrid(device)", StringComparison.Ordinal);
        Assert.True(acceptedGuard >= 0 && hybridSwitch > acceptedGuard);
        Assert.Contains("autoStartAfterSave: false", manualChoice, StringComparison.Ordinal);
        Assert.Contains("await StartDeviceMonitorAsync(device)", manualChoice, StringComparison.Ordinal);
    }

    [Fact]
    public void Chooser_UsesSourceNeutralSignalSourceLabels()
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
