namespace ARSAS.Tests;

public sealed class FastWorkflowRegressionTests
{
    [Fact]
    public void VisibleGooseCapabilityPill_OpensCorrectTabAndArmsOneShotAdapterAutoStart()
    {
        var card = File.ReadAllText(FindRepoFile("FaultRecordUxBehavior.cs"));
        var quick = File.ReadAllText(FindRepoFile("MainWindow.IedGooseQuickStart.cs"));
        var subscriber = File.ReadAllText(FindRepoFile("MainWindow.GooseSubscriber.cs"));
        var mainView = File.ReadAllText(FindRepoFile("MainWindow.xaml"));

        Assert.Contains("mainWindow.OpenIedGooseSubscriber(device)", card, StringComparison.Ordinal);
        Assert.DoesNotContain("tabs.SelectedIndex = 3", card, StringComparison.Ordinal);
        Assert.Contains("GooseSubscriberTabIndex = 4", quick, StringComparison.Ordinal);
        Assert.Contains("MainTabs.SelectedIndex = GooseSubscriberTabIndex", quick, StringComparison.Ordinal);
        Assert.Contains("OpenIedGooseSubscriber(device)", quick, StringComparison.Ordinal);
        Assert.Contains("SelectedGooseAdapter = null;", quick, StringComparison.Ordinal);
        Assert.Contains("_pendingIedGooseAutoStart = device;", quick, StringComparison.Ordinal);
        Assert.Contains("StartGooseSubscriber_Click(this, new RoutedEventArgs())", subscriber, StringComparison.Ordinal);
        Assert.Contains("_pendingIedGooseAutoStart = null;", subscriber, StringComparison.Ordinal);
        Assert.Contains("!IsGooseCapturing && !GooseActionBusy", subscriber, StringComparison.Ordinal);
        Assert.Contains("Click=\"StartGooseSubscriber_Click\"", mainView, StringComparison.Ordinal);
        Assert.DoesNotContain("Fast IEC 61850 Control", mainView, StringComparison.Ordinal);
        Assert.DoesNotContain("Operate only the validated control objects selected for this IED.", mainView, StringComparison.Ordinal);
        Assert.DoesNotContain("Click=\"RefreshCommandValues_Click\"", mainView, StringComparison.Ordinal);
        Assert.Contains("SelectedDevice.CommandSignals", mainView, StringComparison.Ordinal);
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
