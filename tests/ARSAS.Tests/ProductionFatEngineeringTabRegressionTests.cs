namespace ARSAS.Tests;

public sealed class ProductionFatEngineeringTabRegressionTests
{
    [Fact]
    public void EngineeringProjection_ReusesParsedSclWorkspaceWithoutOpeningXmlAgain()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatEngineeringWorkspaceProjectionService.cs"));

        Assert.Contains("device.SclWorkspace", source, StringComparison.Ordinal);
        Assert.Contains("FatSclWorkspaceImportService.Import(workspaceSources)", source, StringComparison.Ordinal);
        Assert.Contains("ENGINEERING_SCL_DATASET_AUTHORITY", source, StringComparison.Ordinal);
        Assert.Contains("IoFatSourceWorkspaceService.DescribeAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SclWorkspaceService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadScl", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionFatTab_AutoBootstrapsFromSelectedEngineeringStaticDataSet()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.ProductionFatEngineeringBootstrap.cs"));

        Assert.Contains("MainTabs.SelectedIndex != NativeFatWorkspaceIndex", source, StringComparison.Ordinal);
        Assert.Contains("selected?.SclWorkspace", source, StringComparison.Ordinal);
        Assert.Contains("DesignModel.DataSets.Sum", source, StringComparison.Ordinal);
        Assert.Contains("IoFatEngineeringWorkspaceProjectionService.BuildAsync", source, StringComparison.Ordinal);
        Assert.Contains("AdoptEngineeringRuntimeWorkspaces", source, StringComparison.Ordinal);
        Assert.Contains("IoTestWorkspaceBootstrapService.OpenSourcesAsync", source, StringComparison.Ordinal);
        Assert.Contains("SynchronizeImportedSclFatWithEngineering", source, StringComparison.Ordinal);
        Assert.Contains("ShowIoTestingWorkspaceAsync", source, StringComparison.Ordinal);
        Assert.Contains("no SCL re-import", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenSclFatTesting_Click", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionFatTab_RegistersExactEngineeringRuntimeWorkspacesForSharedAcquisition()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatSclProjectImportService.cs"));

        Assert.Contains("AdoptEngineeringRuntimeWorkspaces", source, StringComparison.Ordinal);
        Assert.Contains("SetRuntimeWorkspaces(stable)", source, StringComparison.Ordinal);
        Assert.Contains("workspace.WorkspaceKey", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SeventhFatDestination_HasOnlyOneSevenSlotCompatibilityOwner()
    {
        var nativeSource = File.ReadAllText(FindRepoFile("MainWindow.NativeFatWorkspace.cs"));
        var repoRoot = FindRepoRoot();

        Assert.Contains("NavNativeFatButton", nativeSource, StringComparison.Ordinal);
        Assert.Contains("TryFindResource(\"SegmentedNavButton\")", nativeSource, StringComparison.Ordinal);
        Assert.Contains("var cellWidth = contentWidth / 7d", nativeSource, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(MainTabs.SelectedIndex, 0, NativeFatWorkspaceIndex)", nativeSource, StringComparison.Ordinal);
        Assert.False(
            File.Exists(Path.Combine(repoRoot, "MainWindow.ProductionFatNavigationParity.cs")),
            "Do not reintroduce a second ApplicationIdle navigation parity owner. Seven-slot navigation must converge toward one canonical owner.");
    }

    [Fact]
    public void ProductionFatSafetyBoundary_KeepsEngineeringAuthorityAndStrictPreflightCoverage()
    {
        var bootstrapSource = File.ReadAllText(FindRepoFile("MainWindow.ProductionFatEngineeringBootstrap.cs"));
        var fieldRegressionSource = File.ReadAllText(FindRepoFile("tests/ARSAS.Tests/ProductionFatP0FieldRegressionTests.cs"));

        Assert.Contains("RetireManualWorkspaceRowsForStaticDataSetMode", bootstrapSource, StringComparison.Ordinal);
        Assert.Contains("AutomaticStaticDataSetScope_RetiresManualAliasBeforeSessionPreflight", fieldRegressionSource, StringComparison.Ordinal);
        Assert.Contains("IoTestSessionPreflight.Validate", fieldRegressionSource, StringComparison.Ordinal);
        Assert.Contains("multiple enabled test points", fieldRegressionSource, StringComparison.OrdinalIgnoreCase);
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
