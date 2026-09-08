namespace ARSAS.Tests;

public sealed class DeterministicStaticReportPathRegressionTests
{
    [Fact]
    public void StaticPath_BypassesAdaptiveHybridPlannerAndPolling()
    {
        var source = Read("Services/NativeIec61850Client.StaticDataSetReporting.cs");

        Assert.Contains("Deterministic Static DataSet configured-RCB path", source, StringComparison.Ordinal);
        Assert.Contains("var configurationModel = projectionModel", source, StringComparison.Ordinal);
        Assert.Contains("configurationModel.ReportControls", source, StringComparison.Ordinal);
        Assert.Contains("discovery.ReportInventory.ReportControls", source, StringComparison.Ordinal);
        Assert.Contains("GetDataSetDirectoriesAsync", source, StringComparison.Ordinal);
        Assert.Contains("MmsReportSubscriptionPlanStatus.ReadyRequiresWrite", source, StringComparison.Ordinal);
        Assert.Contains("triggerGeneralInterrogation: true", source, StringComparison.Ordinal);
        Assert.Contains("deleteDynamicDataSetOnStop: false", source, StringComparison.Ordinal);
        Assert.Contains("PollingPointKeys = Array.Empty<string>()", source, StringComparison.Ordinal);
        Assert.Contains("PollingFallbackSignalCount = 0", source, StringComparison.Ordinal);

        Assert.DoesNotContain("configurationModels", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MmsCapabilityAwareHybridReportAcquisitionPlanner", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildDynamicPlan", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DefineNamedVariableList", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadValueAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticPath_RequiresConfiguredRcbFamilyAndOrderedLiveDataSetDirectory()
    {
        var source = Read("Services/NativeIec61850Client.StaticDataSetReporting.cs");

        Assert.Contains("SameStaticReference(report.DataSetReference, dataSetGroup.Key)", source, StringComparison.Ordinal);
        Assert.Contains("Iec61850StaticRcbReferenceMatcher.MatchRank", source, StringComparison.Ordinal);
        Assert.Contains("arbitrary same-DataSet", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("live DataSet directory could not prove an ordered non-empty member list", source, StringComparison.Ordinal);
        Assert.Contains("directory.Members", source, StringComparison.Ordinal);
        Assert.Contains("No MMS process polling was substituted", source, StringComparison.Ordinal);
        Assert.Contains("authoritative configuration binds", source, StringComparison.Ordinal);
        Assert.Contains("live DatSet reports", source, StringComparison.Ordinal);
        Assert.Contains("ReportControlReference = concreteReportReference", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticPath_SclAuthorityCannotBeDisplacedByLivePeerRcb()
    {
        var source = Read("Services/NativeIec61850Client.StaticDataSetReporting.cs");

        Assert.Contains("device.SclWorkspace?.DesignModel ?? device.LiveDiscoveryModel", source, StringComparison.Ordinal);
        Assert.Contains("only ReportControls from that", source, StringComparison.Ordinal);
        Assert.Contains("design model may authorize static acquisition", source, StringComparison.Ordinal);
        Assert.Contains("var configurationModel = projectionModel", source, StringComparison.Ordinal);
        Assert.Contains("SelectMany(configured => discovery.ReportInventory.ReportControls", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectMany(configurationModel => configurationModel.ReportControls)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var configured = configuredReports[0]", source, StringComparison.Ordinal);
        Assert.Contains("remained authoritative", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IndexedRcbFamily_PrefersNonOccupiedConcreteInstance()
    {
        var source = Read("Services/NativeIec61850Client.StaticDataSetReporting.cs");

        Assert.Contains("matchedLiveCandidates", source, StringComparison.Ordinal);
        Assert.Contains("evaluatedLiveCandidates", source, StringComparison.Ordinal);
        Assert.Contains("MmsRcbAvailabilityEvaluator.Evaluate", source, StringComparison.Ordinal);
        Assert.Contains("callerOwnedRcbReferences", source, StringComparison.Ordinal);
        Assert.Contains("MmsReportSubscriptionPlanner.IsExplicitlyEnabled(item.Candidate)", source, StringComparison.Ordinal);
        Assert.Contains("MmsReportSubscriptionPlanner.IsReservedByOtherClient(item.Candidate)", source, StringComparison.Ordinal);
        Assert.Contains("MmsReportSubscriptionPlanner.IsExplicitlyDisabled(item.Candidate)", source, StringComparison.Ordinal);
        Assert.Contains("StaticRcbAvailabilityRank", source, StringComparison.Ordinal);
        Assert.Contains("Owner={item.Candidate.Owner}", source, StringComparison.Ordinal);
        Assert.Contains("every concrete instance was explicitly unavailable/in-use", source, StringComparison.Ordinal);
        Assert.Contains("Static mode will not steal an occupied RCB", source, StringComparison.Ordinal);
        Assert.Contains("prefer exact identity, caller-owned/known-safe state", source, StringComparison.Ordinal);
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
