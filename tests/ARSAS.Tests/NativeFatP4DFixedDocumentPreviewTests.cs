namespace ARSAS.Tests;

public sealed class NativeFatP4DFixedDocumentPreviewTests
{
    [Fact]
    public void P4D_PreviewUsesExistingFixedDocumentAuthorityInsteadOfDataGrid()
    {
        var preview = File.ReadAllText(FindRepoFile("MainWindow.NativeFatPrintPreview.cs"));
        var adapter = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatP4DReportAdapter.cs"));
        var renderer = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatReportPreviewDocumentBuilder.cs"));

        Assert.Contains("NativeFatP4DReportAdapter.Build(snapshot, draft: true)", preview, StringComparison.Ordinal);
        Assert.Contains("IoFatReportPreviewDocumentBuilder.Render(layout)", preview, StringComparison.Ordinal);
        Assert.Contains("new DocumentViewer", preview, StringComparison.Ordinal);
        Assert.Contains("Document = document", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("new DataGrid", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("AddPreviewColumn", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsSource = snapshot.Rows", preview, StringComparison.Ordinal);

        Assert.Contains("IoFatReportLayoutPlan Build", adapter, StringComparison.Ordinal);
        Assert.Contains("public static FixedDocument Render(", renderer, StringComparison.Ordinal);
        Assert.Contains("IoFatReportLayoutPlan layout", renderer, StringComparison.Ordinal);
        Assert.Contains("new FixedDocument()", renderer, StringComparison.Ordinal);
    }

    [Fact]
    public void P4D_ReportAdapterLocksExactP4CColumns()
    {
        var adapter = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatP4DReportAdapter.cs"));
        var snapshot = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatPrintPreviewSnapshot.cs"));

        var signal = adapter.IndexOf("\"Signal\"", StringComparison.Ordinal);
        var telegram = adapter.IndexOf("\"IEC Telegram\"", StringComparison.Ordinal);
        var quality = adapter.IndexOf("\"Quality\"", StringComparison.Ordinal);
        var live = adapter.IndexOf("\"Live Value\"", StringComparison.Ordinal);
        var value1 = adapter.IndexOf("\"Value 1\"", StringComparison.Ordinal);
        var value2 = adapter.IndexOf("\"Value 2\"", StringComparison.Ordinal);
        var result = adapter.IndexOf("\"Result\"", StringComparison.Ordinal);

        Assert.True(signal >= 0);
        Assert.True(telegram > signal);
        Assert.True(quality > telegram);
        Assert.True(live > quality);
        Assert.True(value1 > live);
        Assert.True(value2 > value1);
        Assert.True(result > value2);

        Assert.DoesNotContain("\"Type\"", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Status\"", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Timestamp\"", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("\"IEC 61850 reference\"", adapter, StringComparison.Ordinal);

        Assert.Contains("string IecTelegram", snapshot, StringComparison.Ordinal);
        Assert.Contains("string Quality", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("string Type", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("string Status", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void P4D_PreviewDoesNotReintroduceRuntimeOrSclBootstrap()
    {
        var preview = File.ReadAllText(FindRepoFile("MainWindow.NativeFatPrintPreview.cs"));
        var adapter = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatP4DReportAdapter.cs"));

        foreach (var forbidden in new[]
                 {
                     "ConnectAndDiscoverAsync",
                     "StartMonitoringAsync",
                     "PrepareIoTestIedForFatAsync",
                     "OpenDescribedSourcesAsync",
                     "IoFatEngineeringWorkspaceProjectionService",
                     "FatSclWorkspaceImportService"
                 })
        {
            Assert.DoesNotContain(forbidden, preview, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, adapter, StringComparison.Ordinal);
        }
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
