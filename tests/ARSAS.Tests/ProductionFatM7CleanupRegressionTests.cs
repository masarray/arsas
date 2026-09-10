namespace ARSAS.Tests;

public sealed class ProductionFatM7CleanupRegressionTests
{
    [Fact]
    public void ObsoleteNativeSidePanelPreviewStack_IsNotASecondFatPreviewAuthority()
    {
        var repoRoot = FindRepoRoot();
        Assert.False(
            File.Exists(Path.Combine(repoRoot, "MainWindow.NativeFatReportPreview.cs")),
            "The retired native FAT side-panel preview must not return. Production FAT owns one in-place report preview.");
        Assert.False(
            File.Exists(Path.Combine(repoRoot, "MainWindow.NativeFatHistoryInspector.cs")),
            "The retired side-panel history inspector depended on the obsolete native preview and must not return as a second preview stack.");

        var productionPreview = File.ReadAllText(Path.Combine(repoRoot, "IoListTestingWindow.PrintPreview.cs"));
        var embeddedHost = File.ReadAllText(Path.Combine(repoRoot, "IoListTestingWindow.EmbeddedEngineeringHost.cs"));

        Assert.Contains("BuildPrintPreviewHost", productionPreview, StringComparison.Ordinal);
        Assert.Contains("IoFatReportPreviewDocumentBuilder.Build(scopedProject, draft)", productionPreview, StringComparison.Ordinal);
        Assert.Contains("InstallPerIedPrintPreview();", embeddedHost, StringComparison.Ordinal);
        Assert.Contains("_printPreviewToggle = WorkspacePreviewToggle;", embeddedHost, StringComparison.Ordinal);
    }

    [Fact]
    public void Cleanup_DoesNotRetireProductionCapturePreflightOrEngineeringAcquisitionAuthority()
    {
        var bootstrap = File.ReadAllText(FindRepoFile("MainWindow.ProductionFatEngineeringBootstrap.cs"));
        var adapter = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatProductionControllerAdapter.cs"));
        var contract = File.ReadAllText(FindRepoFile("docs/FAT_ENGINEERING_WORKSTATION_CONTRACT.md"));

        Assert.Contains("AdoptEngineeringRuntimeWorkspaces", bootstrap, StringComparison.Ordinal);
        Assert.Contains("IoTestSessionPreflight.Validate", adapter, StringComparison.Ordinal);
        Assert.Contains("IoFatProductionControllerAdapter", adapter, StringComparison.Ordinal);
        Assert.Contains("production FAT", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Engineering", contract, StringComparison.OrdinalIgnoreCase);
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
