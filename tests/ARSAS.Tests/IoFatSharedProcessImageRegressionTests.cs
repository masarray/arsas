namespace ARSAS.Tests;

public sealed class IoFatSharedProcessImageRegressionTests
{
    [Fact]
    public void FatEvidence_UsesOneAtomicRawSnapshotRouteInsteadOfEngineeringUiTimer()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.P0FatSharedProcessEvidence.cs"));

        Assert.Contains("_runtime.PointUpdated -= Runtime_IoTestPointUpdated", source, StringComparison.Ordinal);
        Assert.Contains("_runtime.PointUpdated -= Runtime_IoTestAdditionalPointUpdated", source, StringComparison.Ordinal);
        Assert.Contains("_runtime.PointUpdated -= P0FatRuntimePointUpdated", source, StringComparison.Ordinal);
        Assert.Contains("_runtime.PointUpdated += P0FatAtomicPointUpdated", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.DataBind", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_uiFlushTimer.Tick +=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("P0FatSharedProcessEvidence_Tick", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AtomicRoute_CommitsLiveBeforePublishingEvidenceFromSameSnapshot()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.P0FatSharedProcessEvidence.cs"));

        var apply = source.IndexOf("ApplyP0FatSnapshot(plan.Runtime, snapshot)", StringComparison.Ordinal);
        var barrier = source.IndexOf("IsAtomicFatLiveCommitCurrent(plans, snapshot)", apply, StringComparison.Ordinal);
        var primary = source.IndexOf("coordinator.PrimaryController.Enqueue(entry)", barrier, StringComparison.Ordinal);
        var sibling = source.IndexOf("coordinator.EnqueueAdditional(entry)", primary, StringComparison.Ordinal);

        Assert.True(apply >= 0, "The exact runtime snapshot must first update LIVE VALUE.");
        Assert.True(barrier > apply, "The LIVE-bound runtime must be verified after commit.");
        Assert.True(primary > barrier, "Primary Value 1/2 evidence must be downstream of the LIVE commit.");
        Assert.True(sibling > primary, "Sibling evidence receives the same already-committed frame.");
        Assert.Contains("NewValue = snapshot.Value", source, StringComparison.Ordinal);
        Assert.Contains("DeviceTimestamp = snapshot.DeviceTimestamp", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AtomicCursor_IsPrimedBeforeStartAndIgnoresSequenceOnlyChurn()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.P0FatSharedProcessEvidence.cs"));

        Assert.Contains("if (!activeDeviceIds.Contains(point.DeviceId))", source, StringComparison.Ordinal);
        Assert.Contains("_p0FatSharedProcessCursors[key] = new StableFatProcessCursor", source, StringComparison.Ordinal);
        Assert.Contains("Iec61850MonitorPoint.AreSemanticallyEquivalent(previous.Value, snapshot.Value)", source, StringComparison.Ordinal);
        Assert.Contains("previous.Sequence != snapshot.Sequence", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ParallelEvidenceWiring_UsesSharedProcessRouteInsteadOfLegacyAdditionalObserver()
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
