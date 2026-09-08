namespace ARSAS.Tests;

public sealed class IoFatSharedProcessImageRegressionTests
{
    [Fact]
    public void FatEvidence_DetachesRawRuntimeObserversAndSamplesEngineeringUiImage()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.P0FatSharedProcessEvidence.cs"));

        Assert.Contains("_runtime.PointUpdated -= Runtime_IoTestPointUpdated", source, StringComparison.Ordinal);
        Assert.Contains("_runtime.PointUpdated -= Runtime_IoTestAdditionalPointUpdated", source, StringComparison.Ordinal);
        Assert.Contains("_runtime.PointUpdated -= P0FatRuntimePointUpdated", source, StringComparison.Ordinal);
        Assert.Contains("_uiFlushTimer.Tick += P0FatSharedProcessEvidence_Tick", source, StringComparison.Ordinal);
        Assert.Contains("device.Points", source, StringComparison.Ordinal);
        Assert.Contains("ProjectSharedEngineeringPointToFat", source, StringComparison.Ordinal);
        Assert.Contains("IsFatLiveCommitCurrent(committed)", source, StringComparison.Ordinal);
        Assert.Contains("coordinator.PrimaryController.Enqueue(committed.Entry)", source, StringComparison.Ordinal);
        Assert.Contains("coordinator.EnqueueAdditional(committed.Entry)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedImageCursor_IsPrimedBeforeStartAndIgnoresSequenceOnlyChurn()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.P0FatSharedProcessEvidence.cs"));

        Assert.Contains("if (!activeDeviceIds.Contains(device.DeviceId))", source, StringComparison.Ordinal);
        Assert.Contains("_p0FatSharedProcessCursors[key] = new StableFatProcessCursor", source, StringComparison.Ordinal);
        Assert.Contains("Iec61850MonitorPoint.AreSemanticallyEquivalent(previous.Value, point.Value)", source, StringComparison.Ordinal);
        Assert.Contains("Sequence-only transport churn", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidencePublication_UsesMonotonicLiveCommitBarrierWithoutDispatcherRace()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.P0FatSharedProcessEvidence.cs"));

        var liveProjection = source.IndexOf("ProjectSharedEngineeringPointToFat(pointIndex, point)", StringComparison.Ordinal);
        Assert.True(liveProjection >= 0, "Shared LIVE projection must exist.");

        var publicationEpoch = source.IndexOf(
            "var processSequence = Interlocked.Increment(ref _p0FatVisiblePublicationSequence)",
            liveProjection,
            StringComparison.Ordinal);
        Assert.True(publicationEpoch > liveProjection, "A publication epoch must be allocated only after LIVE projection.");

        var visibleSequenceCommit = source.IndexOf(
            "_p0FatLiveVisibleSequences[plan.TestPointId] = processSequence",
            publicationEpoch,
            StringComparison.Ordinal);
        Assert.True(visibleSequenceCommit > publicationEpoch, "Every mapped LIVE row must receive the publication epoch before evidence is queued.");

        var liveBarrier = source.IndexOf("if (!IsFatLiveCommitCurrent(committed))", visibleSequenceCommit, StringComparison.Ordinal);
        Assert.True(liveBarrier > visibleSequenceCommit, "Evidence must pass the committed LIVE barrier after visible sequence publication.");

        var evidenceEnqueue = source.IndexOf("coordinator.PrimaryController.Enqueue(committed.Entry)", liveBarrier, StringComparison.Ordinal);
        Assert.True(evidenceEnqueue > liveBarrier, "Value 1/2 enqueue must occur only after the LIVE commit barrier succeeds.");

        Assert.Contains("CanPublishEvidenceForTest(liveVisibleSequence, committed.ProcessSequence)", source, StringComparison.Ordinal);
        Assert.Contains("liveVisibleSequence >= evidenceProcessSequence", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherPriority.Background", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ParallelEvidenceWiring_UsesSharedProcessRouteInsteadOfRawPointSubscription()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.MultiSessionEvidence.cs"));

        Assert.Contains("AttachIoFatSharedProcessEvidenceRoute(coordinator)", source, StringComparison.Ordinal);
        Assert.Contains("DetachIoFatSharedProcessEvidenceRoute(coordinator)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_runtime.PointUpdated += Runtime_IoTestAdditionalPointUpdated;\n        _runtime.PointUpdated += Runtime_IoTestAdditionalPointUpdated;",
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
        throw new FileNotFoundException(relativePath);
    }
}
