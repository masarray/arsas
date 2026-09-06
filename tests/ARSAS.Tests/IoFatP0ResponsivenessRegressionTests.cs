namespace ARSAS.Tests;

public sealed class IoFatP0ResponsivenessRegressionTests
{
    [Fact]
    public void ResumeAndStop_KeepWindowInteractive_AndMoveDiskBarriersOffHotPath()
    {
        var actions = Read("IoListTestingWindow.P0RuntimeActions.cs");
        var journal = Read("Services/IoTesting/IoTestEvidenceJournal.cs");

        Assert.Contains("await Dispatcher.Yield(DispatcherPriority.Render)", actions, StringComparison.Ordinal);
        Assert.Contains("BeginCoalescedVisibleFlushScope", actions, StringComparison.Ordinal);
        Assert.Contains("result = Session.Resume()", actions, StringComparison.Ordinal);
        Assert.Contains("BeginDeferredSealScope", actions, StringComparison.Ordinal);
        Assert.Contains("result = Session.Stop()", actions, StringComparison.Ordinal);
        Assert.Contains("await IoTestEvidenceJournal.AwaitDeferredSealsAsync()", actions, StringComparison.Ordinal);
        Assert.Contains("await Task.Run(Storage.SaveNow)", actions, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled = false", actions, StringComparison.Ordinal);

        Assert.Contains("FlushVisibleOrDefer", journal, StringComparison.Ordinal);
        Assert.Contains("state.Journals.Add(this)", journal, StringComparison.Ordinal);
        Assert.Contains("journal.FlushVisibleFromScope()", journal, StringComparison.Ordinal);
        Assert.Contains("Task.Run(SealDurablyAndVerify)", journal, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveProjection_ReusesCachedIndex_InsteadOfScanningFatPlanPerFrame()
    {
        var projection = Read("MainWindow.P0FatRecovery.cs");

        Assert.Contains("GetP0FatPointIndex(fat.Project)", projection, StringComparison.Ordinal);
        Assert.Contains("ComputeP0FatPointIndexVersion", projection, StringComparison.Ordinal);
        Assert.Contains("_p0FatPointIndexVersion == version", projection, StringComparison.Ordinal);
        Assert.Contains("forceRebuild: true", projection, StringComparison.Ordinal);
        Assert.Contains("Live projection index rebuilt", projection, StringComparison.Ordinal);
    }

    [Fact]
    public void FatIedCard_TracksEngineeringConnectionAndMonitoringStateWithoutNewMmsPolling()
    {
        var health = Read("MainWindow.IoFatConnectionHealth.cs");

        Assert.Contains("nameof(Iec61850MonitorDevice.IsConnected)", health, StringComparison.Ordinal);
        Assert.Contains("nameof(Iec61850MonitorDevice.IsMonitoring)", health, StringComparison.Ordinal);
        Assert.Contains("nameof(Iec61850MonitorDevice.Status)", health, StringComparison.Ordinal);
        Assert.Contains("ied.ApplyLiveDeviceBinding(", health, StringComparison.Ordinal);
        Assert.Contains("device.IsConnected,", health, StringComparison.Ordinal);
        Assert.Contains("device.IsMonitoring);", health, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", health, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectAsync", health, StringComparison.Ordinal);
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
