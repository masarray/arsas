namespace ARSAS.Tests;

public sealed class ProductionFatP1CanonicalGridRegressionTests
{
    [Fact]
    public void P1A_NormalFatEntryBindsExactEngineeringPointCollection()
    {
        var gridSource = File.ReadAllText(FindRepoFile("MainWindow.NativeFatCanonicalGrid.cs"));
        var tabSource = File.ReadAllText(FindRepoFile("MainWindow.ProductionFatTab.cs"));

        Assert.Contains("_nativeFatCanonicalGrid.ItemsSource = device?.Points;", gridSource, StringComparison.Ordinal);
        Assert.Contains("BuildNativeFatCanonicalWorkspace", tabSource, StringComparison.Ordinal);
        Assert.Contains("BindNativeFatCanonicalRows();", tabSource, StringComparison.Ordinal);

        Assert.DoesNotContain("IoFatEngineeringWorkspaceProjectionService", gridSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new IoTestPointPlan", gridSource, StringComparison.Ordinal);
        Assert.DoesNotContain("QueueProductionFatEngineeringBootstrap();", tabSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenDescribedSourcesAsync", tabSource, StringComparison.Ordinal);
    }

    [Fact]
    public void P1B_EvidenceColumnsRemainSparseOverlayNotRowWrappers()
    {
        var gridSource = File.ReadAllText(FindRepoFile("MainWindow.NativeFatCanonicalGrid.cs"));
        var overlaySource = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatCanonicalEvidenceOverlay.cs"));

        Assert.Contains("NativeFatEvidenceField.Value1", gridSource, StringComparison.Ordinal);
        Assert.Contains("NativeFatEvidenceField.Value2", gridSource, StringComparison.Ordinal);
        Assert.Contains("NativeFatEvidenceField.Result", gridSource, StringComparison.Ordinal);
        Assert.Contains("NativeFatIedSessionCacheState", gridSource, StringComparison.Ordinal);
        Assert.Contains("point.PointKey", overlaySource, StringComparison.Ordinal);
        Assert.Contains("cache.EvidenceByRow.Remove(key)", overlaySource, StringComparison.Ordinal);

        Assert.DoesNotContain("ObservableCollection<Iec61850MonitorPoint>", gridSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new Iec61850MonitorPoint", gridSource, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relativePath)
        => Path.Combine(FindRepoRoot(), relativePath);

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MainWindow.xaml")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tests", "ARSAS.Tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repository root from '{AppContext.BaseDirectory}'.");
    }
}
