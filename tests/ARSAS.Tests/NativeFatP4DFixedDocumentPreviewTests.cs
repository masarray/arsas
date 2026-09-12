using System.Text;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

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
    public void P4D_SavePdfSerializesTheExactLayoutAlreadyRenderedInPreview()
    {
        var preview = File.ReadAllText(FindRepoFile("MainWindow.NativeFatPrintPreview.cs"));
        var pdfService = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatPdfReportService.cs"));
        var pdfWriter = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatNativePdfWriter.cs"));

        Assert.Contains("var layout = NativeFatP4DReportAdapter.Build(snapshot, draft: true);", preview, StringComparison.Ordinal);
        Assert.Contains("IoFatReportPreviewDocumentBuilder.Render(layout)", preview, StringComparison.Ordinal);
        Assert.Contains("Content = \"Save PDF\"", preview, StringComparison.Ordinal);
        Assert.Contains("IoFatPdfReportService.SaveLayout(", preview, StringComparison.Ordinal);
        Assert.Contains("layout,", preview, StringComparison.Ordinal);
        Assert.Contains("internal static void SaveLayout(", pdfService, StringComparison.Ordinal);
        Assert.Contains("GenerateLayout(layout, reportName, primaryReference)", pdfService, StringComparison.Ordinal);
        Assert.Contains("IoFatNativePdfWriter.Build(layout, reportName, primaryReference)", pdfService, StringComparison.Ordinal);
        Assert.Contains("public static byte[] Build(\n        IoFatReportLayoutPlan layout,\n        string reportName,", pdfWriter, StringComparison.Ordinal);
    }

    [Fact]
    public void P4D_LayoutFirstPdfWriterProducesNativePdfWithoutIoTestProjectRebuild()
    {
        var device = new Iec61850MonitorDevice
        {
            DeviceId = "runtime-p4d",
            Name = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            Port = 102
        };
        var point = new Iec61850MonitorPoint
        {
            DeviceId = device.DeviceId,
            DeviceName = device.Name,
            SignalName = "Breaker",
            IecReference = "AA1E1F06R4LD0/XCBR1.Pos.stVal",
            Quality = "Good",
            Value = "Open [01]"
        };
        device.Points.Add(point);
        var cache = new NativeFatIedSessionCacheState();
        NativeFatCanonicalEvidenceOverlay.WriteCapture(
            cache,
            point,
            NativeFatEvidenceField.Value1,
            "Open [01]",
            ArIED61850Tester.Models.IoTesting.FatEvidenceCaptureKind.OperatorSnapshot,
            DateTimeOffset.UtcNow);
        NativeFatCanonicalEvidenceOverlay.WriteCapture(
            cache,
            point,
            NativeFatEvidenceField.Value2,
            "Closed [10]",
            ArIED61850Tester.Models.IoTesting.FatEvidenceCaptureKind.OperatorSnapshot,
            DateTimeOffset.UtcNow);

        var snapshot = NativeFatPrintPreviewSnapshot.Capture(device, cache);
        var layout = NativeFatP4DReportAdapter.Build(snapshot, draft: true);
        var bytes = IoFatPdfReportService.GenerateLayout(layout, snapshot.IedName, snapshot.Rows[0].IecTelegram);

        Assert.NotEmpty(layout.Pages);
        Assert.True(bytes.Length > 32);
        Assert.Equal("%PDF-1.4", Encoding.ASCII.GetString(bytes, 0, 8));
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
        Assert.Contains("NativeFatCanonicalEvidenceOverlay.ReadDisplay", snapshot, StringComparison.Ordinal);
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
