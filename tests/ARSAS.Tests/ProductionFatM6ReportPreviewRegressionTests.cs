namespace ARSAS.Tests;

public sealed class ProductionFatM6ReportPreviewRegressionTests
{
    [Fact]
    public void EmbeddedFat_UsesExactProductionPreviewInsideTheSameCenter()
    {
        var xaml = File.ReadAllText(FindRepoFile("IoListTestingWindow.xaml"));
        var embedded = File.ReadAllText(FindRepoFile("IoListTestingWindow.EmbeddedEngineeringHost.cs"));
        var preview = File.ReadAllText(FindRepoFile("IoListTestingWindow.PrintPreview.cs"));
        var productionTab = File.ReadAllText(FindRepoFile("MainWindow.ProductionFatTab.cs"));

        Assert.Contains("x:Name=\"WorkspacePreviewToggle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Print Preview\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"TogglePrintPreview_Click\"", xaml, StringComparison.Ordinal);

        Assert.Contains("InstallPerIedPrintPreview();", embedded, StringComparison.Ordinal);
        Assert.Contains("_printPreviewToggle = WorkspacePreviewToggle;", embedded, StringComparison.Ordinal);
        Assert.Contains("DetachProductionFatCentralWorkspace", embedded, StringComparison.Ordinal);

        // MainWindow hosts the exact detached production center in the canonical XAML FAT tab.
        // Explorer and Command Dock remain owned by the workstation shell; preview never
        // replaces MainWindow itself and no retired native FAT tab authority is recreated.
        Assert.Contains("NativeFatTab.Content = surface;", productionTab, StringComparison.Ordinal);
        Assert.DoesNotContain("_nativeFatTab.Content = surface;", productionTab, StringComparison.Ordinal);
        Assert.Contains("global Engineering IED Explorer and shared Command Dock remain authoritative", productionTab, StringComparison.Ordinal);

        var install = ExtractMethod(preview, "private void InstallPerIedPrintPreview()");
        Assert.Contains("_signalWorkspaceGrid = workspaceGrid.Children.OfType<DataGrid>().FirstOrDefault();", install, StringComparison.Ordinal);
        Assert.Contains("_printPreviewHost = BuildPrintPreviewHost();", install, StringComparison.Ordinal);
        Assert.Contains("workspaceGrid.Children.Add(_printPreviewHost);", install, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewToggle_PreservesTheExactProductionGridInstanceAndCaptureSession()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.PrintPreview.cs"));
        var toggle = ExtractMethod(source, "private void TogglePrintPreview_Click(object sender, RoutedEventArgs e)");

        Assert.Contains("_signalWorkspaceGrid.Visibility = _printPreviewActive ? Visibility.Collapsed : Visibility.Visible;", toggle, StringComparison.Ordinal);
        Assert.Contains("_printPreviewHost.Visibility = _printPreviewActive ? Visibility.Visible : Visibility.Collapsed;", toggle, StringComparison.Ordinal);
        Assert.Contains("RefreshPrintPreview();", toggle, StringComparison.Ordinal);

        // M6 is an in-place view switch. Recreating the grid would lose scroll/selection,
        // while touching session lifecycle here could redirect or interrupt an active capture.
        Assert.DoesNotContain("new DataGrid", toggle, StringComparison.Ordinal);
        Assert.DoesNotContain("Session.Stop", toggle, StringComparison.Ordinal);
        Assert.DoesNotContain("Session.Pause", toggle, StringComparison.Ordinal);
        Assert.DoesNotContain("Session.Resume", toggle, StringComparison.Ordinal);
        Assert.DoesNotContain("Cancel", toggle, StringComparison.Ordinal);
        Assert.DoesNotContain("Dispose", toggle, StringComparison.Ordinal);
        Assert.DoesNotContain("Show()", toggle, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDialog", toggle, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewAndNativePdf_ShareOneReportLayoutAuthority()
    {
        var preview = File.ReadAllText(FindRepoFile("IoListTestingWindow.PrintPreview.cs"));
        var documentBuilder = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatReportPreviewDocumentBuilder.cs"));
        var pdf = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatPdfReportService.cs"));

        var refresh = ExtractMethod(preview, "private void RefreshPrintPreview()");
        Assert.Contains("IoFatReportPreviewService.CreateIedScopedProject(Project, SelectedIed)", refresh, StringComparison.Ordinal);
        Assert.Contains("IoFatReportPreviewDocumentBuilder.Build(scopedProject, draft)", refresh, StringComparison.Ordinal);
        Assert.Contains("Session.IsSessionActive && ReferenceEquals(Session.ActiveIed, SelectedIed)", refresh, StringComparison.Ordinal);

        Assert.Contains("IoFatPdfReportService.BuildLayout(project, generatedAt, draft)", documentBuilder, StringComparison.Ordinal);
        Assert.Contains("var layout = BuildLayout(project, generatedAt, draft: false);", pdf, StringComparison.Ordinal);
        Assert.Contains("IoFatNativePdfWriter.Build(layout, project)", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivePreview_TracksViewedIedAndEvidenceWithoutStoppingCapture()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.PrintPreview.cs"));

        Assert.Contains("_printPreviewActive && e.PropertyName == nameof(SelectedIed)", source, StringComparison.Ordinal);
        Assert.Contains("e.PropertyName is nameof(Session.State) or nameof(Session.EvidenceRecordCount)", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke(DispatcherPriority.Background, RefreshPrintPreview)", source, StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method '{signature}'.");

        var openBrace = source.IndexOf('{', start);
        Assert.True(openBrace >= 0, $"Could not find opening brace for '{signature}'.");

        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            switch (source[index])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                        return source[start..(index + 1)];
                    break;
            }
        }

        throw new InvalidDataException($"Method '{signature}' has no balanced closing brace.");
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
