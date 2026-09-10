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

        Assert.Contains("QueueProductionFatEngineeringBootstrap();", source, StringComparison.Ordinal);
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
    public void SeventhFatDestination_IsCanonicalMainWindowSiblingWithOneNavigationOwner()
    {
        var xaml = File.ReadAllText(FindRepoFile("MainWindow.xaml"));
        var mainSource = File.ReadAllText(FindRepoFile("MainWindow.xaml.cs"));
        var bridgeSource = File.ReadAllText(FindRepoFile("MainWindow.NativeFatWorkspace.cs"));
        var productionSource = File.ReadAllText(FindRepoFile("MainWindow.ProductionFatTab.cs"));
        var repoRoot = FindRepoRoot();

        Assert.Contains("x:Name=\"NavNativeFatButton\" Grid.Column=\"6\" Content=\"FAT\" Tag=\"6\" Click=\"NavButton_Click\" Style=\"{StaticResource SegmentedNavButton}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.ColumnSpan=\"7\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NativeFatTab\" Header=\"FAT\"", xaml, StringComparison.Ordinal);

        Assert.Contains("Math.Clamp(index, 0, NativeFatWorkspaceIndex)", mainSource, StringComparison.Ordinal);
        Assert.Contains("var cellWidth = contentWidth / 7d", mainSource, StringComparison.Ordinal);
        Assert.Contains("NavNativeFatButton", mainSource, StringComparison.Ordinal);

        Assert.Contains("private const int NativeFatWorkspaceIndex = 6", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("QueueNativeFatNavigationGeometry", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_nativeFatTab", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_nativeFatNavButton", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildNativeFatWorkspaceContent", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DataGrid", bridgeSource, StringComparison.Ordinal);

        Assert.Contains("NativeFatTab.Content = BuildProductionFatPermanentHost();", productionSource, StringComparison.Ordinal);
        Assert.Contains("NavNativeFatButton.ToolTip", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductionFatNavButton_Click", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_nativeFatInstalled", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_nativeFatTab", productionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_nativeFatNavButton", productionSource, StringComparison.Ordinal);

        Assert.False(
            File.Exists(Path.Combine(repoRoot, "MainWindow.ProductionFatNavigationParity.cs")),
            "Do not reintroduce a second ApplicationIdle navigation parity owner. MainWindow is the seven-slot navigation authority.");
    }

    [Fact]
    public void ExistingWorkspaceSelectionSideEffects_RemainProtectedWhileAddingFat()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.xaml.cs"));

        Assert.Contains("device.ClearUnreadEvents()", source, StringComparison.Ordinal);
        Assert.Contains("ActivateGooseSubscriberWorkspace()", source, StringComparison.Ordinal);
        Assert.Contains("ClearDiagnosticAlert()", source, StringComparison.Ordinal);
        Assert.Contains("UpdateNavigationVisuals(MainTabs.SelectedIndex, animate: true)", source, StringComparison.Ordinal);
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
