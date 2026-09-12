using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class NativeFatP4BStructuredTimestampEvidenceTests
{
    [Fact]
    public void Arm_CapturesValueWithRelayTimestampQualitySourceAndSequence()
    {
        using var coordinator = new NativeFatArmCoordinator();
        var device = Device("runtime-a");
        var point = Point(
            device.DeviceId,
            "CSWI.Pos",
            "AA1E1F06R4LD0/CSWI1.Pos.stVal",
            "True",
            "2026-09-12T06:46:31.958+07:00",
            sequence: 42);
        device.Points.Add(point);
        var cache = new NativeFatIedSessionCacheState();

        var result = coordinator.Arm(device, cache);
        var evidence = NativeFatCanonicalEvidenceOverlay.ReadCapture(
            cache,
            point,
            NativeFatEvidenceField.Value1);

        Assert.True(result.Succeeded);
        Assert.NotNull(evidence);
        Assert.Equal("True", evidence!.RawValue);
        Assert.Equal("Good", evidence.Quality);
        Assert.Equal("Static DataSet reporting", evidence.AcquisitionSource);
        Assert.Equal(42, evidence.Sequence);
        Assert.Equal(31, evidence.IedTimestamp!.Value.Second);
        Assert.Equal(958, evidence.IedTimestamp.Value.Millisecond);
        Assert.Equal(
            "True - 2026-09-12 06:46:31.958",
            NativeFatCanonicalEvidenceOverlay.Read(cache, point, NativeFatEvidenceField.Value1));
        Assert.Equal(
            "True",
            NativeFatCanonicalEvidenceOverlay.ReadRaw(cache, point, NativeFatEvidenceField.Value1));
    }

    [Fact]
    public void CaptureWithoutRelayTimestamp_UsesArsasCaptureTimeAsDisplayFallback()
    {
        using var coordinator = new NativeFatArmCoordinator();
        var device = Device("runtime-fallback");
        var point = Point(
            device.DeviceId,
            "Analog current",
            "AA1E1F06R4MEAS/MMXU1.A.phsA.cVal.mag.f",
            "1247.32",
            "-",
            sequence: 7);
        device.Points.Add(point);
        var cache = new NativeFatIedSessionCacheState();
        var before = DateTimeOffset.Now.AddSeconds(-1);

        Assert.True(coordinator.Arm(device, cache).Succeeded);
        var after = DateTimeOffset.Now.AddSeconds(1);
        var evidence = NativeFatCanonicalEvidenceOverlay.ReadCapture(
            cache,
            point,
            NativeFatEvidenceField.Value1);

        Assert.NotNull(evidence);
        Assert.Null(evidence!.IedTimestamp);
        Assert.InRange(evidence.CapturedAt, before, after);
        Assert.Equal(
            $"1247.32 - {evidence.CapturedAt:yyyy-MM-dd HH:mm:ss.fff}",
            NativeFatCanonicalEvidenceOverlay.Read(cache, point, NativeFatEvidenceField.Value1));
    }

    [Fact]
    public void RollingPair_PreservesOriginalTimestampWhenValue2BecomesValue1()
    {
        using var coordinator = new NativeFatArmCoordinator();
        var device = Device("runtime-roll");
        var point = Point(
            device.DeviceId,
            "Breaker position",
            "AA1E1F06R4LD0/CSWI1.Pos.stVal",
            "Open [01]",
            "2026-09-12T06:46:30.100+07:00",
            sequence: 1);
        device.Points.Add(point);
        var cache = new NativeFatIedSessionCacheState();
        Assert.True(coordinator.Arm(device, cache).Succeeded);

        point.DeviceTimestamp = "2026-09-12T06:46:31.200+07:00";
        point.Sequence = 2;
        point.Value = "Closed [10]";

        point.DeviceTimestamp = "2026-09-12T06:46:32.300+07:00";
        point.Sequence = 3;
        point.Value = "Open [01]";

        var value1 = NativeFatCanonicalEvidenceOverlay.ReadCapture(cache, point, NativeFatEvidenceField.Value1);
        var value2 = NativeFatCanonicalEvidenceOverlay.ReadCapture(cache, point, NativeFatEvidenceField.Value2);

        Assert.NotNull(value1);
        Assert.NotNull(value2);
        Assert.Equal("Closed [10]", value1!.RawValue);
        Assert.Equal(200, value1.IedTimestamp!.Value.Millisecond);
        Assert.Equal("Open [01]", value2!.RawValue);
        Assert.Equal(300, value2.IedTimestamp!.Value.Millisecond);
        Assert.Equal(
            "Closed [10] - 2026-09-12 06:46:31.200",
            NativeFatCanonicalEvidenceOverlay.Read(cache, point, NativeFatEvidenceField.Value1));
        Assert.Equal(
            "Open [01] - 2026-09-12 06:46:32.300",
            NativeFatCanonicalEvidenceOverlay.Read(cache, point, NativeFatEvidenceField.Value2));
    }

    [Fact]
    public async Task PersistHydrate_UsesStableIedNameAndPreservesStructuredEvidenceAcrossRuntimeDeviceId()
    {
        var root = TempRoot();
        try
        {
            using var service = new NativeFatEvidenceHydrationService(root);
            using var coordinator = new NativeFatArmCoordinator();
            var before = Device("runtime-before");
            var beforePoint = Point(
                before.DeviceId,
                "CSWI.Pos",
                "AA1E1F06R4LD0/CSWI1.Pos.stVal",
                "True",
                "2026-09-12T06:46:31.958+07:00",
                sequence: 77);
            before.Points.Add(beforePoint);
            var saved = new NativeFatIedSessionCacheState();
            Assert.True(coordinator.Arm(before, saved).Succeeded);
            await service.SaveAsync(before, saved);

            var recreated = Device("runtime-after");
            var recreatedPoint = Point(
                recreated.DeviceId,
                "renamed display",
                "AA1E1F06R4LD0/CSWI1.Pos.stVal",
                "False",
                "2026-09-12T07:00:00.000+07:00",
                sequence: 1);
            recreated.Points.Add(recreatedPoint);

            var hydration = await service.HydrateAsync(recreated);
            var restored = new NativeFatIedSessionCacheState();
            NativeFatCanonicalEvidenceOverlay.MergeMissing(restored, hydration.EvidenceByRow);
            var capture = NativeFatCanonicalEvidenceOverlay.ReadCapture(
                restored,
                recreatedPoint,
                NativeFatEvidenceField.Value1);

            Assert.True(hydration.Succeeded);
            Assert.True(hydration.SnapshotFound);
            Assert.Equal(1, hydration.LoadedRows);
            Assert.NotNull(capture);
            Assert.Equal("True", capture!.RawValue);
            Assert.Equal(77, capture.Sequence);
            Assert.Equal(958, capture.IedTimestamp!.Value.Millisecond);
            Assert.Equal(
                "True - 2026-09-12 06:46:31.958",
                NativeFatCanonicalEvidenceOverlay.Read(restored, recreatedPoint, NativeFatEvidenceField.Value1));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void UntouchedRenderedEvidenceCommit_DoesNotRecaptureOrChangeTimestamp()
    {
        var point = Point(
            "runtime-edit",
            "Trip",
            "AA1E1F06R4LD0/PTRC1.Tr.general",
            "False",
            "2026-09-12T08:01:02.345+07:00",
            sequence: 9);
        var cache = new NativeFatIedSessionCacheState();
        NativeFatCanonicalEvidenceOverlay.WriteCapture(
            cache,
            point,
            NativeFatEvidenceField.Value1,
            "False",
            ArIED61850Tester.Models.IoTesting.FatEvidenceCaptureKind.AutomaticValue,
            new DateTimeOffset(2026, 9, 12, 8, 1, 3, TimeSpan.FromHours(7)));
        var before = NativeFatCanonicalEvidenceOverlay.ReadCapture(cache, point, NativeFatEvidenceField.Value1);
        var rendered = NativeFatCanonicalEvidenceOverlay.Read(cache, point, NativeFatEvidenceField.Value1);

        NativeFatCanonicalEvidenceOverlay.Write(cache, point, NativeFatEvidenceField.Value1, rendered);
        var after = NativeFatCanonicalEvidenceOverlay.ReadCapture(cache, point, NativeFatEvidenceField.Value1);

        Assert.Same(before, after);
        Assert.Equal("False - 2026-09-12 08:01:02.345", rendered);
    }

    private static Iec61850MonitorDevice Device(string deviceId)
        => new()
        {
            DeviceId = deviceId,
            Name = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            Port = 102,
            IsConnected = true,
            IsMonitoring = true
        };

    private static Iec61850MonitorPoint Point(
        string deviceId,
        string signalName,
        string reference,
        string value,
        string deviceTimestamp,
        long sequence)
        => new()
        {
            DeviceId = deviceId,
            DeviceName = "AA1E1F06R4",
            SignalName = signalName,
            IecReference = reference,
            IecDataType = "BOOLEAN",
            Quality = "Good",
            DeviceTimestamp = deviceTimestamp,
            Status = "Live",
            SourceMode = "Static DataSet reporting",
            Sequence = sequence,
            Value = value
        };

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "arsas-native-fat-p4b-" + Guid.NewGuid().ToString("N"));
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
}
