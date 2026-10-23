using System.Text.Json;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class NativeFatStateSafetyRegressionTests
{
    [Fact]
    public void Capture_RequiresARealRuntimeObservation()
    {
        var point = new Iec61850MonitorPoint
        {
            DeviceId = "runtime-1",
            DeviceName = "IED_A",
            SignalName = "CSWI Pos",
            IecReference = "Q0/CSWI1.Pos.stVal",
            FunctionalConstraint = "ST",
            IecDataType = "DbPos",
            Value = "Closed [10]",
            Quality = "Good",
            Status = "Live",
            SourceMode = "IEC 61850 report",
            DeviceTimestamp = "2026-09-14T10:00:00.000+07:00"
        };
        var state = new NativeFatSignalState
        {
            Key = NativeFatIdentity.BuildKey(point),
            SignalName = point.SignalName,
            IecReference = point.IecReference,
            FunctionalConstraint = point.FunctionalConstraint,
            DataType = point.IecDataType
        };

        using var row = new NativeFatSignalRow(state, sourceSignal: null, sourcePoint: point);

        // A projected/default value is not evidence until the runtime sequence advances.
        Assert.False(row.CanCapture);
        Assert.False(row.CaptureValue(1));
        Assert.Null(state.Value1);
        Assert.Empty(state.History);

        point.Sequence = 1;
        point.Status = "Queued";
        Assert.False(row.CanCapture);

        // First real live observation unlocks capture and preserves runtime provenance.
        point.Status = "Live";
        Assert.True(row.CanCapture);
        Assert.True(row.CaptureValue(1));
        Assert.NotNull(state.Value1);
        Assert.Equal("Closed [10]", state.Value1!.Value);
        Assert.Equal(1, state.Value1.Sequence);
        Assert.Equal("IEC 61850 report", state.Value1.SourceMode);
    }

    [Fact]
    public async Task State_ResumesAcrossRuntimeDeviceIdChange_ByStableSclIdentity()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new NativeFatStateStore(root);
            var before = Device("runtime-before", "10.1.1.10");
            before.SclSourceSha256 = new string('A', 64);
            before.SclIedName = "IED_A";
            before.SclAccessPointName = "AP1";

            var saved = await store.LoadAsync(before);
            saved.Signals.Add(new NativeFatSignalState
            {
                Key = "Q0/CSWI1.POS.STVAL|ST",
                SignalName = "CSWI Pos",
                IecReference = "Q0/CSWI1.Pos.stVal",
                FunctionalConstraint = "ST",
                DataType = "DbPos",
                Result = NativeFatResult.Pass
            });
            await store.SaveAsync(saved);
            var savedPath = saved.StoragePath;

            var after = Device("runtime-after", "10.9.9.99");
            after.SclSourceSha256 = new string('A', 64);
            after.SclIedName = "IED_A";
            after.SclAccessPointName = "AP1";

            var restored = await store.LoadAsync(after);

            Assert.Equal(2, restored.SchemaVersion);
            Assert.Equal("runtime-after", restored.DeviceId);
            Assert.Equal(NativeFatStateStore.BuildPersistenceIdentity(after), restored.PersistenceIdentity);
            Assert.Equal(savedPath, restored.StoragePath);
            Assert.Single(restored.Signals);
            Assert.Equal(NativeFatResult.Pass, restored.Signals[0].Result);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task State_ResumesAcrossRuntimeDeviceIdChange_ByEndpointIdentity()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new NativeFatStateStore(root);
            var before = Device("runtime-before", "192.168.81.103");
            var saved = await store.LoadAsync(before);
            saved.Signals.Add(new NativeFatSignalState
            {
                Key = "Q0/XSWI1.POS.STVAL|ST",
                SignalName = "XSWI Pos",
                IecReference = "Q0/XSWI1.Pos.stVal",
                FunctionalConstraint = "ST",
                Result = NativeFatResult.Review
            });
            await store.SaveAsync(saved);

            var after = Device("runtime-after", "192.168.81.103");
            var restored = await store.LoadAsync(after);

            Assert.Equal("runtime-after", restored.DeviceId);
            Assert.Equal(NativeFatResult.Review, Assert.Single(restored.Signals).Result);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task AmbiguousLegacyNameMatch_IsNeverGuessed()
    {
        var root = CreateTempRoot();
        try
        {
            var legacyA = new NativeFatDeviceState
            {
                SchemaVersion = 1,
                DeviceId = "old-a",
                PersistenceIdentity = string.Empty,
                IedName = "IED_A",
                Signals = new List<NativeFatSignalState>
                {
                    new() { Key = "A|ST", SignalName = "A", Result = NativeFatResult.Pass }
                }
            };
            var legacyB = new NativeFatDeviceState
            {
                SchemaVersion = 1,
                DeviceId = "old-b",
                PersistenceIdentity = string.Empty,
                IedName = "IED_A",
                Signals = new List<NativeFatSignalState>
                {
                    new() { Key = "B|ST", SignalName = "B", Result = NativeFatResult.Fail }
                }
            };
            await File.WriteAllTextAsync(
                Path.Combine(root, "legacy-a.json"),
                JsonSerializer.Serialize(legacyA));
            await File.WriteAllTextAsync(
                Path.Combine(root, "legacy-b.json"),
                JsonSerializer.Serialize(legacyB));

            var store = new NativeFatStateStore(root);
            var current = Device("runtime-new", "192.168.81.104");
            var loaded = await store.LoadAsync(current);

            Assert.Empty(loaded.Signals);
            Assert.Equal("runtime-new", loaded.DeviceId);
            Assert.Equal(2, loaded.SchemaVersion);
            Assert.Equal(NativeFatStateStore.BuildPersistenceIdentity(current), loaded.PersistenceIdentity);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static Iec61850MonitorDevice Device(string deviceId, string ipAddress)
        => new()
        {
            DeviceId = deviceId,
            Name = "IED_A",
            IpAddress = ipAddress,
            Port = 102
        };

    private static string CreateTempRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "arsas-native-fat-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
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
