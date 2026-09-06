namespace ARSAS.Tests;

public sealed class IoFatP0ResponsivenessRegressionTests
{
    [Fact]
    public void StartResumeAndStop_KeepWindowInteractive_AndMoveDiskBarriersOffHotPath()
    {
        var actions = Read("IoListTestingWindow.P0RuntimeActions.cs");
        var journal = Read("Services/IoTesting/IoTestEvidenceJournal.cs");

        Assert.Contains("FindButtonByContentBindingPath(this, nameof(SelectedStartWorkflowText))", actions, StringComparison.Ordinal);
        Assert.Contains("await Dispatcher.Yield(DispatcherPriority.Render)", actions, StringComparison.Ordinal);
        Assert.Contains("StartSelectedIedSafely_Click(sender, e)", actions, StringComparison.Ordinal);
        Assert.Contains("WaitForP0StartWorkflowCompletionAsync", actions, StringComparison.Ordinal);
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

    [Fact]
    public void CommandBridge_EmitsLatencyDiagnostics_WithoutCreatingAnotherControlStack()
    {
        var bridge = Read("MainWindow.IoFatCommandBridge.cs");

        Assert.Contains("Stopwatch.StartNew()", bridge, StringComparison.Ordinal);
        Assert.Contains("Command completed in", bridge, StringComparison.Ordinal);
        Assert.Contains("Command values refresh completed in", bridge, StringComparison.Ordinal);
        Assert.Contains("await ExecuteClaimedControlAsync(signal, claim)", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("new NativeIec61850Client", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandFeedbackFreshnessFence_PreventsValueFlickerAndPhantomSoe_WithoutHidingReports()
    {
        var facade = Read("Services/UiResponsiveIec61850MonitorRuntimeFacade.cs");

        Assert.Contains("CommandFeedbackFreshnessWindow = TimeSpan.FromSeconds(2)", facade, StringComparison.Ordinal);
        Assert.Contains("PendingEventOriginWindow = TimeSpan.FromSeconds(1)", facade, StringComparison.Ordinal);
        Assert.Contains("_inner.PointUpdated += ForwardPointUpdate", facade, StringComparison.Ordinal);
        Assert.Contains("_inner.EventRaised += ForwardEventRaised", facade, StringComparison.Ordinal);
        Assert.Contains("IsConfirmedCommandFeedback(snapshot)", facade, StringComparison.Ordinal);
        Assert.Contains("_commandFeedbackFences[key] = new CommandFeedbackFence", facade, StringComparison.Ordinal);
        Assert.Contains("_pendingEventOrigins[key] = new PendingEventOrigin", facade, StringComparison.Ordinal);
        Assert.Contains("snapshot.IsReportTraffic", facade, StringComparison.Ordinal);
        Assert.Contains("ForwardEventRaised(Iec61850EventEntry entry)", facade, StringComparison.Ordinal);
        Assert.Contains("TakePendingEventOrigin(key, entry.NewValue)", facade, StringComparison.Ordinal);
        Assert.Contains("origin is { IsConfirmedCommandFeedback: true } && matchesConfirmed", facade, StringComparison.Ordinal);
        Assert.Contains("origin is { IsReportTraffic: true }", facade, StringComparison.Ordinal);
        Assert.Contains("suppressed duplicate report SOE", facade, StringComparison.Ordinal);
        Assert.Contains("withheld phantom MMS verification SOE", facade, StringComparison.Ordinal);
        Assert.Contains("P0_COMMAND_FRESHNESS: ", facade, StringComparison.Ordinal);
        Assert.Contains("ClearCommandFeedbackState(deviceId)", facade, StringComparison.Ordinal);
        Assert.Contains("Report traffic remains process", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("IsReportEvent(Iec61850EventEntry entry)", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("using System.Windows", facade, StringComparison.Ordinal);
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
