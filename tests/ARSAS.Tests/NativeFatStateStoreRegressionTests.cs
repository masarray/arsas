using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class NativeFatStateStoreRegressionTests
{
    [Fact]
    public void Reconcile_AddRenameRemoveReadd_PreservesExistingEvidence()
    {
        var device = CreateDevice("device-a", "Relay A");
        var original = CreateSignal("Trip original", "IED1LD0/GGIO1.Ind1.stVal");
        device.Signals.Add(original);

        var state = new NativeFatDeviceState();
        NativeFatStateStore.Reconcile(state, device);

        var saved = Assert.Single(state.Signals);
        Assert.Equal(NativeFatResult.Untested, saved.Result);
        Assert.False(saved.IsHistorical);

        using (var row = Assert.Single(NativeFatStateStore.BuildRows(state, device)))
        {
            original.Value = "True";
            original.Quality = "Good";
            original.DeviceTimestamp = "2026-09-09 08:15:01.125";
            Assert.True(row.CaptureValue(1));
            row.SetResult(NativeFatResult.Pass);
        }

        original.Name = "Trip renamed";
        NativeFatStateStore.Reconcile(state, device);
        saved = Assert.Single(state.Signals);
        Assert.Equal("Trip renamed", saved.SignalName);
        Assert.Equal(NativeFatResult.Pass, saved.Result);
        Assert.Equal("True", saved.Value1?.Value);
        Assert.False(saved.IsHistorical);

        device.Signals.Clear();
        NativeFatStateStore.Reconcile(state, device);
        saved = Assert.Single(state.Signals);
        Assert.True(saved.IsHistorical);
        Assert.Equal(NativeFatResult.Pass, saved.Result);
        Assert.Equal("True", saved.Value1?.Value);

        // Same IEC identity, different case and '$' separator: this must restore the old
        // FAT row rather than creating a second UNTESTED row.
        device.Signals.Add(CreateSignal("Trip re-added", "ied1ld0/GGIO1$Ind1$stVal"));
        NativeFatStateStore.Reconcile(state, device);
        saved = Assert.Single(state.Signals);
        Assert.False(saved.IsHistorical);
        Assert.Equal("Trip re-added", saved.SignalName);
        Assert.Equal(NativeFatResult.Pass, saved.Result);
        Assert.Equal("True", saved.Value1?.Value);
    }

    [Fact]
    public void Reconcile_ExplorerSelectionChange_PreservesEvidenceAndRestoresOnReselect()
    {
        var device = CreateDevice("device-selection", "Selection IED");
        var first = CreateSignal("DI 1", "IED1LD0/GGIO1.Ind1.stVal");
        var second = CreateSignal("DI 2", "IED1LD0/GGIO1.Ind2.stVal");
        device.Signals.Add(first);
        device.Signals.Add(second);

        var state = new NativeFatDeviceState();
        NativeFatStateStore.Reconcile(state, device);
        Assert.Equal(2, state.Signals.Count);

        var firstState = state.Signals.Single(signal =>
            signal.Key == NativeFatIdentity.BuildKey(first));
        firstState.Result = NativeFatResult.Pass;
        firstState.Value1 = new NativeFatCapture
        {
            Value = "False",
            Quality = "Good",
            SourceMode = "BRCB"
        };

        // Keep DI 2 selected so Explorer has an explicit selected-signal scope. DI 1 must
        // become historical rather than being deleted when its checkbox is cleared.
        first.IsSelected = false;
        NativeFatStateStore.Reconcile(state, device);

        firstState = state.Signals.Single(signal =>
            signal.Key == NativeFatIdentity.BuildKey(first));
        var secondState = state.Signals.Single(signal =>
            signal.Key == NativeFatIdentity.BuildKey(second));
        Assert.True(firstState.IsHistorical);
        Assert.False(secondState.IsHistorical);
        Assert.Equal(NativeFatResult.Pass, firstState.Result);
        Assert.Equal("False", firstState.Value1?.Value);

        // Re-selecting the same canonical IEC identity resumes the old evidence instead of
        // starting a new UNTESTED record.
        first.IsSelected = true;
        NativeFatStateStore.Reconcile(state, device);

        firstState = state.Signals.Single(signal =>
            signal.Key == NativeFatIdentity.BuildKey(first));
        Assert.False(firstState.IsHistorical);
        Assert.Equal(NativeFatResult.Pass, firstState.Result);
        Assert.Equal("False", firstState.Value1?.Value);
        Assert.Equal(2, state.Signals.Count);
    }

    [Fact]
    public async Task SaveLoad_RestartAndIedRename_ResumeByStableDeviceId()
    {
        using var temp = new TemporaryDirectory();
        var device = CreateDevice("stable-device-01", "Relay Before Rename");
        var signal = CreateSignal("Breaker status", "IED1LD0/GGIO1.Ind2.stVal");
        device.Signals.Add(signal);

        var store = new NativeFatStateStore(temp.Path);
        var state = await store.LoadAndReconcileAsync(device);
        using (var row = Assert.Single(NativeFatStateStore.BuildRows(state, device)))
        {
            signal.Value = "False";
            signal.Quality = "Good";
            Assert.True(row.CaptureValue(1));
            row.SetResult(NativeFatResult.Review);
        }
        await store.SaveAsync(state);
        var originalPath = state.StoragePath;

        device.Name = "Relay After Rename";
        var restartedStore = new NativeFatStateStore(temp.Path);
        var resumed = await restartedStore.LoadAndReconcileAsync(device);

        var resumedSignal = Assert.Single(resumed.Signals);
        Assert.Equal("Relay After Rename", resumed.IedName);
        Assert.Equal(NativeFatResult.Review, resumedSignal.Result);
        Assert.Equal("False", resumedSignal.Value1?.Value);
        Assert.Equal(originalPath, resumed.StoragePath);
        Assert.Single(Directory.GetFiles(temp.Path, "*.json"));
    }

    [Fact]
    public async Task SameDisplayNameDifferentDeviceIds_NeverShareOneStateFile()
    {
        using var temp = new TemporaryDirectory();
        var store = new NativeFatStateStore(temp.Path);

        var first = CreateDevice("device-one", "Duplicate IED Name");
        first.Signals.Add(CreateSignal("DI 1", "IED1LD0/GGIO1.Ind1.stVal"));
        var firstState = await store.LoadAndReconcileAsync(first);
        firstState.Signals.Single().Result = NativeFatResult.Pass;
        await store.SaveAsync(firstState);

        var second = CreateDevice("device-two", "Duplicate IED Name");
        second.Signals.Add(CreateSignal("DI 1", "IED1LD0/GGIO1.Ind1.stVal"));
        var secondState = await store.LoadAndReconcileAsync(second);
        secondState.Signals.Single().Result = NativeFatResult.Fail;
        await store.SaveAsync(secondState);

        Assert.NotEqual(firstState.StoragePath, secondState.StoragePath);
        Assert.Equal(2, Directory.GetFiles(temp.Path, "*.json").Length);

        var restartedStore = new NativeFatStateStore(temp.Path);
        var firstReloaded = await restartedStore.LoadAndReconcileAsync(first);
        var secondReloaded = await restartedStore.LoadAndReconcileAsync(second);
        Assert.Equal(NativeFatResult.Pass, Assert.Single(firstReloaded.Signals).Result);
        Assert.Equal(NativeFatResult.Fail, Assert.Single(secondReloaded.Signals).Result);
    }

    [Fact]
    public async Task DamagedPreferredJson_IsNeverOverwrittenByRecoveryState()
    {
        using var temp = new TemporaryDirectory();
        var device = CreateDevice("device-corrupt", "Corrupt Evidence IED");
        device.Signals.Add(CreateSignal("DI 1", "IED1LD0/GGIO1.Ind1.stVal"));

        var firstStore = new NativeFatStateStore(temp.Path);
        var seed = await firstStore.LoadAsync(device);
        var damagedPath = seed.StoragePath;
        const string damagedContent = "{ this is intentionally not valid JSON";
        await File.WriteAllTextAsync(damagedPath, damagedContent);

        var recoveryStore = new NativeFatStateStore(temp.Path);
        var recovered = await recoveryStore.LoadAndReconcileAsync(device);
        Assert.NotEqual(damagedPath, recovered.StoragePath);
        Assert.Contains("RECOVERY", Path.GetFileName(recovered.StoragePath), StringComparison.OrdinalIgnoreCase);

        await recoveryStore.SaveAsync(recovered);
        Assert.Equal(damagedContent, await File.ReadAllTextAsync(damagedPath));
        Assert.True(File.Exists(recovered.StoragePath));
        Assert.Equal(2, Directory.GetFiles(temp.Path, "*.json").Length);
    }

    [Fact]
    public void ReportSnapshot_FreezesEvidenceAndSummaryAtBuildTime()
    {
        var device = CreateDevice("device-report", "Report IED");
        var state = new NativeFatDeviceState
        {
            DeviceId = device.DeviceId,
            IedName = device.Name,
            Signals =
            {
                new NativeFatSignalState
                {
                    Key = "IED1LD0/GGIO1.IND1.STVAL|ST",
                    SignalName = "Current DI",
                    IecReference = "IED1LD0/GGIO1.Ind1.stVal",
                    FunctionalConstraint = "ST",
                    DataType = "Boolean",
                    Result = NativeFatResult.Pass,
                    Value1 = new NativeFatCapture
                    {
                        Value = "False",
                        Quality = "Good",
                        DeviceTimestamp = "2026-09-09 08:00:00.001",
                        SourceMode = "BRCB",
                        CapturedUtc = new DateTimeOffset(2026, 9, 9, 8, 0, 1, TimeSpan.Zero)
                    },
                    Value2 = new NativeFatCapture
                    {
                        Value = "True",
                        Quality = "Good",
                        DeviceTimestamp = "2026-09-09 08:00:05.002",
                        SourceMode = "BRCB",
                        CapturedUtc = new DateTimeOffset(2026, 9, 9, 8, 0, 6, TimeSpan.Zero)
                    },
                    History = { new NativeFatHistoryEntry { Action = "Result PASS", Result = NativeFatResult.Pass } }
                },
                new NativeFatSignalState
                {
                    Key = "IED1LD0/GGIO1.IND9.STVAL|ST",
                    SignalName = "Removed DI",
                    IecReference = "IED1LD0/GGIO1.Ind9.stVal",
                    FunctionalConstraint = "ST",
                    DataType = "Boolean",
                    Result = NativeFatResult.Fail,
                    IsHistorical = true
                }
            }
        };

        var generated = new DateTimeOffset(2026, 9, 9, 8, 30, 0, TimeSpan.Zero);
        var snapshot = NativeFatReportSnapshotBuilder.Build(device, state, generated);

        Assert.Equal(generated, snapshot.GeneratedUtc);
        Assert.Equal(1, snapshot.CurrentCount);
        Assert.Equal(1, snapshot.HistoricalCount);
        Assert.Equal(1, snapshot.PassCount);
        Assert.Equal(0, snapshot.FailCount);
        Assert.Equal(0, snapshot.UntestedCount);

        var current = snapshot.Rows.Single(row => !row.IsHistorical);
        Assert.Equal("False", current.Value1Text);
        Assert.Equal("True", current.Value2Text);
        Assert.Equal("Good", current.Value1Quality);
        Assert.Equal("BRCB", current.Value2SourceMode);
        Assert.Contains("history 1 record", current.EvidenceSummaryText, StringComparison.OrdinalIgnoreCase);

        // A report snapshot is immutable evidence. Later live/persisted mutation must not
        // retroactively change what this preview/export build represented.
        state.Signals[0].Value1!.Value = "CHANGED AFTER SNAPSHOT";
        state.Signals[0].Result = NativeFatResult.Review;
        Assert.Equal("False", current.Value1Text);
        Assert.Equal(NativeFatResult.Pass, current.Result);
    }

    private static Iec61850MonitorDevice CreateDevice(string deviceId, string name)
        => new()
        {
            DeviceId = deviceId,
            Name = name,
            IpAddress = "192.0.2.10",
            Port = 102
        };

    private static SignalDefinition CreateSignal(string name, string reference)
    {
        var signal = new SignalDefinition
        {
            Name = name,
            ObjectReference = reference,
            FunctionalConstraint = "ST",
            DataType = "Boolean",
            Category = "Status",
            IsSelected = true,
            ProbeStatus = "Readable"
        };
        Assert.True(signal.CanPublishAsSignal, $"Test signal '{reference}' must be publishable by Explorer policy.");
        return signal;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ARSAS.Tests",
                "NativeFat",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Test cleanup must not hide the actual assertion result.
            }
        }
    }
}
