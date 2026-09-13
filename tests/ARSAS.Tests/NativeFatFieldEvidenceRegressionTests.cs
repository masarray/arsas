using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class NativeFatFieldEvidenceRegressionTests
{
    [Fact]
    public async Task RestartWithNewRuntimeDeviceId_RestoresOnlyExactIedNameAndTelegram()
    {
        var root = TempRoot();
        try
        {
            using var service = new NativeFatEvidenceHydrationService(root);

            var before = Device("runtime-before");
            var cswiBefore = Point(before, "CSWI Pos", "Q0/CSWI1.Pos.stVal", "Open [01]");
            var sfBefore = Point(before, "SF62ndCB", "ADD/GGIO5.SF62ndCB.stVal", "false");
            before.Points.Add(cswiBefore);
            before.Points.Add(sfBefore);

            var saved = new NativeFatIedSessionCacheState();
            NativeFatCanonicalEvidenceOverlay.WriteCapture(
                saved,
                cswiBefore,
                NativeFatEvidenceField.Value1,
                "Open [01]",
                ArIED61850Tester.Models.IoTesting.FatEvidenceCaptureKind.AutomaticValue,
                DateTimeOffset.Parse("2026-09-13T11:01:37.116+07:00"));
            NativeFatCanonicalEvidenceOverlay.WriteCapture(
                saved,
                cswiBefore,
                NativeFatEvidenceField.Value2,
                "Closed [10]",
                ArIED61850Tester.Models.IoTesting.FatEvidenceCaptureKind.AutomaticTransition,
                DateTimeOffset.Parse("2026-09-13T11:01:51.329+07:00"));
            await service.SaveAsync(before, saved);

            var after = Device("runtime-after");
            var sfAfter = Point(after, "SF62ndCB", "ADD/GGIO5.SF62ndCB.stVal", "false");
            var cswiAfter = Point(after, "CSWI Pos", "Q0/CSWI1.Pos.stVal", "Closed [10]");
            after.Points.Add(sfAfter);
            after.Points.Add(cswiAfter);

            var hydration = await service.HydrateAsync(after);
            var restored = new NativeFatIedSessionCacheState();
            NativeFatCanonicalEvidenceOverlay.MergeMissing(restored, hydration.EvidenceByRow);

            Assert.True(hydration.Succeeded);
            Assert.True(hydration.SnapshotFound);
            Assert.Equal("Open [01]", NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, cswiAfter, NativeFatEvidenceField.Value1));
            Assert.Equal("Closed [10]", NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, cswiAfter, NativeFatEvidenceField.Value2));
            Assert.Equal(string.Empty, NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, sfAfter, NativeFatEvidenceField.Value1));
            Assert.Equal(string.Empty, NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, sfAfter, NativeFatEvidenceField.Value2));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void RecycledGridRows_ClearStaleEvidenceAndRefreshFromCurrentCanonicalPoint()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.NativeFatFieldEvidenceFixes.cs"));

        Assert.Contains("row.DataContextChanged += NativeFatFieldEvidence_RowDataContextChanged", source, StringComparison.Ordinal);
        Assert.Contains("ClearNativeFatEvidenceRowVisual(row)", source, StringComparison.Ordinal);
        Assert.Contains("row.Item is not Iec61850MonitorPoint point", source, StringComparison.Ordinal);
        Assert.Contains("RefreshNativeFatEvidenceCells(point)", source, StringComparison.Ordinal);
        Assert.Contains("IEDName + IEC Telegram", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedIndex", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SignalName", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationClosing_FlushesEvidenceBeforeClosedCleanupCanCancelDebounce()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.NativeFatFieldEvidenceFixes.cs"));
        var production = File.ReadAllText(FindRepoFile("MainWindow.ProductionFatTab.cs"));

        Assert.Contains("window.Closing += window.NativeFatFieldEvidence_WindowClosing", source, StringComparison.Ordinal);
        Assert.Contains("FlushNativeFatEvidenceBeforeShutdown()", source, StringComparison.Ordinal);
        Assert.Contains("SaveAsync(device, cache, CancellationToken.None)", source, StringComparison.Ordinal);
        Assert.Contains("Closed += ProductionFat_MainWindowClosed", production, StringComparison.Ordinal);
        Assert.Contains("DisposeNativeFatArmCoordinator()", production, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportBranding_UsesIconOnly_LowersLogo_AndSignOffHasNoScopeCard()
    {
        var image = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatReportImage.cs"));
        var finalization = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatReportFinalization.cs"));

        Assert.Contains("private const double NativeLogoTop = 576d", image, StringComparison.Ordinal);
        Assert.Contains("legacyWordmark", image, StringComparison.Ordinal);
        Assert.Contains("string.Equals(text.Text, \"ARSAS\"", image, StringComparison.Ordinal);
        Assert.DoesNotContain("IED / REPORT SCOPE", finalization, StringComparison.Ordinal);
        Assert.DoesNotContain("FOR FAT RECORD", finalization, StringComparison.Ordinal);
    }

    private static Iec61850MonitorDevice Device(string deviceId)
        => new()
        {
            DeviceId = deviceId,
            Name = "AA1EIF06R4",
            IpAddress = "192.168.81.103",
            Port = 102,
            IsConnected = true,
            IsMonitoring = true
        };

    private static Iec61850MonitorPoint Point(
        Iec61850MonitorDevice device,
        string signal,
        string reference,
        string value)
        => new()
        {
            DeviceId = device.DeviceId,
            DeviceName = device.Name,
            SignalName = signal,
            IecReference = reference,
            IecDataType = "DbPos",
            Quality = "Good",
            Status = "Live",
            SourceMode = "IEC 61850 report",
            Value = value,
            DeviceTimestamp = "2026-09-13T11:01:51.329+07:00"
        };

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "arsas-native-fat-field-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
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
