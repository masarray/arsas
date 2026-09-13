using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class NativeFatEvidenceDurabilityRegressionTests
{
    [Fact]
    public async Task IedOwnedStore_LoadsBeforeRowsExist_AndSurvivesImmediateTeardown()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "arsas-native-fat-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            using var store = new NativeFatEvidenceStore(root);
            var coordinator = new NativeFatEvidencePersistenceCoordinator(store);

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

            // Capture evidence first, then tear down every Engineering row immediately.
            // The worker must never need before.Points again.
            coordinator.Queue(NativeFatEvidenceDurabilitySnapshot.Capture(before, cache));
            before.Points.Clear();
            await coordinator.DrainAsync(before.Name);

            var path = store.SnapshotPath(before.Name);
            Assert.Equal("AA1EIF06R4.native-fat-evidence.json", Path.GetFileName(path));
            Assert.True(File.Exists(path));

            var json = await File.ReadAllTextAsync(path);
            Assert.Contains("\"deviceName\":\"AA1EIF06R4\"", json, StringComparison.Ordinal);
            Assert.Contains("\"value1\":\"Closed [10]\"", json, StringComparison.Ordinal);
            Assert.Contains("\"value2\":\"Open [01]\"", json, StringComparison.Ordinal);
            Assert.Contains("\"result\":\"COMPLETE\"", json, StringComparison.Ordinal);

            // Reopen creates a different runtime DeviceId. Load happens before canonical rows
            // exist and therefore cannot depend on Start FAT, Points, row order or selection.
            var after = Device("runtime-after");
            var load = await store.LoadAsync(after.Name);
            Assert.True(load.Succeeded);
            Assert.True(load.SnapshotFound);
            Assert.Equal(1, load.LoadedRows);

            var restored = new NativeFatIedSessionCacheState();
            NativeFatCanonicalEvidenceOverlay.MergeMissing(restored, load.EvidenceByRow);

            // Canonical rows materialize later from the SCL/Engineering workspace.
            var thdAfter = Point(
                after,
                "ThdPPV PhsBC",
                "VI3p1_THDHarmonics/V_MHAI1.ThdPPV.phsBC.cVal.mag.f",
                "0");
            var cswiAfter = Point(after, "CSWI Pos", "Q0/CSWI1.Pos.stVal", "Open [01]");
            after.Points.Add(thdAfter);
            after.Points.Add(cswiAfter);

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
                "COMPLETE",
                NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, cswiAfter, NativeFatEvidenceField.Result));

            Assert.Equal(
                string.Empty,
                NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, thdAfter, NativeFatEvidenceField.Value1));
            Assert.Equal(
                string.Empty,
                NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, thdAfter, NativeFatEvidenceField.Value2));

            var preview = NativeFatPrintPreviewSnapshot.Capture(after, restored);
            var previewCswi = Assert.Single(
                preview.Rows.Where(row => row.IecTelegram.Equals(cswiAfter.IecTelegram, StringComparison.OrdinalIgnoreCase)));
            var expectedV1 = DateTimeOffset.Parse("2026-09-13T14:10:11.123+07:00")
                .ToLocalTime()
                .ToString("dd/MM/yyyy HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
            var expectedV2 = DateTimeOffset.Parse("2026-09-13T14:10:19.456+07:00")
                .ToLocalTime()
                .ToString("dd/MM/yyyy HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal("Closed [10]", previewCswi.Value1);
            Assert.Equal(expectedV1, previewCswi.Value1TimestampText);
            Assert.Equal("Open [01]", previewCswi.Value2);
            Assert.Equal(expectedV2, previewCswi.Value2TimestampText);
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
    public async Task LoadedPair_StartFatDoesNotOverwriteUntilARealTransitionOccurs()
    {
        var device = Device("runtime-reopen");
        var point = Point(device, "CSWI Pos", "Q0/CSWI1.Pos.stVal", "Open [01]");
        device.Points.Add(point);

        var cache = new NativeFatIedSessionCacheState();
        NativeFatCanonicalEvidenceOverlay.WriteCapture(
            cache,
            point,
            NativeFatEvidenceField.Value1,
            "Closed [10]",
            FatEvidenceCaptureKind.AutomaticValue,
            DateTimeOffset.Parse("2026-09-13T14:10:11.123+07:00"));
        NativeFatCanonicalEvidenceOverlay.WriteCapture(
            cache,
            point,
            NativeFatEvidenceField.Value2,
            "Open [01]",
            FatEvidenceCaptureKind.AutomaticTransition,
            DateTimeOffset.Parse("2026-09-13T14:10:19.456+07:00"));

        using var arm = new NativeFatArmCoordinator();
        var changes = new List<NativeFatEvidenceChangedEventArgs>();
        arm.EvidenceChanged += (_, e) => changes.Add(e);

        var armed = arm.Arm(device, cache);
        Assert.True(armed.Succeeded);
        Assert.Equal(0, armed.SeededValue1Rows);
        Assert.Empty(changes);
        Assert.Equal("Closed [10]", NativeFatCanonicalEvidenceOverlay.ReadRaw(cache, point, NativeFatEvidenceField.Value1));
        Assert.Equal("Open [01]", NativeFatCanonicalEvidenceOverlay.ReadRaw(cache, point, NativeFatEvidenceField.Value2));

        point.Value = "Closed [10]";
        await Task.Delay(25);

        Assert.Equal("Open [01]", NativeFatCanonicalEvidenceOverlay.ReadRaw(cache, point, NativeFatEvidenceField.Value1));
        Assert.Equal("Closed [10]", NativeFatCanonicalEvidenceOverlay.ReadRaw(cache, point, NativeFatEvidenceField.Value2));
        Assert.Equal("OK", NativeFatCanonicalEvidenceOverlay.Read(cache, point, NativeFatEvidenceField.Result));
        Assert.NotEmpty(changes);
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
