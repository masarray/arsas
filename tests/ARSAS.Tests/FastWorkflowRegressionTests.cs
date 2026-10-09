namespace ARSAS.Tests;

public sealed class FastWorkflowRegressionTests
{
    [Fact]
    public void IedCardGooseCta_OpensSubscriberAndWaitsForOperatorStart()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.IedGooseQuickStart.cs"));
        var subscriber = File.ReadAllText(FindRepoFile("MainWindow.GooseSubscriber.cs"));
        var view = File.ReadAllText(FindRepoFile("Views/GooseSubscriberView.xaml"));

        Assert.Contains("GooseSubscriberTabIndex = 4", source, StringComparison.Ordinal);
        Assert.Contains("MainTabs.SelectedIndex = GooseSubscriberTabIndex", source, StringComparison.Ordinal);
        Assert.Contains("UpdateNavigationVisuals(GooseSubscriberTabIndex", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MainTabs.SelectedIndex = 3", source, StringComparison.Ordinal);
        Assert.Contains("ResolveLocalIpv4ForTarget", source, StringComparison.Ordinal);
        Assert.Contains("CaptureAdapterMatchesNetworkInterface", source, StringComparison.Ordinal);
        Assert.Contains("SelectedGooseAdapter = adapter", source, StringComparison.Ordinal);
        Assert.Contains("if (IsGooseCapturing || GooseActionBusy)", source, StringComparison.Ordinal);
        Assert.Contains("press Start", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_gooseSubscriberRuntime.StartAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StopGooseSubscriberAsync()", source, StringComparison.Ordinal);
        Assert.Contains("usableAdapters.Length == 1 ? usableAdapters[0] : null", subscriber, StringComparison.Ordinal);
        Assert.Contains("Click=\"StartCapture_Click\"", view, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedGooseAdapter, Mode=TwoWay}\"", view, StringComparison.Ordinal);
    }

    [Fact]
    public void FatCardPreparationProgress_IsRealDeterminateSmoothedAndLowPriority()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.RealPreparationProgress.cs"));
        var engineering = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.Progress.cs"));
        Assert.Contains("progressBar.IsIndeterminate = false", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Background", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(100)", source, StringComparison.Ordinal);
        Assert.Contains("RefreshPreparationProgressBarCache", source, StringComparison.Ordinal);
        Assert.Contains("if (!hasActivePreparation)", source, StringComparison.Ordinal);
        Assert.Contains("AdvanceDisplay", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeSpan.FromMilliseconds(50)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RepeatBehavior", source, StringComparison.Ordinal);
        Assert.Contains("device.DiscoveryProgressPercent", engineering, StringComparison.Ordinal);
        Assert.Contains("LivePointReady", engineering, StringComparison.Ordinal);
        Assert.Contains("device.IsMonitoring", engineering, StringComparison.Ordinal);
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
