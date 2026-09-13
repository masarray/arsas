using ArIED61850Tester;
using System.Windows.Controls;
using System.Windows.Media;

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
    public void CommandPanel_DarkHeaderKeepsTargetBadgeReadableAcrossRefreshes()
    {
        RunInSta(() =>
        {
            var title = new TextBlock { Text = "Command Dock" };
            var target = new TextBlock { Text = "TARGET · AA1EIF06R4" };
            var badge = new Border
            {
                Tag = "P0CommandTargetBadge",
                Background = Brushes.Black,
                BorderBrush = Brushes.Black,
                Child = target
            };
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(title);
            header.Children.Add(badge);
            var expander = new Expander { Header = header };

            MainWindowFieldPresentationFix.ApplyDarkCommandHeaderContrast(expander);
            MainWindowFieldPresentationFix.ApplyDarkCommandHeaderContrast(expander);

            Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(title.Foreground).Color);
            Assert.Equal(Color.FromRgb(0x58, 0x6B, 0x82), Assert.IsType<SolidColorBrush>(target.Foreground).Color);
            Assert.Equal(Color.FromRgb(0xF4, 0xF7, 0xFB), Assert.IsType<SolidColorBrush>(badge.Background).Color);
            Assert.Equal(Color.FromRgb(0xD6, 0xE0, 0xEC), Assert.IsType<SolidColorBrush>(badge.BorderBrush).Color);
        });
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
        Assert.Contains("fresh capability-aware engine evidence", source, StringComparison.Ordinal);
        Assert.Contains("MmsCapabilityAwareHybridReportAcquisitionPlanner.Build", source, StringComparison.Ordinal);
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

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "WPF command-header regression timed out.");
        Assert.Null(failure);
    }
}
