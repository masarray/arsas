namespace ARSAS.Tests;

public sealed class DeterministicStaticReportPathRegressionTests
{
    [Fact]
    public void StaticPath_UsesEngineCanonicalHotPath_AndNeverEnablesPolling()
    {
        var source = Read("Services/NativeIec61850Client.StaticDataSetReporting.cs");

        Assert.Contains("PrepareCanonicalStaticAcquisitionSmartAsync", source, StringComparison.Ordinal);
        Assert.Contains("MmsConfiguredStaticRcbEligibilityPolicy.Evaluate", source, StringComparison.Ordinal);
        Assert.Contains("BuildModelDataSetDirectories", source, StringComparison.Ordinal);
        Assert.Contains("DirectoryFromAvailability", source, StringComparison.Ordinal);
        Assert.Contains("TryVerifyStaticDataSetMemberOrder", source, StringComparison.Ordinal);
        Assert.Contains("MmsReportSubscriptionPlanStatus.ReadyRequiresWrite", source, StringComparison.Ordinal);
        Assert.Contains("StartConfiguredStaticReportMonitorAsync", source, StringComparison.Ordinal);
        Assert.Contains("triggerGeneralInterrogation: true", source, StringComparison.Ordinal);
        Assert.Contains("PollingPointKeys = Array.Empty<string>()", source, StringComparison.Ordinal);
        Assert.Contains("PollingFallbackSignalCount = 0", source, StringComparison.Ordinal);

        Assert.DoesNotContain("EnsureDiscoveryForReportingAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Iec61850StaticRcbReferenceMatcher.MatchRank", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MmsCapabilityAwareHybridReportAcquisitionPlanner", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildDynamicPlan", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DefineNamedVariableList", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadValueAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPersistentReportMonitorClientCompatibleAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UnifiedConfiguredStaticPath_RequestsOneShotGi_WhileSafeTrialRemainsReadOnly()
    {
        var staticReporting = Read("Services/NativeIec61850Client.StaticDataSetReporting.cs");
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");
        var safeTrial = Read("Services/SclSafeTrialRunner.cs");

        Assert.Contains("StartConfiguredStaticReportMonitorAsync", staticReporting, StringComparison.Ordinal);
        Assert.Contains("triggerGeneralInterrogation: true", staticReporting, StringComparison.Ordinal);
        Assert.DoesNotContain("StartTrustedSclStaticReportMonitorAsync", runtime, StringComparison.Ordinal);

        Assert.Contains("readOnly = true", safeTrial, StringComparison.Ordinal);
        Assert.Contains("writesAllowed = false", safeTrial, StringComparison.Ordinal);
        Assert.Contains("reportEnableAllowed = false", safeTrial, StringComparison.Ordinal);
        Assert.Contains("dynamicDataSetAllowed = false", safeTrial, StringComparison.Ordinal);
        Assert.DoesNotContain("StartConfiguredStaticReportMonitorAsync", safeTrial, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticPath_RequiresEngineProvenExactRcb_AndCanonicalLiveMemberOrder()
    {
        var source = Read("Services/NativeIec61850Client.StaticDataSetReporting.cs");

        Assert.Contains("smart.Acquisition.Plan.TargetResolution.Segments", source, StringComparison.Ordinal);
        Assert.Contains("ExactLiveReportControlReferences", source, StringComparison.Ordinal);
        Assert.Contains("MmsConfiguredStaticRcbEligibilityPolicy.Evaluate", source, StringComparison.Ordinal);
        Assert.Contains("canonical model has no ordered DataSet members", source, StringComparison.Ordinal);
        Assert.Contains("targeted live availability did not prove a populated DataSet directory", source, StringComparison.Ordinal);
        Assert.Contains("canonical/live DataSet member order mismatch", source, StringComparison.Ordinal);
        Assert.Contains("Members = modelDirectory.Members", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Members = liveDirectory.Members", source, StringComparison.Ordinal);
        Assert.Contains("ReportControlReference = selected.Snapshot.Reference", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Iec61850StaticRcbReferenceMatcher.MatchRank", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticPath_ConsumesTargetedAvailabilityDirectoryEvidence_WithoutRediscovery()
    {
        var source = Read("Services/NativeIec61850Client.StaticDataSetReporting.cs");

        Assert.Contains("DirectoryFromAvailability(selected.Snapshot)", source, StringComparison.Ordinal);
        Assert.Contains("Members = modelDirectory.Members", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetDataSetDirectoriesAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureDiscoveryForReportingAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Members = liveDirectory.Members", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticPath_SclAuthorityConvergesThroughCanonicalCoverage_NotLivePeerSubstitution()
    {
        var source = Read("Services/NativeIec61850Client.StaticDataSetReporting.cs");
        var canonical = Read("Services/NativeIec61850Client.CanonicalAcquisition.cs");

        Assert.Contains("device.SclWorkspace?.DesignModel ?? device.LiveDiscoveryModel", source, StringComparison.Ordinal);
        Assert.Contains("ResolveCanonicalRuntimeModel", source, StringComparison.Ordinal);
        Assert.Contains("CanonicalIngressKind.SclFile", canonical, StringComparison.Ordinal);
        Assert.Contains("PrepareCanonicalStaticAcquisitionSmartAsync", source, StringComparison.Ordinal);
        Assert.Contains("smart.Coverage.Segments", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectMany(configured => discovery.ReportInventory.ReportControls", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Iec61850StaticRcbReferenceMatcher.MatchRank", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConcreteRcbSelection_IsEngineEvidenceDriven_AndNeverNameFabricated()
    {
        var source = Read("Services/NativeIec61850Client.StaticDataSetReporting.cs");
        var sclClient = Read("Services/NativeIec61850Client.SclAssisted.cs");

        Assert.Contains("callerOwnedRcbReferences", source, StringComparison.Ordinal);
        Assert.Contains("ExactLiveReportControlReferences", source, StringComparison.Ordinal);
        Assert.Contains("MmsConfiguredStaticRcbEligibilityPolicy.Evaluate", source, StringComparison.Ordinal);
        Assert.Contains("MmsConfiguredStaticRcbEligibilityKind.CallerOwned", source, StringComparison.Ordinal);
        Assert.Contains("MmsConfiguredStaticRcbEligibilityKind.ExplicitFree", source, StringComparison.Ordinal);
        Assert.Contains("MmsConfiguredStaticRcbEligibilityKind.ReducedMissingReservationEvidence", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StaticRcbAvailabilityRank", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConcreteFirstStaticRcbReference", sclClient, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildTrustedSclReportInventory", sclClient, StringComparison.Ordinal);
    }

    [Fact]
    public void HybridEntryPoints_RouteStaticModeToDeterministicPath()
    {
        var hybrid = Read("Services/NativeIec61850Client.HybridReporting.cs");

        Assert.Contains("Iec61850MonitoringModeRegistry.IsStaticDataSetReportOnly(device)", hybrid, StringComparison.Ordinal);
        Assert.Contains("BuildStaticDataSetReportPlansAsync", hybrid, StringComparison.Ordinal);
        Assert.Contains("_deterministicStaticSubscriptions.ContainsKey(plan.PlanId)", hybrid, StringComparison.Ordinal);
        Assert.Contains("StartStaticDataSetReportMonitorAsync", hybrid, StringComparison.Ordinal);
        Assert.DoesNotContain("Iec61850MonitoringModeRegistry.IsStaticDataSetReportOnly(device.DeviceId)", hybrid, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualHybridPlanner_RemainsAvailableOutsideStaticRoute()
    {
        var hybrid = Read("Services/NativeIec61850Client.HybridReporting.cs");

        Assert.Contains("MmsCapabilityAwareHybridReportAcquisitionPlanner.Build", hybrid, StringComparison.Ordinal);
        Assert.Contains("AllowDynamicBrcb", hybrid, StringComparison.Ordinal);
        Assert.Contains("AllowPollingFallback = true", hybrid, StringComparison.Ordinal);
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
