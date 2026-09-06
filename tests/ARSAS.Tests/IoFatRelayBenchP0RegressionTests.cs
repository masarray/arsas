using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatRelayBenchP0RegressionTests
{
    [Fact]
    public void ReportBackedAnalog_LatchesFirstGoodValueAndFirstChangedValueImmediately()
    {
        var point = NewAnalogPoint("TOTVA");
        var coordinator = new FatAutoCaptureCoordinator();

        var value1 = coordinator.Observe(point, Observation("0", 1, "BRCB"));

        Assert.NotNull(value1.Evidence);
        Assert.Equal(FatValueSlot.Value1, value1.Evidence!.Slot);
        Assert.Equal("0", value1.Evidence.RawValue);
        Assert.Equal(FatAutoCaptureStage.WaitingChange, value1.Stage);
        point.Runtime.SetFatValueEvidence(value1.Evidence);

        var value2 = coordinator.Observe(point, Observation("1000", 2, "BRCB"));

        Assert.NotNull(value2.Evidence);
        Assert.Equal(FatValueSlot.Value2, value2.Evidence!.Slot);
        Assert.Equal("1000", value2.Evidence.RawValue);
        Assert.Equal(FatAutoCaptureStage.Complete, value2.Stage);
    }

    [Fact]
    public void CompletedPair_AdvancesToLatestTwoConditionsWhenTestIsRepeated()
    {
        var point = NewAnalogPoint("ROLLING-TOTVA");
        var coordinator = new FatAutoCaptureCoordinator();

        var first = coordinator.Observe(point, Observation("0", 1, "BRCB"));
        Assert.NotNull(first.Evidence);
        point.Runtime.SetFatValueEvidence(first.Evidence!);

        var second = coordinator.Observe(point, Observation("1000", 2, "BRCB"));
        Assert.NotNull(second.Evidence);
        point.Runtime.SetFatValueEvidence(second.Evidence!);
        Assert.Equal("0", point.Value1Text);
        Assert.Equal("1000", point.Value2Text);

        var repeated = coordinator.Observe(point, Observation("0", 3, "BRCB"));
        Assert.NotNull(repeated.Evidence);
        Assert.NotNull(repeated.ShiftedValue1Evidence);
        point.Runtime.SetFatValueEvidence(repeated.ShiftedValue1Evidence!);
        point.Runtime.SetFatValueEvidence(repeated.Evidence!);

        Assert.Equal(FatAutoCaptureStage.Complete, repeated.Stage);
        Assert.Equal("1000", point.Value1Text);
        Assert.Equal("0", point.Value2Text);
    }

    [Fact]
    public void CompletedPair_DoesNotAdvanceWhenNewestConditionIsRepeatedWithoutChange()
    {
        var point = NewAnalogPoint("ROLLING-NO-EDGE");
        var coordinator = new FatAutoCaptureCoordinator();

        var first = coordinator.Observe(point, Observation("0", 1, "BRCB"));
        point.Runtime.SetFatValueEvidence(first.Evidence!);
        var second = coordinator.Observe(point, Observation("1000", 2, "BRCB"));
        point.Runtime.SetFatValueEvidence(second.Evidence!);

        var unchanged = coordinator.Observe(point, Observation("1000", 3, "BRCB"));

        Assert.Null(unchanged.Evidence);
        Assert.Equal(FatAutoCaptureStage.Complete, unchanged.Stage);
        Assert.Equal("0", point.Value1Text);
        Assert.Equal("1000", point.Value2Text);
    }

    [Fact]
    public void ReportBackedAnalog_EquivalentNoiseDoesNotConsumeValue2()
    {
        var point = NewAnalogPoint("TOTVA-NOISE");
        var coordinator = new FatAutoCaptureCoordinator();

        var baseline = coordinator.Observe(point, Observation("1000", 1, "InformationReport/BRCB"));
        Assert.NotNull(baseline.Evidence);
        point.Runtime.SetFatValueEvidence(baseline.Evidence!);

        var equivalent = coordinator.Observe(point, Observation("1000.2", 2, "InformationReport/BRCB"));
        Assert.Null(equivalent.Evidence);
        Assert.Equal(FatAutoCaptureStage.WaitingChange, equivalent.Stage);

        var changed = coordinator.Observe(point, Observation("1001", 3, "InformationReport/BRCB"));
        Assert.NotNull(changed.Evidence);
        Assert.Equal(FatValueSlot.Value2, changed.Evidence!.Slot);
        Assert.Equal("1001", changed.Evidence.RawValue);
    }

    [Fact]
    public void PolledAnalog_KeepsSettlingGuard()
    {
        var point = NewAnalogPoint("POLL-FALLBACK");
        var coordinator = new FatAutoCaptureCoordinator();

        var first = coordinator.Observe(point, Observation("12.5", 1, "MMS-POLL"));
        var second = coordinator.Observe(point, Observation("12.5", 2, "MMS-POLL"));
        var third = coordinator.Observe(point, Observation("12.5", 3, "MMS-POLL"));

        Assert.Null(first.Evidence);
        Assert.Null(second.Evidence);
        Assert.NotNull(third.Evidence);
        Assert.Equal(FatValueSlot.Value1, third.Evidence!.Slot);
    }

    [Fact]
    public void EvidenceJournal_QueuesWritesAndKeepsDurableBarrierOffAppendPath()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            Path.Combine("Services", "IoTesting", "IoTestEvidenceJournal.cs")));

        Assert.DoesNotContain("FileOptions.WriteThrough", source, StringComparison.Ordinal);
        Assert.Contains("Channel<IoTestJournalEnvelope>", source, StringComparison.Ordinal);
        Assert.Contains("Task.Run(ProcessPendingWritesAsync)", source, StringComparison.Ordinal);
        Assert.Contains("QueueEnvelope(envelope);", source, StringComparison.Ordinal);
        Assert.Contains("await _pendingWrites.Reader.WaitToReadAsync()", source, StringComparison.Ordinal);
        Assert.Contains("_writer.Flush();", source, StringComparison.Ordinal);
        Assert.Contains("_stream.Flush(flushToDisk: true);", source, StringComparison.Ordinal);
        Assert.Contains("FileOptions.SequentialScan", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AlreadyLiveEngineeringScope_UsesExactNoDiscoveryFastPath()
    {
        var point = new IoTestPointPlan
        {
            TestPointId = "FAST-POS",
            IedName = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            SignalName = "Position",
            ObjectReference = "AA1E1F06R4Q0/CSWI1.Pos.stVal",
            FunctionalConstraint = "ST",
            ExpectedOnText = "Closed",
            ExpectedOffText = "Open",
            WorkspaceSelected = true,
            TestEnabled = true,
            ImportReady = true,
            BindingStatus = IoTestSignalSelectionService.SclWorkspaceAuthorityBindingStatus
        };
        var ied = new IoTestIedPlan
        {
            IedName = point.IedName,
            IpAddress = point.IpAddress,
            TestPoints = { point }
        };
        var signal = new SignalDefinition
        {
            Name = point.SignalName,
            ObjectReference = point.ObjectReference,
            DisplayReference = point.ObjectReference,
            FunctionalConstraint = point.FunctionalConstraint,
            IsSelected = true
        };
        var device = new Iec61850MonitorDevice
        {
            DeviceId = "fast-live-device",
            Name = point.IedName,
            SclIedName = point.IedName,
            IpAddress = point.IpAddress,
            IsConnected = true,
            IsMonitoring = true
        };
        device.Signals.Add(signal);
        device.Points.Add(new Iec61850MonitorPoint
        {
            DeviceId = device.DeviceId,
            DeviceName = device.Name,
            IpAddress = device.IpAddress,
            SignalName = point.SignalName,
            IecReference = point.ObjectReference,
            FunctionalConstraint = point.FunctionalConstraint,
            Value = "Closed [10]",
            Quality = "Good",
            SourceMode = "BRCB"
        });

        var result = new IoTestSignalSelectionService().Resolve(ied, device);

        Assert.True(result.Succeeded, result.Message);
        Assert.Single(result.Matches);
        Assert.Same(signal, result.Matches[0].Signal);
        Assert.Contains("already-live Engineering acquisition session", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AlreadyLiveFastPath_PrecedesMandatoryInventoryMutation()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            Path.Combine("Services", "IoTesting", "IoTestSignalSelectionService.cs")));
        var fastPath = source.IndexOf("TryResolveAlreadyLiveExactScope", StringComparison.Ordinal);
        var mandatoryInventory = source.IndexOf("Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device);", StringComparison.Ordinal);

        Assert.True(fastPath >= 0);
        Assert.True(mandatoryInventory > fastPath);
        Assert.Contains("device.IsConnected && device.IsMonitoring", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PreparationProgress_DoesNotWalkWholeVisualTreeOnHotDispatcherTick()
    {
        var source = File.ReadAllText(FindRepositoryFile("IoListTestingWindow.RealPreparationProgress.cs"));
        var tickStart = source.IndexOf("private void PreparationProgressTimer_Tick", StringComparison.Ordinal);
        var cacheStart = source.IndexOf("private void RefreshPreparationProgressBarCache", StringComparison.Ordinal);

        Assert.True(tickStart >= 0);
        Assert.True(cacheStart > tickStart);
        var tickBody = source[tickStart..cacheStart];
        Assert.DoesNotContain("VisualDescendants<ProgressBar>", tickBody, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Background", source, StringComparison.Ordinal);
        Assert.Contains("Interval = TimeSpan.FromMilliseconds(100)", source, StringComparison.Ordinal);
        Assert.Contains("if (!hasActivePreparation)", tickBody, StringComparison.Ordinal);
        Assert.Contains("return;", tickBody, StringComparison.Ordinal);
    }

    private static IoTestPointPlan NewAnalogPoint(string id)
        => new()
        {
            TestPointId = id,
            IedName = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            SignalName = id,
            ObjectReference = $"AA1E1F06R4/PPRE_MMXU1.{id}",
            FunctionalConstraint = "MX",
            ExpectedOnText = "Value 1",
            ExpectedOffText = "Value 2",
            SignalKind = FatSignalKind.Analog,
            CaptureMode = FatCaptureMode.OperatorSnapshot,
            WorkspaceSelected = true,
            TestEnabled = true,
            ImportReady = true,
            BindingStatus = "CID_DATASET_EXACT"
        };

    private static IoTestObservation Observation(string rawValue, long sequence, string source)
    {
        var captured = new DateTimeOffset(2026, 9, 6, 9, 0, 0, TimeSpan.Zero)
            .AddMilliseconds(sequence * 10);
        return new IoTestObservation(
            null,
            rawValue,
            captured,
            captured.AddMilliseconds(-2),
            "Good",
            source,
            sequence,
            1);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}