namespace ARSAS.Tests;

public sealed class DiscoveryStaticWorkflowParityRegressionTests
{
    [Fact]
    public void CandidateDiscovery_UsesEvidenceGatedFieldVerifiedSmartRoute()
    {
        var source = Read("Services/NativeIec61850Client.cs");
        var patcher = Read("scripts/enable-smart-discovery-capture.ps1");
        var smartSource = Read("Services/NativeIec61850Client.SmartDiscoveryCapture.cs");

        // Ordinary tracked source remains promotion-gated, while field-capture/R7 builds
        // intentionally transform this same file before compiling. The regression contract
        // therefore accepts both pre-transform and post-transform source, but requires the
        // exact guarded route and lifecycle reset markers whenever the route is installed.
        var routeInstalled = source.Contains(
            "return await DiscoverSignalsSmartForCaptureAsync(cancellationToken, progress)",
            StringComparison.Ordinal);

        Assert.Contains(
            "DiscoverSignalsSmartForCaptureAsync(cancellationToken, progress)",
            patcher,
            StringComparison.Ordinal);
        Assert.Contains("__P0_5C_CONNECT_RESET__", patcher, StringComparison.Ordinal);
        Assert.Contains("__P0_5C_DISPOSE_RESET__", patcher, StringComparison.Ordinal);

        if (routeInstalled)
        {
            Assert.Contains("__P0_5C_CONNECT_RESET__", source, StringComparison.Ordinal);
            Assert.Contains("__P0_5C_DISPOSE_RESET__", source, StringComparison.Ordinal);
        }

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
    public void StaticDataSetSelection_VerifiesAuthoritativeUnitsBeforeArmingReports()
    {
        var quickActions = Read("MainWindow.SclQuickActions.cs");
        var unitCall = quickActions.IndexOf(
            "EnrichSelectedStaticDataSetUnitsAsync",
            StringComparison.Ordinal);
        var monitorCall = quickActions.IndexOf(
            "StartDeviceMonitorAsync(device)",
            unitCall,
            StringComparison.Ordinal);

        Assert.True(unitCall >= 0, "Discovery Static DataSet path must perform bounded unit enrichment.");
        Assert.True(monitorCall > unitCall, "Engineering-unit reads must finish before RCB monitoring is armed.");
        Assert.DoesNotContain("device.SclWorkspace == null", quickActions, StringComparison.Ordinal);

        var client = Read("Services/NativeIec61850Client.cs");
        Assert.Contains("EnrichAuthoritativeEngineeringUnitsAsync", client, StringComparison.Ordinal);
        Assert.Contains("allowInferredFallback: false", client, StringComparison.Ordinal);
        Assert.Contains(".units.SIUnit", client, StringComparison.Ordinal);
        Assert.Contains(".units.multiplier", client, StringComparison.Ordinal);
        Assert.Contains("floating-point", client, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveScl_WhileMonitoring_UsesCanonicalSnapshotWithoutStoppingReports()
    {
        var source = Read("MainWindow.xaml.cs");
        var handlerStart = source.IndexOf(
            "private async void IedSaveScl_Click",
            StringComparison.Ordinal);
        var handlerEnd = source.IndexOf(
            "private void SaveOpenedSclAsGenericEdition2",
            handlerStart,
            StringComparison.Ordinal);

        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);
        var handler = source[handlerStart..handlerEnd];

        Assert.Contains("if (device.IsMonitoring)", handler, StringComparison.Ordinal);
        Assert.Contains(
            "Save SCL uses the current canonical model and skips optional save-time value enrichment",
            handler,
            StringComparison.Ordinal);
        Assert.Contains("EnrichCanonicalForSclSaveAsync", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("StopDeviceMonitorAsync", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("StopDeviceConnectionAsync", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoveryStructuredDataSetMembers_UseExactSchemaLeavesWithoutRelaxingRuntimeSafety()
    {
        var inventory = Read("Services/Iec61850DataSetSignalInventoryService.cs");
        var projection = Read("Services/SchemaSafeAggregateProjectionService.cs");
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");

        Assert.Contains("TryResolveStaticDataSetPrimaryLeaf", inventory, StringComparison.Ordinal);
        Assert.Contains("ResolvedFromExactSchema", inventory, StringComparison.Ordinal);
        Assert.Contains(".cval.mag.f", projection, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".instcval.mag.f", projection, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "The authoritative schema contains none of the approved exact magnitude references",
            projection,
            StringComparison.Ordinal);

        // The fix must make previously unresolved rows publishable by giving them an exact
        // runtime leaf; it must not bypass the existing process-value safety boundary.
        Assert.Contains(
            ".Where(signal => signal.IsSelected && signal.CanPublishToRuntime)",
            runtime,
            StringComparison.Ordinal);
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
    public void StaticReporting_UsesEngineCanonicalHotPath_AndCanonicalMemberOrder()
    {
        var source = Read("Services/NativeIec61850Client.StaticDataSetReporting.cs");

        Assert.Contains("PrepareCanonicalStaticAcquisitionSmartAsync", source, StringComparison.Ordinal);
        Assert.Contains("MmsConfiguredStaticRcbEligibilityPolicy.Evaluate", source, StringComparison.Ordinal);
        Assert.Contains("DirectoryFromAvailability", source, StringComparison.Ordinal);
        Assert.Contains("TryVerifyStaticDataSetMemberOrder", source, StringComparison.Ordinal);
        Assert.Contains("Members = modelDirectory.Members", source, StringComparison.Ordinal);
        Assert.DoesNotContain("discovery.DataSetDirectories.SingleOrDefault", source, StringComparison.Ordinal);
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
    public void DiscoveryAndOpenScl_StaticPlanning_EnterOneEngineAcquisitionContract()
    {
        var canonical = Read("Services/NativeIec61850Client.CanonicalAcquisition.cs");
        var staticReporting = Read("Services/NativeIec61850Client.StaticDataSetReporting.cs");

        Assert.Contains("CanonicalLiveModelAdapter.FromLiveDiscovery", canonical, StringComparison.Ordinal);
        Assert.Contains("CanonicalIngressKind.SclFile", canonical, StringComparison.Ordinal);
        Assert.Contains("BuildCanonicalStaticSelections", canonical, StringComparison.Ordinal);
        Assert.Contains("PrepareCanonicalStaticAcquisitionSmartAsync", staticReporting, StringComparison.Ordinal);
        Assert.DoesNotContain("Iec61850StaticRcbReferenceMatcher.MatchRank", staticReporting, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureDiscoveryForReportingAsync(cancellationToken)", staticReporting, StringComparison.Ordinal);
        Assert.Contains("StartConfiguredStaticReportMonitorAsync", staticReporting, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPersistentReportMonitorClientCompatibleAsync", staticReporting, StringComparison.Ordinal);
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

    [Fact]
    public void OpenScl_Uses_EngineOwned_Bounded_Association_Resolution_Without_Discovery_Fallback()
    {
        var preparation = Read("Services/SclAssistedConnectionPreparation.cs");
        Assert.Contains(
            "SclAssistedMmsAssociationCandidateResolver.Resolve",
            preparation,
            StringComparison.Ordinal);
        Assert.Contains("AssociationResolution = associationResolution", preparation, StringComparison.Ordinal);
        Assert.DoesNotContain("errors.AddRange(exactAssociation.Errors)", preparation, StringComparison.Ordinal);

        var client = Read("Services/NativeIec61850Client.SclAssisted.cs");
        var canonical = Read("Services/NativeIec61850Client.CanonicalAcquisition.cs");
        var start = client.IndexOf(
            "public async Task<SclAssistedClientConnectResult> ConnectUsingSclAsync",
            StringComparison.Ordinal);
        var end = client.IndexOf(
            "private static IReadOnlyList<ArMms.MmsDataSetDirectoryResult> BuildModelDataSetDirectories",
            start,
            StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        var connectFlow = client[start..end];

        Assert.Contains("preparation.AssociationResolution", connectFlow, StringComparison.Ordinal);
        Assert.Contains("_session.ConnectSclAssistedAsync", connectFlow, StringComparison.Ordinal);
        Assert.Contains("TryInstallCanonicalSclRuntimeModel", connectFlow, StringComparison.Ordinal);
        Assert.Contains("MmsCanonicalReportInventoryProjection.Build", connectFlow, StringComparison.Ordinal);
        Assert.Contains("online.SelectedAssociationCandidateName", connectFlow, StringComparison.Ordinal);
        Assert.Contains("online.AssociationAttemptCount", connectFlow, StringComparison.Ordinal);
        Assert.DoesNotContain(".DiscoverAsync(", connectFlow, StringComparison.Ordinal);
        Assert.DoesNotContain("DiscoverSignals", connectFlow, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildTrustedSclReportInventory", client, StringComparison.Ordinal);
        Assert.DoesNotContain("ConcreteFirstStaticRcbReference", client, StringComparison.Ordinal);
        Assert.Contains("SclCanonicalImporter.Import", canonical, StringComparison.Ordinal);
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
