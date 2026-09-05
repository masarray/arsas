namespace ARSAS.Tests;

public sealed class DeterministicStaticReportPathRegressionTests
{
    [Fact]
    public void StaticPath_BypassesAdaptiveHybridPlannerAndPolling()
    {
        var source = Read("Services/NativeIec61850Client.StaticDataSetReporting.cs");

        Assert.Contains("Deterministic Static DataSet configured-RCB path", source, StringComparison.Ordinal);
        Assert.Contains("model.ReportControls", source, StringComparison.Ordinal);
        Assert.Contains("discovery.ReportInventory.ReportControls", source, StringComparison.Ordinal);
        Assert.Contains("GetDataSetDirectoriesAsync", source, StringComparison.Ordinal);
        Assert.Contains("MmsReportSubscriptionPlanStatus.ReadyRequiresWrite", source, StringComparison.Ordinal);
        Assert.Contains("triggerGeneralInterrogation: true", source, StringComparison.Ordinal);
        Assert.Contains("deleteDynamicDataSetOnStop: false", source, StringComparison.Ordinal);
        Assert.Contains("PollingPointKeys = Array.Empty<string>()", source, StringComparison.Ordinal);
        Assert.Contains("PollingFallbackSignalCount = 0", source, StringComparison.Ordinal);

        Assert.DoesNotContain("MmsCapabilityAwareHybridReportAcquisitionPlanner", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildDynamicPlan", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DefineNamedVariableList", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadValueAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticPath_RequiresExactConfiguredRcbAndOrderedLiveDataSetDirectory()
    {
        var source = Read("Services/NativeIec61850Client.StaticDataSetReporting.cs");

        Assert.Contains("SameStaticReference(report.DataSetReference, dataSetGroup.Key)", source, StringComparison.Ordinal);
        Assert.Contains("SameStaticReference(candidate.Reference, configured.Reference)", source, StringComparison.Ordinal);
        Assert.Contains("live DataSet directory could not prove an ordered non-empty member list", source, StringComparison.Ordinal);
        Assert.Contains("directory.Members", source, StringComparison.Ordinal);
        Assert.Contains("No MMS process polling was substituted", source, StringComparison.Ordinal);
        Assert.Contains("SCL binds", source, StringComparison.Ordinal);
        Assert.Contains("live DatSet reports", source, StringComparison.Ordinal);
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
