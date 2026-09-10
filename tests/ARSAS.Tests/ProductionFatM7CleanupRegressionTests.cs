namespace ARSAS.Tests;

public sealed class ProductionFatM7CleanupRegressionTests
{
    [Fact]
    public void ObsoleteNativeFatRuntime_IsNotASecondProductionAuthority()
    {
        var repoRoot = FindRepoRoot();
        var bridge = File.ReadAllText(Path.Combine(repoRoot, "MainWindow.NativeFatWorkspace.cs"));
        var productionTab = File.ReadAllText(Path.Combine(repoRoot, "MainWindow.ProductionFatTab.cs"));

        Assert.False(
            File.Exists(Path.Combine(repoRoot, "MainWindow.NativeFatExport.cs")),
            "The retired native FAT export must not return; production FAT owns report/export delivery.");
        Assert.False(
            File.Exists(Path.Combine(repoRoot, "MainWindow.NativeFatReportPreview.cs")),
            "The retired native FAT side-panel preview must not return. Production FAT owns one in-place report preview.");
        Assert.False(
            File.Exists(Path.Combine(repoRoot, "MainWindow.NativeFatHistoryInspector.cs")),
            "The retired side-panel history inspector must not return as a second FAT presentation stack.");

        // MainWindow.NativeFatWorkspace.cs is now only a compatibility bridge for the
        // canonical seventh shell slot. It must never regain its own FAT runtime/state.
        Assert.Contains("private const int NativeFatWorkspaceIndex = 6", bridge, StringComparison.Ordinal);
        Assert.Contains("QueueNativeFatNavigationGeometry", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("DataGrid", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("NativeFatStateStore", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("NativeFatSignalRow", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("NativeFatCapture", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveNativeFatStateAsync", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildNativeFatWorkspaceContent", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterNativeFatWorkspace", bridge, StringComparison.Ordinal);

        // Production FAT mounts directly into the canonical XAML slot. There must be no
        // compatibility-field handshake with the retired native runtime.
        Assert.Contains("NativeFatTab.Content = BuildProductionFatPermanentHost();", productionTab, StringComparison.Ordinal);
        Assert.Contains("NativeFatTab.Content = surface;", productionTab, StringComparison.Ordinal);
        Assert.Contains("NavNativeFatButton.ToolTip", productionTab, StringComparison.Ordinal);
        Assert.DoesNotContain("_nativeFatInstalled", productionTab, StringComparison.Ordinal);
        Assert.DoesNotContain("_nativeFatTab", productionTab, StringComparison.Ordinal);
        Assert.DoesNotContain("_nativeFatNavButton", productionTab, StringComparison.Ordinal);
        Assert.DoesNotContain("AttachNativeFatObservedDevice", productionTab, StringComparison.Ordinal);
        Assert.DoesNotContain("NativeFat_MainWindowPropertyChanged", productionTab, StringComparison.Ordinal);
        Assert.DoesNotContain("NativeFat_MainTabsSelectionChanged", productionTab, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionReportPreviewAndExport_RemainSingleAuthority()
    {
        var repoRoot = FindRepoRoot();
        var productionPreview = File.ReadAllText(Path.Combine(repoRoot, "IoListTestingWindow.PrintPreview.cs"));
        var embeddedHost = File.ReadAllText(Path.Combine(repoRoot, "IoListTestingWindow.EmbeddedEngineeringHost.cs"));
        var pdfService = File.ReadAllText(Path.Combine(repoRoot, "Services", "IoTesting", "IoFatPdfReportService.cs"));

        Assert.Contains("BuildPrintPreviewHost", productionPreview, StringComparison.Ordinal);
        Assert.Contains("IoFatReportPreviewDocumentBuilder.Build(scopedProject, draft)", productionPreview, StringComparison.Ordinal);
        Assert.Contains("InstallPerIedPrintPreview();", embeddedHost, StringComparison.Ordinal);
        Assert.Contains("_printPreviewToggle = WorkspacePreviewToggle;", embeddedHost, StringComparison.Ordinal);
        Assert.Contains("BuildLayout", pdfService, StringComparison.Ordinal);
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
