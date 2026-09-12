namespace ARSAS.Tests;

public sealed class NativeFatP5LegacyBridgeRemovalTests
{
    [Fact]
    public void P5_NormalNativeFatRuntimeHasNoProjectionBootstrapReconnectOrSecondAcquisitionOwner()
    {
        var grid = File.ReadAllText(FindRepoFile("MainWindow.NativeFatCanonicalGrid.cs"));
        var arm = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatArmCoordinator.cs"));
        var preview = File.ReadAllText(FindRepoFile("MainWindow.NativeFatPrintPreview.cs"));
        var tab = File.ReadAllText(FindRepoFile("MainWindow.ProductionFatTab.cs"));

        Assert.Contains("_nativeFatCanonicalGrid.ItemsSource = device?.Points;", grid, StringComparison.Ordinal);
        Assert.Contains("NativeFatCanonicalEvidenceOverlay", arm, StringComparison.Ordinal);
        Assert.Contains("NativeFatPrintPreviewSnapshot.Capture", preview, StringComparison.Ordinal);
        Assert.Contains("BuildNativeFatCanonicalWorkspace", tab, StringComparison.Ordinal);

        var normalRuntimeSources = new[] { grid, arm, preview };
        foreach (var source in normalRuntimeSources)
        {
            foreach (var forbidden in new[]
                     {
                         "IoListTestingWindow",
                         "IoFatEngineeringWorkspaceProjectionService",
                         "IoTestWorkspaceBootstrapService",
                         "OpenDescribedSourcesAsync",
                         "PrepareIoTestIedForFatAsync",
                         "ConnectAndDiscoverAsync",
                         "ConnectUsingCachedModelAsync",
                         "StartMonitoringAsync",
                         "ShowIoTestingWorkspaceAsync",
                         "FatSclWorkspaceImportService"
                     })
            {
                Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
            }
        }

        foreach (var forbidden in new[]
                 {
                     "QueueProductionFatEngineeringBootstrap",
                     "EnsureProductionFatFromEngineeringAsync",
                     "IoFatEngineeringWorkspaceProjectionService",
                     "IoTestWorkspaceBootstrapService",
                     "OpenDescribedSourcesAsync",
                     "ShowIoTestingWorkspaceAsync"
                 })
        {
            Assert.DoesNotContain(forbidden, tab, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void P5_ObsoleteAutomaticBridgeFilesAndProjectionAuthorityArePhysicallyRemoved()
    {
        var root = FindRepoRoot();
        var selection = File.ReadAllText(FindRepoFile("Services/IoTesting/IoTestSignalSelectionService.cs"));
        var importer = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatSclProjectImportService.cs"));

        foreach (var relativePath in new[]
                 {
                     "MainWindow.ProductionFatEngineeringBootstrap.cs",
                     "MainWindow.ProductionFatNoFlicker.cs",
                     Path.Combine("Services", "IoTesting", "IoFatEngineeringWorkspaceProjectionService.cs"),
                     Path.Combine("Services", "IoTesting", "IoFatCanonicalEvidenceMigrationService.cs")
                 })
        {
            Assert.False(
                File.Exists(Path.Combine(root, relativePath)),
                $"P5 requires obsolete automatic FAT bridge file '{relativePath}' to remain retired.");
        }

        Assert.DoesNotContain("ENGINEERING_SCL_DATASET_AUTHORITY", selection, StringComparison.Ordinal);
        Assert.DoesNotContain("EngineeringSclDataSetAuthorityBindingStatus", selection, StringComparison.Ordinal);
        Assert.DoesNotContain("AdoptEngineeringRuntimeWorkspaces", importer, StringComparison.Ordinal);
    }

    [Fact]
    public void P5_ExplicitManualCompatibilityRemainsAnIsolatedOperatorBoundary()
    {
        var tab = File.ReadAllText(FindRepoFile("MainWindow.ProductionFatTab.cs"));

        Assert.Contains("MountProductionFatWorkspace", tab, StringComparison.Ordinal);
        Assert.Contains("UnmountProductionFatWorkspace", tab, StringComparison.Ordinal);
        Assert.Contains("FAT compatibility workspace", tab, StringComparison.Ordinal);
        Assert.Contains("if (_productionFatWindow is { IsLoaded: true })", tab, StringComparison.Ordinal);
        Assert.Contains("BindNativeFatCanonicalRows();", tab, StringComparison.Ordinal);
    }

    [Fact]
    public void P5_LegacyEvidenceHydrationIsPassiveDataMigrationNotWorkspaceBootstrap()
    {
        var hydration = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatEvidenceHydrationService.cs"));

        Assert.Contains("TryHydrateLegacySnapshotAsync", hydration, StringComparison.Ordinal);
        Assert.Contains("without opening the legacy workspace", hydration, StringComparison.Ordinal);
        Assert.DoesNotContain("IoListTestingWindow", hydration, StringComparison.Ordinal);
        Assert.DoesNotContain("IoTestWorkspaceBootstrapService", hydration, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenDescribedSourcesAsync", hydration, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowIoTestingWorkspaceAsync", hydration, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectAndDiscoverAsync", hydration, StringComparison.Ordinal);
        Assert.DoesNotContain("StartMonitoringAsync", hydration, StringComparison.Ordinal);
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
