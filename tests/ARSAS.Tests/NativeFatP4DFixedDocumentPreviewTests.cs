using System.Text;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class NativeFatP4DFixedDocumentPreviewTests
{
    [Fact]
    public void P4D_PreviewUsesExistingFixedDocumentAuthorityWithProfessionalToolbar()
    {
        var preview = File.ReadAllText(FindRepoFile("MainWindow.NativeFatPrintPreview.cs"));
        var adapter = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatP4DReportAdapter.cs"));
        var renderer = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatReportPreviewDocumentBuilder.cs"));

        Assert.Contains("NativeFatP4DReportAdapter.Build(currentSnapshot, draft: true)", preview, StringComparison.Ordinal);
        Assert.Contains("IoFatReportPreviewDocumentBuilder.Render(currentLayout)", preview, StringComparison.Ordinal);
        Assert.Contains("new DocumentViewer", preview, StringComparison.Ordinal);
        Assert.Contains("CollapseNativeDocumentViewerChrome(viewer)", preview, StringComparison.Ordinal);
        Assert.Contains("NativePreviewLucideIcon.Printer", preview, StringComparison.Ordinal);
        Assert.Contains("NativePreviewLucideIcon.Minus", preview, StringComparison.Ordinal);
        Assert.Contains("NativePreviewLucideIcon.Plus", preview, StringComparison.Ordinal);
        Assert.Contains("NativePreviewLucideIcon.Maximize2", preview, StringComparison.Ordinal);
        Assert.Contains("NativePreviewLucideIcon.ChevronLeft", preview, StringComparison.Ordinal);
        Assert.Contains("NativePreviewLucideIcon.ChevronRight", preview, StringComparison.Ordinal);
        Assert.Contains("NativePreviewLucideIcon.RefreshCw", preview, StringComparison.Ordinal);
        Assert.Contains("NativePreviewLucideIcon.Save", preview, StringComparison.Ordinal);
        Assert.Contains("NativePreviewLucideIcon.X", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("new DataGrid", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsSource = snapshot.Rows", preview, StringComparison.Ordinal);

        Assert.Contains("IoFatReportLayoutPlan Build", adapter, StringComparison.Ordinal);
        Assert.Contains("public static FixedDocument Render(", renderer, StringComparison.Ordinal);
        Assert.Contains("IoFatReportLayoutPlan layout", renderer, StringComparison.Ordinal);
        Assert.Contains("new FixedDocument()", renderer, StringComparison.Ordinal);
    }

    [Fact]
    public void P4D_SavePdfSerializesTheExactLayoutCurrentlyRenderedInPreview()
    {
        var preview = File.ReadAllText(FindRepoFile("MainWindow.NativeFatPrintPreview.cs"));
        var pdfService = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatPdfReportService.cs"));
        var pdfWriter = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatNativePdfWriter.cs"));

        Assert.Contains("var currentLayout = NativeFatP4DReportAdapter.Build(currentSnapshot, draft: true);", preview, StringComparison.Ordinal);
        Assert.Contains("Document = document", preview, StringComparison.Ordinal);
        Assert.Contains("viewer.Document = IoFatReportPreviewDocumentBuilder.Render(currentLayout)", preview, StringComparison.Ordinal);
        Assert.Contains("BuildNativePreviewLabeledContent(NativePreviewLucideIcon.Save, \"Save PDF\")", preview, StringComparison.Ordinal);
        Assert.Contains("SaveNativeFatPreviewPdf(preview, currentSnapshot, currentLayout)", preview, StringComparison.Ordinal);
        Assert.Contains("IoFatPdfReportService.SaveLayout(", preview, StringComparison.Ordinal);
        Assert.Contains("layout,", preview, StringComparison.Ordinal);
        Assert.Contains("internal static void SaveLayout(", pdfService, StringComparison.Ordinal);
        Assert.Contains("GenerateLayout(layout, reportName, primaryReference)", pdfService, StringComparison.Ordinal);
        Assert.Contains("IoFatNativePdfWriter.Build(layout, reportName, primaryReference)", pdfService, StringComparison.Ordinal);
        Assert.Contains("public static byte[] Build(", pdfWriter, StringComparison.Ordinal);
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
            SignalName = "52_ACB1 Status",
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
            DateTimeOffset.UtcNow.AddSeconds(-1));
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

        Assert.Equal("52_ACB1 Status", snapshot.Rows[0].Signal);
        Assert.Equal("COMPLETE", snapshot.Rows[0].Result);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Rows[0].Value1TimestampText));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Rows[0].Value2TimestampText));
        Assert.True(layout.Pages.Count >= 2);
        Assert.Contains(layout.Pages[^1].Commands.OfType<IoFatReportTextCommand>(), command => command.Text == "TESTED BY");
        Assert.Contains(layout.Pages[^1].Commands.OfType<IoFatReportTextCommand>(), command => command.Text == "WITNESSED BY");
        Assert.Contains(layout.Pages[^1].Commands.OfType<IoFatReportTextCommand>(), command => command.Text == "APPROVED BY");
        Assert.True(bytes.Length > 32);
        Assert.Equal("%PDF-1.4", Encoding.ASCII.GetString(bytes, 0, 8));
    }

    [Fact]
    public void P4D_ReportAdapterLocksExactNineColumnP4CContract()
    {
        var adapter = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatP4DReportAdapter.cs"));
        var snapshot = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatPrintPreviewSnapshot.cs"));

        var signal = adapter.IndexOf("\"Signal\"", StringComparison.Ordinal);
        var telegram = adapter.IndexOf("\"IEC Telegram\"", StringComparison.Ordinal);
        var quality = adapter.IndexOf("\"Quality\"", StringComparison.Ordinal);
        var live = adapter.IndexOf("\"Live Value\"", StringComparison.Ordinal);
        var value1 = adapter.IndexOf("\"Value 1\"", StringComparison.Ordinal);
        var timestamp1 = adapter.IndexOf("\"V1 Timestamp\"", StringComparison.Ordinal);
        var value2 = adapter.IndexOf("\"Value 2\"", StringComparison.Ordinal);
        var timestamp2 = adapter.IndexOf("\"V2 Timestamp\"", StringComparison.Ordinal);
        var result = adapter.IndexOf("\"Result\"", StringComparison.Ordinal);

        Assert.True(signal >= 0);
        Assert.True(telegram > signal);
        Assert.True(quality > telegram);
        Assert.True(live > quality);
        Assert.True(value1 > live);
        Assert.True(timestamp1 > value1);
        Assert.True(value2 > timestamp1);
        Assert.True(timestamp2 > value2);
        Assert.True(result > timestamp2);

        Assert.DoesNotContain("\"Type\"", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Status\"", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("\"IEC 61850 reference\"", adapter, StringComparison.Ordinal);

        Assert.Contains("string IecTelegram", snapshot, StringComparison.Ordinal);
        Assert.Contains("string Quality", snapshot, StringComparison.Ordinal);
        Assert.Contains("string Value1TimestampText", snapshot, StringComparison.Ordinal);
        Assert.Contains("string Value2TimestampText", snapshot, StringComparison.Ordinal);
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
                     "FatSclWorkspaceImportService",
                     "new IoTestProject"
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
