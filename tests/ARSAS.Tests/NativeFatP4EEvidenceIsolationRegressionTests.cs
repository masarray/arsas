using System.Text.Json;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class NativeFatP4EEvidenceIsolationRegressionTests
{
    [Fact]
    public async Task P4E_RestartReorder_CswiEvidenceStaysOnExactTelegramAndNeverMovesToThdPpv()
    {
        var root = TempRoot();
        try
        {
            using var service = new NativeFatEvidenceHydrationService(root);
            var before = Device("runtime-before", "AA1E1F06R4");
            var cswiBefore = Point(
                before,
                "Breaker position",
                "AA1E1F06R4LD0/CSWI1.Pos.stVal",
                "Open [01]",
                "BOOLEAN");
            var thdBefore = Point(
                before,
                "THD phase voltage",
                "AA1E1F06R4LD0/MMXU1.ThdPPV.phsA.cVal.mag.f",
                "2.20",
                "FLOAT32");
            before.Points.Add(cswiBefore);
            before.Points.Add(thdBefore);

            var saved = new NativeFatIedSessionCacheState();
            NativeFatCanonicalEvidenceOverlay.WriteCapture(
                saved,
                cswiBefore,
                NativeFatEvidenceField.Value1,
                "Open [01]",
                ArIED61850Tester.Models.IoTesting.FatEvidenceCaptureKind.AutomaticValue,
                DateTimeOffset.UtcNow);
            NativeFatCanonicalEvidenceOverlay.WriteCapture(
                saved,
                cswiBefore,
                NativeFatEvidenceField.Value2,
                "Closed [10]",
                ArIED61850Tester.Models.IoTesting.FatEvidenceCaptureKind.AutomaticTransition,
                DateTimeOffset.UtcNow);
            await service.SaveAsync(before, saved);

            // Simulate application/runtime recreation plus the exact row-order inversion that
            // previously allowed CSWI evidence to appear on an unrelated THD row.
            var after = Device("runtime-after", "AA1E1F06R4");
            var thdAfter = Point(
                after,
                "THD renamed after restart",
                "AA1E1F06R4LD0/MMXU1.ThdPPV.phsA.cVal.mag.f",
                "2.25",
                "FLOAT32");
            var cswiAfter = Point(
                after,
                "Breaker renamed after restart",
                "AA1E1F06R4LD0/CSWI1.Pos.stVal",
                "Closed [10]",
                "BOOLEAN");
            after.Points.Add(thdAfter);
            after.Points.Add(cswiAfter);

            var hydration = await service.HydrateAsync(after);
            var restored = new NativeFatIedSessionCacheState();
            NativeFatCanonicalEvidenceOverlay.MergeMissing(restored, hydration.EvidenceByRow);

            Assert.True(hydration.Succeeded);
            Assert.Equal("Open [01]", NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, cswiAfter, NativeFatEvidenceField.Value1));
            Assert.Equal("Closed [10]", NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, cswiAfter, NativeFatEvidenceField.Value2));
            Assert.Equal(string.Empty, NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, thdAfter, NativeFatEvidenceField.Value1));
            Assert.Equal(string.Empty, NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, thdAfter, NativeFatEvidenceField.Value2));

            var snapshot = NativeFatPrintPreviewSnapshot.Capture(after, restored);
            Assert.Equal(2, snapshot.Rows.Count);
            Assert.Equal("LD0/MMXU1.ThdPPV.phsA.cVal.mag.f", snapshot.Rows[0].IecTelegram);
            Assert.Equal("LD0/CSWI1.Pos.stVal", snapshot.Rows[1].IecTelegram);
            Assert.Equal("—", snapshot.Rows[0].Value1);
            Assert.StartsWith("Open [01] - ", snapshot.Rows[1].Value1, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void P4E_TwoIedsWithSameSignalNameCannotCrossContaminateEvidence()
    {
        var cache = new NativeFatIedSessionCacheState();
        var iedA = Device("runtime-a", "IED_A");
        var iedB = Device("runtime-b", "IED_B");
        var pointA = Point(iedA, "Breaker position", "IED_ALD0/CSWI1.Pos.stVal", "Open [01]", "BOOLEAN");
        var pointB = Point(iedB, "Breaker position", "IED_BLD0/CSWI1.Pos.stVal", "Closed [10]", "BOOLEAN");

        NativeFatCanonicalEvidenceOverlay.Write(cache, pointA, NativeFatEvidenceField.Result, "PASS-A");
        NativeFatCanonicalEvidenceOverlay.Write(cache, pointB, NativeFatEvidenceField.Result, "PASS-B");

        Assert.NotEqual(
            NativeFatCanonicalEvidenceOverlay.BuildRowKey(pointA),
            NativeFatCanonicalEvidenceOverlay.BuildRowKey(pointB));
        Assert.Equal("PASS-A", NativeFatCanonicalEvidenceOverlay.ReadRaw(cache, pointA, NativeFatEvidenceField.Result));
        Assert.Equal("PASS-B", NativeFatCanonicalEvidenceOverlay.ReadRaw(cache, pointB, NativeFatEvidenceField.Result));
        Assert.Equal(2, cache.EvidenceByRow.Count);
    }

    [Fact]
    public async Task P4E_UnknownPersistedTelegramIsIgnoredAsOrphanAndNeverRemappedByPositionOrName()
    {
        var root = TempRoot();
        try
        {
            using var service = new NativeFatEvidenceHydrationService(root);
            var device = Device("runtime-current", "AA1E1F06R4");
            var cswi = Point(device, "Same display name", "AA1E1F06R4LD0/CSWI1.Pos.stVal", "Open [01]", "BOOLEAN");
            device.Points.Add(cswi);

            Directory.CreateDirectory(root);
            var orphanDocument = new
            {
                schema = "ARSAS-NATIVE-FAT-EVIDENCE-2.0",
                savedAtUtc = DateTimeOffset.UtcNow,
                deviceId = "old-runtime",
                deviceName = device.Name,
                ipAddress = device.IpAddress,
                evidenceByRow = new Dictionary<string, object>
                {
                    ["aa1e1f06r4|ld0/unknown1.pos.stval"] = new
                    {
                        value1 = "SHOULD-NOT-MOVE",
                        value2 = "",
                        result = "PASS"
                    }
                }
            };
            await File.WriteAllTextAsync(
                service.SnapshotPath(device.Name),
                JsonSerializer.Serialize(orphanDocument));

            var hydration = await service.HydrateAsync(device);
            var restored = new NativeFatIedSessionCacheState();
            NativeFatCanonicalEvidenceOverlay.MergeMissing(restored, hydration.EvidenceByRow);

            Assert.True(hydration.Succeeded);
            Assert.True(hydration.SnapshotFound);
            Assert.Equal(0, hydration.LoadedRows);
            Assert.Equal(1, hydration.IgnoredRows);
            Assert.Empty(hydration.EvidenceByRow);
            Assert.Equal(string.Empty, NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, cswi, NativeFatEvidenceField.Value1));
            Assert.Equal(string.Empty, NativeFatCanonicalEvidenceOverlay.ReadRaw(restored, cswi, NativeFatEvidenceField.Result));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("BOOLEAN", "True")]
    [InlineData("FLOAT32", "1247.32 A")]
    [InlineData("DbPos", "Closed [10]")]
    [InlineData("INT32", "Tap 7")]
    public void P4E_DigitalAnalogPositionAndTapEvidenceKeepMillisecondTimestamp(string dataType, string rawValue)
    {
        var device = Device("runtime-types", "AA1E1F06R4");
        var point = Point(device, "Evidence", "AA1E1F06R4LD0/GGIO1.Test.stVal", rawValue, dataType);
        point.DeviceTimestamp = "2026-09-12T06:46:31.958+07:00";
        device.Points.Add(point);
        var cache = new NativeFatIedSessionCacheState();

        NativeFatCanonicalEvidenceOverlay.WriteCapture(
            cache,
            point,
            NativeFatEvidenceField.Value1,
            rawValue,
            ArIED61850Tester.Models.IoTesting.FatEvidenceCaptureKind.AutomaticValue,
            DateTimeOffset.UtcNow);

        var snapshot = NativeFatPrintPreviewSnapshot.Capture(device, cache);
        Assert.Equal($"{rawValue} - 2026-09-12 06:46:31.958", snapshot.Rows[0].Value1);
    }

    [Fact]
    public void P4E_PrintPreviewRowCountAndOrderExactlyFollowCanonicalExplorerRows()
    {
        var device = Device("runtime-order", "AA1E1F06R4");
        device.Points.Add(Point(device, "Third", "AA1E1F06R4LD0/GGIO1.Ind3.stVal", "False", "BOOLEAN"));
        device.Points.Add(Point(device, "First", "AA1E1F06R4LD0/GGIO1.Ind1.stVal", "True", "BOOLEAN"));
        device.Points.Add(Point(device, "Second", "AA1E1F06R4LD0/GGIO1.Ind2.stVal", "False", "BOOLEAN"));

        var snapshot = NativeFatPrintPreviewSnapshot.Capture(device, new NativeFatIedSessionCacheState());

        Assert.Equal(device.Points.Count, snapshot.Rows.Count);
        Assert.Equal(
            device.Points.Select(point => point.IecTelegram),
            snapshot.Rows.Select(row => row.IecTelegram));
        Assert.Equal(
            device.Points.Select(point => point.SignalName),
            snapshot.Rows.Select(row => row.Signal));
    }

    [Fact]
    public void P4E_RecycledEvidenceColumnReadsTheCurrentRowObjectNotIndexOrSignalName()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.NativeFatCanonicalGrid.cs"));
        var classStart = source.IndexOf("private sealed class NativeFatEvidenceColumn", StringComparison.Ordinal);
        Assert.True(classStart >= 0);
        var evidenceColumn = source[classStart..];

        Assert.Contains("dataItem is Iec61850MonitorPoint point", evidenceColumn, StringComparison.Ordinal);
        Assert.Contains("_owner.ReadNativeFatEvidence(point, Field)", evidenceColumn, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedIndex", evidenceColumn, StringComparison.Ordinal);
        Assert.DoesNotContain("SignalName", evidenceColumn, StringComparison.Ordinal);
        Assert.DoesNotContain("Items.IndexOf", evidenceColumn, StringComparison.Ordinal);
    }

    private static Iec61850MonitorDevice Device(string deviceId, string name)
        => new()
        {
            DeviceId = deviceId,
            Name = name,
            IpAddress = "192.168.81.103",
            Port = 102,
            IsConnected = true,
            IsMonitoring = true
        };

    private static Iec61850MonitorPoint Point(
        Iec61850MonitorDevice device,
        string signalName,
        string reference,
        string value,
        string dataType)
        => new()
        {
            DeviceId = device.DeviceId,
            DeviceName = device.Name,
            SignalName = signalName,
            IecReference = reference,
            IecDataType = dataType,
            Quality = "Good",
            Status = "Live",
            SourceMode = "Static DataSet reporting",
            Value = value
        };

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "arsas-native-fat-p4e-" + Guid.NewGuid().ToString("N"));
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
