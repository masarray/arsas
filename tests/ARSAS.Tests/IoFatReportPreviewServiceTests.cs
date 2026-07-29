using System.Text;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatReportPreviewServiceTests
{
    [Fact]
    public void CreateIedScopedProject_PreservesIdentityAndOnlySelectedIed()
    {
        var first = BuildIed("IED_A", "192.168.1.10", "TP-A");
        var second = BuildIed("IED_B", "192.168.1.11", "TP-B");
        var project = BuildProject(first, second);

        var scoped = IoFatReportPreviewService.CreateIedScopedProject(project, second);

        Assert.Equal(project.ProjectId, scoped.ProjectId);
        Assert.Equal(project.SourceWorkbookSha256, scoped.SourceWorkbookSha256);
        Assert.Single(scoped.Ieds);
        Assert.Same(second, scoped.Ieds[0]);
    }

    [Fact]
    public void NativePerIedPdf_IsScopedToSelectedIed()
    {
        var selected = BuildIed("IED_A", "192.168.1.10", "TP-A");
        var other = BuildIed("IED_B", "192.168.1.11", "TP-B");
        var scoped = IoFatReportPreviewService.CreateIedScopedProject(BuildProject(selected, other), selected);

        var bytes = IoFatPdfReportService.Generate(
            scoped,
            new DateTimeOffset(2026, 7, 29, 7, 0, 0, TimeSpan.Zero));
        var text = Encoding.ASCII.GetString(bytes);

        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.Contains("IED_A", text, StringComparison.Ordinal);
        Assert.Contains("TP-A", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IED_B", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TP-B", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewWorkspace_UsesAriecNativeDocumentViewerPattern()
    {
        var previewSource = File.ReadAllText(FindRepoFile("IoListTestingWindow.PrintPreview.cs"));
        var builderSource = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatReportPreviewDocumentBuilder.cs"));
        var layoutSource = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatReportLayoutEngine.cs"));
        var pdfSource = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatPdfReportService.cs"));
        var htmlServiceSource = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatReportPreviewService.cs"));

        Assert.Contains("DocumentViewer", previewSource, StringComparison.Ordinal);
        Assert.Contains("FixedDocument", builderSource, StringComparison.Ordinal);
        Assert.Contains("FitToWidth", previewSource, StringComparison.Ordinal);
        Assert.Contains("ApplicationCommands.Print", previewSource, StringComparison.Ordinal);
        Assert.Contains("NavigationCommands.PreviousPage", previewSource, StringComparison.Ordinal);
        Assert.Contains("PageCount", previewSource, StringComparison.Ordinal);
        Assert.Contains("Native preview", previewSource, StringComparison.Ordinal);
        Assert.Contains("IoFatReportLayoutEngine.Build", builderSource, StringComparison.Ordinal);
        Assert.Contains("BuildLayout", pdfSource, StringComparison.Ordinal);
        Assert.Contains("IoFatReportLayoutPlan", layoutSource, StringComparison.Ordinal);
        Assert.Contains("DRAFT / LIVE", layoutSource, StringComparison.Ordinal);

        Assert.DoesNotContain("WebBrowser", previewSource, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigateToString", previewSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildHtml", previewSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildHtml", htmlServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SignalGrid_CentersOperationalColumnsAndUsesRequestedEvidenceColors()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.PrintPreview.cs"));
        var converters = File.ReadAllText(FindRepoFile("IoListTestingWindow.GridConverters.cs"));

        Assert.Contains("InstallSignalGridPolish", source, StringComparison.Ordinal);
        Assert.Contains("HorizontalContentAlignmentProperty, HorizontalAlignment.Center", source, StringComparison.Ordinal);
        Assert.Contains("TextBlock.TextAlignmentProperty, TextAlignment.Center", source, StringComparison.Ordinal);
        Assert.Contains("ApplyColumn(grid, \"LIVE\"", source, StringComparison.Ordinal);
        Assert.Contains("ApplyColumn(grid, \"VALUE\"", source, StringComparison.Ordinal);
        Assert.Contains("ApplyColumn(grid, \"QUALITY\"", source, StringComparison.Ordinal);
        Assert.Contains("ApplyColumn(grid, \"STATUS\"", source, StringComparison.Ordinal);
        Assert.Contains("ApplyColumn(grid, \"RESULT\"", source, StringComparison.Ordinal);
        Assert.Contains("Runtime.OnEvidence", source, StringComparison.Ordinal);
        Assert.Contains("Runtime.OffEvidence", source, StringComparison.Ordinal);

        Assert.Contains("TrueBrush = Brush(229, 72, 77)", converters, StringComparison.Ordinal);
        Assert.Contains("FalseBrush = Brush(22, 166, 106)", converters, StringComparison.Ordinal);
        Assert.Contains("SuccessBrush = Brush(22, 132, 90)", converters, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewKeepsProgressOnIedCardOnly()
    {
        var previewSource = File.ReadAllText(FindRepoFile("IoListTestingWindow.PrintPreview.cs"));
        var xaml = File.ReadAllText(FindRepoFile("IoListTestingWindow.xaml"));

        Assert.Contains("RemoveMainPreparationSurface", previewSource, StringComparison.Ordinal);
        Assert.Contains("workspaceGrid.Children.Remove(preparationSurface)", previewSource, StringComparison.Ordinal);
        Assert.Contains("ClearStalePreparationFlags", previewSource, StringComparison.Ordinal);
        Assert.Contains("CardProgress", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void IoFatPreparation_UsesCachedReconnect_AndRetriesReportPlanBeforePollingFallback()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.AutoConnect.cs"));

        Assert.Contains("Fast reconnect", source, StringComparison.Ordinal);
        Assert.Contains("ConnectUsingSavedModelAsync", source, StringComparison.Ordinal);
        Assert.Contains("SettleIoFatReportPriorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("rebuilding the report plan once", source, StringComparison.Ordinal);
        Assert.Contains("configured RCB → temporary dynamic DataSet/URCB", source, StringComparison.Ordinal);
        Assert.Contains("IsReportSource", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteControlAsync", source, StringComparison.Ordinal);
    }

    private static IoTestProject BuildProject(params IoTestIedPlan[] ieds) => new()
    {
        ProjectId = "PROJECT-1",
        SchemaVersion = "ARSAS-FAT-IO-1.0",
        ProjectName = "Preview Project",
        SourceWorkbookName = "source.xlsx",
        SourceWorkbookSha256 = "abcdef0123456789abcdef0123456789",
        Ieds = ieds.ToList()
    };

    private static IoTestIedPlan BuildIed(string name, string ip, string signalName) => new()
    {
        IedName = name,
        IpAddress = ip,
        TestPoints = new List<IoTestPointPlan>
        {
            new()
            {
                TestPointId = $"{name}-TP-1",
                IedName = name,
                IpAddress = ip,
                SignalName = signalName,
                ObjectReference = $"{name}LD/GGIO1.Ind1.stVal",
                FunctionalConstraint = "ST",
                ExpectedOnText = "true",
                ExpectedOffText = "false"
            }
        }
    };

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
