namespace ARSAS.Tests;

public sealed class FastWorkflowRegressionTests
{
    [Fact]
    public void IedCardQuickCapture_UsesSelectedEndpointRoute()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.IedGooseQuickStart.cs"));
        Assert.Contains("MainTabs.SelectedIndex = 3", source, StringComparison.Ordinal);
        Assert.Contains("ResolveLocalIpv4ForTarget", source, StringComparison.Ordinal);
        Assert.Contains("CaptureAdapterMatchesNetworkInterface", source, StringComparison.Ordinal);
        Assert.Contains("_gooseSubscriberRuntime.StartAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GooseAdapters.First()", source, StringComparison.Ordinal);
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
