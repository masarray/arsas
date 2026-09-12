namespace ARSAS.Tests;

public sealed class ProductionFatEngineeringTabRegressionTests
{
    [Fact]
    public void EngineeringProjection_ReusesParsedSclWorkspaceWithoutOpeningXmlAgain()
    {
        // This service remains available to explicit/manual compatibility workflows.
        // P5 removes it only from normal native FAT navigation/runtime ownership.
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
    public void ProductionFatTab_NormalEntryHasNoLegacyProjectionBootstrapModule()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.ProductionFatTab.cs"));
        var repoRoot = FindRepoRoot();

        Assert.False(
            File.Exists(Path.Combine(repoRoot, "MainWindow.ProductionFatEngineeringBootstrap.cs")),
            "P5 removes the automatic Engineering -> legacy IoTest bootstrap module from normal FAT navigation.");

        Assert.Contains("NativeFatTab.Content = BuildProductionFatPermanentHost();", source, StringComparison.Ordinal);
        Assert.Contains("SynchronizeProductionFatSelectedIed();", source, StringComparison.Ordinal);
        Assert.Contains("BindNativeFatCanonicalRows();", source, StringComparison.Ordinal);

        foreach (var forbidden in new[]
                 {
                     "QueueProductionFatEngineeringBootstrap",
                     "EnsureProductionFatFromEngineeringAsync",
                     "IoFatEngineeringWorkspaceProjectionService",
                     "IoTestWorkspaceBootstrapService",
                     "OpenDescribedSourcesAsync",
                     "ShowIoTestingWorkspaceAsync",
                     "ShowProductionFatBootstrapState"
                 })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }

        // Explicit/manual compatibility remains a deliberate, operator-invoked boundary.
        Assert.Contains("MountProductionFatWorkspace", source, StringComparison.Ordinal);
        Assert.Contains("FAT compatibility workspace", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitCompatibilityProjection_CanStillAdoptExactEngineeringRuntimeWorkspaces()
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
    public void ExplicitLegacyMigrationSafety_RemainsAvailableWithoutOwningNormalFatRuntime()
    {
        var migrationSource = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatCanonicalEvidenceMigrationService.cs"));
        var fieldRegressionSource = File.ReadAllText(FindRepoFile("tests/ARSAS.Tests/ProductionFatP0FieldRegressionTests.cs"));
        var preflightSource = File.ReadAllText(FindRepoFile("Services/IoTesting/IoTestSessionPreflight.cs"));
        var preflightTests = File.ReadAllText(FindRepoFile("tests/ARSAS.Tests/IoTestSessionPreflightTests.cs"));

        Assert.Contains("MigrateAndRemoveLegacyManualRows", migrationSource, StringComparison.Ordinal);
        Assert.Contains("IsLegacyManualWorkspaceRow", migrationSource, StringComparison.Ordinal);
        Assert.Contains("AutomaticStaticDataSetScope_RetiresManualAliasBeforeSessionPreflight", fieldRegressionSource, StringComparison.Ordinal);
        Assert.Contains("IoTestSessionPreflight.Validate", fieldRegressionSource, StringComparison.Ordinal);
        Assert.Contains("RetireRedundantManualWorkspaceRows", preflightSource, StringComparison.Ordinal);
        Assert.Contains("multiple enabled test points", preflightSource, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Validate_RejectsDuplicateEnabledLiveReference", preflightTests, StringComparison.Ordinal);
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
