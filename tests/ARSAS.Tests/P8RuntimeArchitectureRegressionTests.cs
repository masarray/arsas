namespace ARSAS.Tests;

public sealed class P8RuntimeArchitectureRegressionTests
{
    [Fact]
    public void IecRuntimeFacade_IsolatesLifecycleOperationsPerIed_OffDispatcherThread()
    {
        var source = Read("Services/UiResponsiveIec61850MonitorRuntimeFacade.cs");

        Assert.Contains("ConcurrentDictionary<string, DeviceOperationSlot>", source, StringComparison.Ordinal);
        Assert.Contains("OperationGate", source, StringComparison.Ordinal);
        Assert.Contains("StopGate", source, StringComparison.Ordinal);
        Assert.Contains("RunPreemptiveStopAsync", source, StringComparison.Ordinal);
        Assert.Contains("Task.Run(", source, StringComparison.Ordinal);
        Assert.Contains("ActiveOperationCancellation", source, StringComparison.Ordinal);
        Assert.Contains("Generation", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitoringRuntime_KeepsReconnectStateInsideEachDeviceSession()
    {
        var source = Read("Services/Iec61850MonitorRuntime.cs");

        Assert.Contains("ConcurrentDictionary<string, DeviceSession>", source, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource MonitorCancellation", source, StringComparison.Ordinal);
        Assert.Contains("DateTime NextReconnectUtc", source, StringComparison.Ordinal);
        Assert.Contains("int ConsecutiveReconnectFailures", source, StringComparison.Ordinal);
        Assert.Contains("TryReconnectAsync(DeviceSession session", source, StringComparison.Ordinal);
        Assert.Contains("SmartReconnectPolicy.GetRetryDelay(attempt)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_CoalescesOnlyPointProjection_WhileSoeRemainsQueuedLosslessly()
    {
        var source = Read("MainWindow.xaml.cs");

        Assert.Contains("ConcurrentDictionary<string, PendingPointUpdate> _pendingPointSnapshots", source, StringComparison.Ordinal);
        Assert.Contains("ConcurrentQueue<Iec61850EventEntry> _pendingEvents", source, StringComparison.Ordinal);
        Assert.Contains("Interval = TimeSpan.FromMilliseconds(200)", source, StringComparison.Ordinal);
        Assert.Contains("_pendingPointSnapshots.AddOrUpdate", source, StringComparison.Ordinal);
        Assert.Contains("_pendingEvents.Enqueue(entry)", source, StringComparison.Ordinal);
        Assert.Contains("while (eventBatch.Count < 1000 && _pendingEvents.TryDequeue", source, StringComparison.Ordinal);

        Assert.False(File.Exists(Path.Combine(FindRepoRoot(), "Services", "LatestValueUiBatcher.cs")));
    }

    [Fact]
    public void FatGrid_UsesRowAndColumnRecyclingVirtualization()
    {
        var source = Read("IoListTestingWindow.xaml");

        Assert.Contains("EnableRowVirtualization=\"True\"", source, StringComparison.Ordinal);
        Assert.Contains("EnableColumnVirtualization=\"True\"", source, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", source, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", source, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.CanContentScroll=\"True\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Shutdown_IsBounded_AndRuntimeOwnsAsyncDisposal()
    {
        var mainWindow = Read("MainWindow.xaml.cs");
        var facade = Read("Services/UiResponsiveIec61850MonitorRuntimeFacade.cs");

        Assert.Contains("_uiFlushTimer.Stop();", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_progressAnimationTimer.Stop();", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_applicationCancellation.Cancel();", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_runtime.DisposeAsync().AsTask()", mainWindow, StringComparison.Ordinal);
        Assert.Contains("DisposeBudget = TimeSpan.FromSeconds(3)", facade, StringComparison.Ordinal);
        Assert.Contains("activeCancellation?.Cancel();", facade, StringComparison.Ordinal);
        Assert.Contains("_deviceSlots.Clear();", facade, StringComparison.Ordinal);
    }

    [Fact]
    public void DefensiveTelemetry_DoesNotReplaceMissingRelayTimeWithPcTime()
    {
        var source = Read("Services/Iec61850TelemetryEnvelope.cs");

        Assert.Contains("DateTimeOffset? SourceTimestampUtc", source, StringComparison.Ordinal);
        Assert.Contains("DateTimeOffset ReceivedAtUtc", source, StringComparison.Ordinal);
        Assert.Contains("source timestamp remains unknown", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never replaced by ReceivedAtUtc/PC time", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceTimestampUtc = DateTime", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P8_DoesNotIntroduceSpeculativePoolingIntoAmbiguousOwnershipPaths()
    {
        var nativeClient = Read("Services/NativeIec61850Client.cs");
        var monitor = Read("Services/Iec61850MonitorRuntime.cs");
        var goose = Read("Services/GooseSubscriberRuntime.cs");

        Assert.DoesNotContain("ArrayPool<", nativeClient, StringComparison.Ordinal);
        Assert.DoesNotContain("ArrayPool<", monitor, StringComparison.Ordinal);
        Assert.DoesNotContain("ArrayPool<", goose, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(FindRepoRoot(), "Services", "PooledByteBufferLease.cs")));
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MainWindow.xaml")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tests", "ARSAS.Tests")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
