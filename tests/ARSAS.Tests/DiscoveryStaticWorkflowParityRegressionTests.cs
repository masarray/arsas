namespace ARSAS.Tests;

public sealed class DiscoveryStaticWorkflowParityRegressionTests
{
    [Fact]
    public void ProductionDiscovery_UsesFieldVerifiedSmartSingleFlightRoute()
    {
        var source = Read("Services/NativeIec61850Client.cs");

        var entry = source.IndexOf(
            "public async Task<IReadOnlyList<SignalDefinition>> DiscoverSignalsAsync",
            StringComparison.Ordinal);
        var legacy = source.IndexOf(
            "LastDiscoverySummary = string.Empty;",
            entry,
            StringComparison.Ordinal);
        var smart = source.IndexOf(
            "DiscoverSignalsSmartForCaptureAsync(cancellationToken, progress)",
            entry,
            StringComparison.Ordinal);

        Assert.True(entry >= 0 && smart > entry && legacy > smart);
        Assert.Contains("ResetSmartDiscoveryAuthority(); // __P0_5C_CONNECT_RESET__", source, StringComparison.Ordinal);
        Assert.Contains("ResetSmartDiscoveryAuthority(); // __P0_5C_DISPOSE_RESET__", source, StringComparison.Ordinal);

        var smartSource = Read("Services/NativeIec61850Client.SmartDiscoveryCapture.cs");
        Assert.Contains("P0-R9-STRUCTURAL", smartSource, StringComparison.Ordinal);
        Assert.Contains("DiscoverSmartSingleFlightAsync", smartSource, StringComparison.Ordinal);
        Assert.Contains("discoveryValues=deferred", smartSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoveryCompletion_EntersSameIedActionsWorkflowAsOpenedModel()
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
        var discoveryFlow = source[start..end];

        Assert.Contains("var discoveredDataSetCount = device.LiveDiscoveryModel?.DataSets.Count ?? 0;", discoveryFlow, StringComparison.Ordinal);
        Assert.Contains("device.SignalCount > 0 || discoveredDataSetCount > 0", discoveryFlow, StringComparison.Ordinal);
        Assert.Contains("await OpenIedWorkspaceActionsAsync(device);", discoveryFlow, StringComparison.Ordinal);
        Assert.DoesNotContain("await OpenSignalSelectionWizardAsync(device, restoredCount);", discoveryFlow, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoveredIed_StaticDataSetAction_IsCapabilityDrivenByCanonicalModel()
    {
        var codeBehind = Read("SclSignalSelectionModeWindow.xaml.cs");
        var xaml = Read("SclSignalSelectionModeWindow.xaml");

        Assert.Contains("targetDevice.SclWorkspace?.DesignModel ?? targetDevice.LiveDiscoveryModel", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CanUseStaticDataSet = dataSetCount > 0", codeBehind, StringComparison.Ordinal);
        Assert.Contains("BuildReportBackedDataSetReferences(targetDevice)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanUseStaticDataSet}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding StaticDataSetAvailabilityText}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticReporting_ReusesCompletedDiscoveryAuthority_WithoutRediscovery()
    {
        var source = Read("Services/NativeIec61850Client.cs");
        var start = source.IndexOf(
            "private async Task<ArMms.MmsDiscoveryResult?> EnsureDiscoveryForReportingAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private async Task<IReadOnlyList<ArMms.MmsDataSetDirectoryResult>> ReadPlannedDataSetDirectoriesAsync",
            start,
            StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        var reportingDiscovery = source[start..end];
        Assert.Contains("if (_lastDiscovery != null)", reportingDiscovery, StringComparison.Ordinal);
        Assert.Contains("return _lastDiscovery;", reportingDiscovery, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticReporting_UsesCanonicalMemberOrder_WithoutSecondDirectoryRead()
    {
        var source = Read("Services/NativeIec61850Client.StaticDataSetReporting.cs");

        Assert.Contains("BuildModelDataSetDirectories", source, StringComparison.Ordinal);
        Assert.Contains("discovery.DataSetDirectories.SingleOrDefault", source, StringComparison.Ordinal);
        Assert.Contains("TryVerifyStaticDataSetMemberOrder", source, StringComparison.Ordinal);
        Assert.Contains("Members = modelDirectory.Members", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetDataSetDirectoriesAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Members = liveDirectory.Members", source, StringComparison.Ordinal);
        Assert.Contains("PollingPointKeys = Array.Empty<string>()", source, StringComparison.Ordinal);
        Assert.Contains("PollingFallbackSignalCount = 0", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenedModelAndDiscoveredModel_ShareTheSameDataSetProjectionBuilder()
    {
        var source = Read("Services/NativeIec61850Client.SclAssisted.cs");

        Assert.Contains("BuildModelDataSetDirectories(_liveModel, \"TrustedScl\")", source, StringComparison.Ordinal);
        Assert.Contains("private static IReadOnlyList<ArMms.MmsDataSetDirectoryResult> BuildModelDataSetDirectories", source, StringComparison.Ordinal);
        Assert.Contains("Source = source", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportProjection_UsesSameModelAuthorityPrecedenceAsPlanning()
    {
        var source = Read("Services/Iec61850MonitorRuntime.cs");

        Assert.Contains(
            "session.Device.SclWorkspace?.DesignModel ?? session.Device.LiveDiscoveryModel",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "session.Device.LiveDiscoveryModel ?? session.Device.SclWorkspace?.DesignModel",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingOpenedModelWorkflow_RemainsTaskFirstAndStaticReportOnly()
    {
        var quickActions = Read("MainWindow.SclQuickActions.cs");
        var shared = Read("MainWindow.SharedSclWorkspace.cs");

        Assert.Contains("new SclSignalSelectionModeWindow(1, device)", quickActions, StringComparison.Ordinal);
        Assert.Contains("if (dialog.UseStaticDataSet)", quickActions, StringComparison.Ordinal);
        Assert.Contains("ApplyStaticDataSetSelection(device)", quickActions, StringComparison.Ordinal);
        Assert.Contains("Iec61850MonitoringModeRegistry.UseStaticDataSetReportOnly(device)", shared, StringComparison.Ordinal);
        Assert.Contains("cyclic MMS process polling and dynamic DataSet writes remain disabled", shared, StringComparison.Ordinal);
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
