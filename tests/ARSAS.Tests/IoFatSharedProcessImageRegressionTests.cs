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
        Assert.Contains("coordinator.PrimaryController.Enqueue(entry)", source, StringComparison.Ordinal);
        Assert.Contains("coordinator.EnqueueAdditional(entry)", source, StringComparison.Ordinal);
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
    public void EvidencePublication_IsDeferredBelowDataBindAfterLiveProjection()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.P0FatSharedProcessEvidence.cs"));

        var liveProjection = source.IndexOf("ProjectSharedEngineeringPointToFat(pointIndex, point)", StringComparison.Ordinal);
        var deferredPublication = source.IndexOf("DispatcherPriority.Background", StringComparison.Ordinal);
        var evidenceEnqueue = source.IndexOf("routeOwner.PrimaryController.Enqueue(entry)", StringComparison.Ordinal);

        Assert.True(liveProjection >= 0, "Shared LIVE projection must exist.");
        Assert.True(deferredPublication > liveProjection, "Evidence must be deferred only after the LIVE projection.");
        Assert.True(evidenceEnqueue > deferredPublication, "Value 1/2 enqueue must happen inside the deferred publication turn.");
        Assert.Contains("DataBind (8) and Render (7) both outrank Background (4)", source, StringComparison.Ordinal);
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
