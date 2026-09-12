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

    [Fact]
    public void P1C_NativeFatUsesEngineeringGridStyleTemplateAndVirtualizationContract()
    {
        var gridSource = File.ReadAllText(FindRepoFile("MainWindow.NativeFatCanonicalGrid.cs"));
        var columnContract = File.ReadAllText(FindRepoFile("MainWindow.NativeFatP4CColumnContract.cs"));
        var engineeringXaml = File.ReadAllText(FindRepoFile("MainWindow.xaml"));
        var appXaml = File.ReadAllText(FindRepoFile("App.xaml"));

        Assert.Contains("x:Key=\"ModernDataGrid\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"RowHeight\" Value=\"32\"/>", appXaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"ColumnHeaderHeight\" Value=\"34\"/>", appXaml, StringComparison.Ordinal);

        Assert.Contains("Style=\"{StaticResource ModernDataGrid}\" FrozenColumnCount=\"2\"", engineeringXaml, StringComparison.Ordinal);
        Assert.Contains("CellTemplate=\"{StaticResource ProcessValueBadgeTemplate}\"", engineeringXaml, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", engineeringXaml, StringComparison.Ordinal);

        Assert.Contains("FindResource(\"ModernDataGrid\") as Style", gridSource, StringComparison.Ordinal);
        Assert.Contains("ApplyNativeFatP4CColumnContract();", gridSource, StringComparison.Ordinal);
        Assert.Contains("AddCanonicalTemplateColumn(\"Live Value\", \"ProcessValueBadgeTemplate\", 140);", columnContract, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.SetVirtualizationMode(_nativeFatCanonicalGrid, VirtualizationMode.Recycling);", gridSource, StringComparison.Ordinal);
        Assert.Contains("RowStyle = BuildEngineeringLiveRowStyle()", gridSource, StringComparison.Ordinal);
        Assert.Contains("CellStyle = BuildEngineeringLiveCellStyle()", gridSource, StringComparison.Ordinal);

        // Engineering FAT must inherit the shared 32 px authority rather than the
        // legacy IoList FAT local 40 px row family.
        Assert.DoesNotContain("RowHeight = 40", gridSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight = 40", gridSource, StringComparison.Ordinal);
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
