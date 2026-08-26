namespace ARSAS.Tests;

public sealed class EventDrivenSessionLiveMonitorRegressionTests
{
    [Fact]
    public void Annunciator_ReconcilesCurrentLiveSnapshot_WithoutCreatingSecondSoePath()
    {
        var alarm = File.ReadAllText(FindRepoFile("MainWindow.AlarmAnnunciator.cs"));
        var main = File.ReadAllText(FindRepoFile("MainWindow.xaml.cs"));

        Assert.Contains("ReconcileAnnunciatorFromLivePoint", alarm, StringComparison.Ordinal);
        Assert.Contains("item.InitializeFromPoint(point)", alarm, StringComparison.Ordinal);
        Assert.Contains("Waiting for live value", alarm, StringComparison.Ordinal);
        Assert.DoesNotContain("Waiting for live SOE", alarm, StringComparison.Ordinal);
        Assert.Contains("ReconcileAnnunciatorFromLivePoint(point);", main, StringComparison.Ordinal);
        Assert.DoesNotContain("Events.Add", alarm, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadValueAsync", alarm, StringComparison.Ordinal);
        Assert.DoesNotContain("StartDeviceAsync", alarm, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveMonitor_HasFullWidthGlobalSearch_AndNoDuplicateSummaryBadge()
    {
        var xaml = File.ReadAllText(FindRepoFile("MainWindow.xaml"));
        var behavior = File.ReadAllText(FindRepoFile("GridUxBehavior.cs"));
        var bridge = File.ReadAllText(FindRepoFile("MainWindow.GlobalLiveSearch.cs"));
        var section = Slice(xaml, "<!-- GLOBAL MULTI-IED LIVE MONITOR -->", "<!-- EVENT LOG -->");

        Assert.Contains("GlobalLiveSearchBox", section, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GlobalLiveGrid\"", section, StringComparison.Ordinal);
        Assert.Contains("GlobalLiveSearch_TextChanged", section, StringComparison.Ordinal);
        Assert.Contains("GlobalLiveSearchClear_Click", section, StringComparison.Ordinal);
        Assert.DoesNotContain("MonitoringInsightText", section, StringComparison.Ordinal);
        Assert.Contains("SetGlobalRapidSearch(GlobalLiveGrid", bridge, StringComparison.Ordinal);
        Assert.Contains("SearchQuery", behavior, StringComparison.Ordinal);
        Assert.Contains("FilterGlobalPoint(item, state.Filters, state.SearchQuery)", behavior, StringComparison.Ordinal);
        Assert.Contains("nameof(DataGridColumn.ActualWidth)", behavior, StringComparison.Ordinal);
        Assert.Contains("Source = column", behavior, StringComparison.Ordinal);
    }

    [Fact]
    public void SclMonitoring_UsesAriecHybridStaticAndGuardedDynamicReports_BeforeResidualPolling()
    {
        var bridge = File.ReadAllText(FindRepoFile(Path.Combine("Services", "NativeIec61850Client.HybridReporting.cs")));
        var guarded = File.ReadAllText(FindRepoFile(Path.Combine("Services", "NativeIec61850Client.HybridReporting.GuardedRuntime.cs")));
        var models = File.ReadAllText(FindRepoFile(Path.Combine("Models", "MonitorModels.cs")));

        Assert.Contains("AllowStaticBrcb = true", bridge, StringComparison.Ordinal);
        Assert.Contains("AllowStaticUrcb = true", bridge, StringComparison.Ordinal);
        Assert.Contains("var allowDynamicWrites = device.AllowDynamicDataSetWrites && !dynamicWriteCircuitOpen", bridge, StringComparison.Ordinal);
        Assert.Contains("AllowDynamicBrcb = allowDynamicWrites", bridge, StringComparison.Ordinal);
        Assert.Contains("AllowDynamicUrcb = allowDynamicWrites", bridge, StringComparison.Ordinal);
        Assert.Contains("DynamicWriteCircuitByDevice", bridge, StringComparison.Ordinal);
        Assert.Contains("TryLoadGuardedRuntimeContextAsync(device", bridge, StringComparison.Ordinal);
        Assert.Contains("BuildCapabilityPlanWithGuardedRuntime", bridge, StringComparison.Ordinal);
        Assert.Contains("MmsCapabilityAwareHybridReportAcquisitionPlanner.Build", guarded, StringComparison.Ordinal);
        Assert.Contains("MmsGuardedDynamicReportRuntimePlanner.Build", guarded, StringComparison.Ordinal);
        Assert.Contains("MmsHybridDynamicAttemptEvidenceBuilder.Build", bridge, StringComparison.Ordinal);
        Assert.Contains("_session.LastNegotiatedCapabilities", bridge, StringComparison.Ordinal);
        Assert.Contains("StartPersistentReportMonitorWithAttemptEvidenceAsync", bridge, StringComparison.Ordinal);
        Assert.Contains("TryStartDynamicRecoveryAfterStaticFailureP4Async", bridge, StringComparison.Ordinal);
        Assert.Contains("MmsPollingFallback", bridge, StringComparison.Ordinal);
        Assert.Contains("AllowDynamicDataSetWrites { get; set; } = true", models, StringComparison.Ordinal);
    }

    private static string Slice(string source, string start, string end)
    {
        var a = source.IndexOf(start, StringComparison.Ordinal);
        var b = source.IndexOf(end, StringComparison.Ordinal);
        Assert.True(a >= 0 && b > a);
        return source[a..b];
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
