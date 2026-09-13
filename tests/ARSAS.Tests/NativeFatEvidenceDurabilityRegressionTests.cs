using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class NativeFatEvidenceDurabilityRegressionTests
{
    [Fact]
    public async Task ImmediateIedTeardown_PersistsAndRehydratesCapturedEvidenceAndPreview()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "arsas-native-fat-durability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            using var service = new NativeFatEvidenceHydrationService(root);
            var coordinator = new NativeFatEvidencePersistenceCoordinator(service);

            var before = Device("runtime-before");
            var cswiBefore = Point(before, "CSWI Pos", "Q0/CSWI1.Pos.stVal", "Closed [10]");
            var thdBefore = Point(
                before,
                "ThdPPV PhsBC",
                "VI3p1_THDHarmonics/V_MHAI1.ThdPPV.phsBC.cVal.mag.f",
                "0");
            before.Points.Add(cswiBefore);
            before.Points.Add(thdBefore);

            var cache = new NativeFatIedSessionCacheState();
            cswiBefore.DeviceTimestamp = "2026-09-13T14:10:11.123+07:00";
            NativeFatCanonicalEvidenceOverlay.WriteCapture(
                cache,
                cswiBefore,
                NativeFatEvidenceField.Value1,
                "Closed [10]",
                FatEvidenceCaptureKind.AutomaticValue,
                DateTimeOffset.Parse("2026-09-13T14:10:11.123+07:00"));

            cswiBefore.DeviceTimestamp = "2026-09-13T14:10:19.456+07:00";
            NativeFatCanonicalEvidenceOverlay.WriteCapture(
                cache,
                cswiBefore,
                NativeFatEvidenceField.Value2,
                "Open [01]",
                FatEvidenceCaptureKind.AutomaticTransition,
                DateTimeOffset.Parse("2026-09-13T14:10:19.456+07:00"));

            // This is the field failure sequence: freeze at capture, then Engineering removes
            // the old canonical rows immediately while persistence continues in the worker.
            var frozen = NativeFatEvidenceDurabilitySnapshot.Capture(before, cache);
            coordinator.Queue(frozen);
            before.Points.Clear();
            await coordinator.DrainAsync(before.Name);

            var after = Device("runtime-after");
            var thdAfter = Point(
                after,
                "ThdPPV PhsBC",
                "VI3p1_THDHarmonics/V_MHAI1.ThdPPV.phsBC.cVal.mag.f",
                "0");
            var cswiAfter = Point(after, "CSWI Pos", "Q0/CSWI1.Pos.stVal", "Open [01]");
            after.Points.Add(thdAfter);
            after.Points.Add(cswiAfter);

            var hydration = await service.HydrateAsync(after);
            var restored = new NativeFatIedSessionCacheState();
            NativeFatCanonicalEvidenceOverlay.MergeMissing(restored, hydration.EvidenceByRow);

            Assert.True(hydration.Succeeded);
            Assert.True(hydration.SnapshotFound);
            Assert.Equal(
                "Closed [10]",
                NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, cswiAfter, NativeFatEvidenceField.Value1));
            Assert.Equal(
                "2026-09-13 14:10:11.123",
                NativeFatCanonicalEvidenceOverlay.Read(restored, cswiAfter, NativeFatEvidenceField.Value1Timestamp));
            Assert.Equal(
                "Open [01]",
                NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, cswiAfter, NativeFatEvidenceField.Value2));
            Assert.Equal(
                "2026-09-13 14:10:19.456",
                NativeFatCanonicalEvidenceOverlay.Read(restored, cswiAfter, NativeFatEvidenceField.Value2Timestamp));
            Assert.Equal(
                "OK",
                NativeFatCanonicalEvidenceOverlay.Read(restored, cswiAfter, NativeFatEvidenceField.Result));

            Assert.Equal(
                string.Empty,
                NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, thdAfter, NativeFatEvidenceField.Value1));
            Assert.Equal(
                string.Empty,
                NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, thdAfter, NativeFatEvidenceField.Value2));
            Assert.Equal(
                string.Empty,
                NativeFatCanonicalEvidenceOverlay.Read(restored, thdAfter, NativeFatEvidenceField.Result));

            var preview = NativeFatPrintPreviewSnapshot.Capture(after, restored);
            var previewCswi = Assert.Single(
                preview.Rows.Where(row => row.IecTelegram.Equals(cswiAfter.IecTelegram, StringComparison.OrdinalIgnoreCase)));
            Assert.Equal("Closed [10]", previewCswi.Value1);
            Assert.Equal("2026-09-13 14:10:11.123", previewCswi.Value1TimestampText);
            Assert.Equal("Open [01]", previewCswi.Value2);
            Assert.Equal("2026-09-13 14:10:19.456", previewCswi.Value2TimestampText);
            Assert.Equal("COMPLETE", previewCswi.Result);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void ReleaseGuard_RemovesObsoleteEngineeringCompatibilityButtonFromShippedUi()
    {
        var root = FindRepoRoot();
        var guard = File.ReadAllText(Path.Combine(root, "IoListTestingWindow.ReleaseNavigationGuard.cs"));

        Assert.Contains("\"Engineering\"", guard, StringComparison.Ordinal);
        Assert.Contains("Visibility.Collapsed", guard, StringComparison.Ordinal);
        Assert.Contains("button.IsEnabled = false", guard, StringComparison.Ordinal);
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
            IpAddress = device.IpAddress,
            SignalName = signal,
            IecReference = reference,
            IecDataType = "DbPos",
            Quality = "Good",
            Status = "Live",
            SourceMode = "IEC 61850 report",
            Value = value,
            DeviceTimestamp = "2026-09-13T14:10:00.000+07:00"
        };

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
