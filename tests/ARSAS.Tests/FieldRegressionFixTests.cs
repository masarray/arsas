using ArIED61850Tester;

namespace ARSAS.Tests;

public sealed class FieldRegressionFixTests
{
    [Fact]
    public void IedTimestamp_LiveDisplayRoundsToMilliseconds_WhileFullSourceRemainsAvailable()
    {
        const string full = "2026-08-15 04:48:33.6407808";

        Assert.Equal(
            "2026-08-15 04:48:33.641",
            Iec61850TimestampPresentation.FormatMilliseconds(full));
        Assert.Equal("2026-08-15 04:48:33.6407808", full);

        var source = File.ReadAllText(FindRepoFile("MainWindow.FieldPresentationFix.cs"));
        Assert.Contains("RoundedIedTimestampConverter", source, StringComparison.Ordinal);
        Assert.Contains("Iec61850TimestampPresentation.FormatMilliseconds", source, StringComparison.Ordinal);
        Assert.Contains("FullIedTimestampTooltipConverter", source, StringComparison.Ordinal);
        Assert.Contains("yyyy-MM-dd HH:mm:ss.fffffff", source, StringComparison.Ordinal);
        Assert.Contains("new Binding(\"DeviceTimestamp\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandPanel_DarkHeaderForcesReadableWhiteCaptions()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.FieldPresentationFix.cs"));

        Assert.Contains("CommandPanelExpander", source, StringComparison.Ordinal);
        Assert.Contains("expander.Foreground = Brushes.White", source, StringComparison.Ordinal);
        Assert.Contains("text.Foreground = Brushes.White", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TopBar_ParentContainersCannotClipResponsiveNavigation()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.TopBarContainerHardening.cs"));

        Assert.Contains("root.ClipToBounds = false", source, StringComparison.Ordinal);
        Assert.Contains("header.ClipToBounds = false", source, StringComparison.Ordinal);
        Assert.Contains("header.MinHeight = 68d", source, StringComparison.Ordinal);
        Assert.Contains("shell.Height = 64d", source, StringComparison.Ordinal);
        Assert.Contains("shell.ClipToBounds = false", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.ContextIdle", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SclFastConnect_UsesTypedDesignModelForHybridCatalog_ButKeepsFreshLiveRcbValidation()
    {
        var source = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.HybridReporting.cs"));

        Assert.Contains(
            "device?.LiveDiscoveryModel ?? device?.SclWorkspace?.DesignModel",
            source,
            StringComparison.Ordinal);
        Assert.Contains("ResolveHybridPlanningModel(device) is not null", source, StringComparison.Ordinal);
        Assert.Contains("Iec61850SignalCatalogBuilder.Build(planningModel)", source, StringComparison.Ordinal);
        Assert.Contains("EnsureDiscoveryForReportingAsync(cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("CheckReportControlAvailabilityAsync", source, StringComparison.Ordinal);
        Assert.Contains("RequireExactAvailabilityEvidence = true", source, StringComparison.Ordinal);
        Assert.Contains("fresh engine evidence", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CanUseHybridReportPlanner(Iec61850MonitorDevice device)\n        => device?.LiveDiscoveryModel is not null",
            source,
            StringComparison.Ordinal);
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
