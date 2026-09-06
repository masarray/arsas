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
