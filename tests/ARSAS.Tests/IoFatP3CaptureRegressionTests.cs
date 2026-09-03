using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatP3CaptureRegressionTests
{
    [Fact]
    public void AnalogAutoCapture_LatchesStableBaselineAndFinalSettledValue_NotTransients()
    {
        var point = NewPoint("AN-1", FatSignalKind.Analog, FatCaptureMode.OperatorSnapshot);
        var coordinator = new FatAutoCaptureCoordinator();
        long sequence = 0;

        FatAutoCaptureDecision decision = default!;
        foreach (var raw in new[] { "0", "0", "0" })
        {
            decision = coordinator.Observe(point, Observation(raw, ++sequence));
            if (decision.Evidence != null)
                point.Runtime.SetFatValueEvidence(decision.Evidence);
        }

        Assert.NotNull(point.Runtime.Value1Evidence);
        Assert.Equal("0", point.Runtime.Value1Evidence!.RawValue);
        Assert.Equal(FatEvidenceCaptureKind.AutomaticValue, point.Runtime.Value1Evidence.CaptureKind);

        foreach (var raw in new[] { "18.412", "43.920", "61.850", "65.702", "65.746", "65.748", "65.748", "65.748" })
        {
            decision = coordinator.Observe(point, Observation(raw, ++sequence));
            if (decision.Evidence != null)
                point.Runtime.SetFatValueEvidence(decision.Evidence);
        }

        Assert.NotNull(point.Runtime.Value2Evidence);
        Assert.Equal("65.748", point.Runtime.Value2Evidence!.RawValue);
        Assert.Equal(FatEvidenceCaptureKind.AutomaticValue, point.Runtime.Value2Evidence.CaptureKind);
        Assert.Equal(FatAutoCaptureStage.Complete, decision.Stage);
    }

    [Fact]
    public void AutoCapture_BadQualityDoesNotCreateEvidence_AndCompletePairDoesNotAutoOverwrite()
    {
        var point = NewPoint("AN-2", FatSignalKind.Analog, FatCaptureMode.OperatorSnapshot);
        var coordinator = new FatAutoCaptureCoordinator();

        var rejected = coordinator.Observe(point, Observation("12.3", 1, quality: "Invalid"));
        Assert.Null(rejected.Evidence);
        Assert.False(point.HasValue1Evidence);

        var value1 = Evidence(FatValueSlot.Value1, "10", 2, FatEvidenceCaptureKind.OperatorRecapture);
        var value2 = Evidence(FatValueSlot.Value2, "20", 3, FatEvidenceCaptureKind.OperatorRecapture);
        point.Runtime.SetFatValueEvidence(value1);
        point.Runtime.SetFatValueEvidence(value2);

        var locked = coordinator.Observe(point, Observation("999", 4));

        Assert.Null(locked.Evidence);
        Assert.Equal(FatAutoCaptureStage.Complete, locked.Stage);
        Assert.Equal(value1.EvidenceId, point.Runtime.Value1Evidence!.EvidenceId);
        Assert.Equal(value2.EvidenceId, point.Runtime.Value2Evidence!.EvidenceId);
    }

    [Fact]
    public void GenericValueEvidence_OverridesLegacyAutomaticOnOffProjection()
    {
        var point = NewPoint("DI-1", FatSignalKind.Discrete, FatCaptureMode.AutomaticTransition);
        var evaluator = new IoTestTransitionEvaluator();
        evaluator.StartAttempt(point, DigitalObservation(false, 1));
        evaluator.Observe(point, DigitalObservation(true, 2));
        evaluator.Observe(point, DigitalObservation(false, 3));

        Assert.Equal("True", point.Value1Text);
        Assert.Equal("False", point.Value2Text);

        var manualValue1 = Evidence(FatValueSlot.Value1, "MANUAL-V1", 4, FatEvidenceCaptureKind.OperatorRecapture);
        var manualValue2 = Evidence(FatValueSlot.Value2, "MANUAL-V2", 5, FatEvidenceCaptureKind.OperatorRecapture);
        point.Runtime.SetFatValueEvidence(manualValue1);
        point.Runtime.SetFatValueEvidence(manualValue2);

        Assert.Equal("MANUAL-V1", point.Value1Text);
        Assert.Equal("MANUAL-V2", point.Value2Text);
        Assert.True(point.IsFatEvidenceComplete);
    }

    [Fact]
    public void RecaptureSingleSlot_PreservesOtherSlot_AndUsesOperatorRecaptureProvenance()
    {
        var fixture = CreateSnapshotFixture();
        using var controller = fixture.Controller;
        Assert.True(controller.Start(fixture.Ied).Succeeded);

        fixture.LivePoint.Value = "10";
        fixture.LivePoint.Sequence = 1;
        Assert.True(controller.CaptureOperatorSnapshot(fixture.Point, FatValueSlot.Value1).Succeeded);
        fixture.LivePoint.Value = "20";
        fixture.LivePoint.Sequence = 2;
        Assert.True(controller.CaptureOperatorSnapshot(fixture.Point, FatValueSlot.Value2).Succeeded);
        var oldValue2 = fixture.Point.Runtime.Value2Evidence!.EvidenceId;

        fixture.LivePoint.Value = "15";
        fixture.LivePoint.Sequence = 3;
        var recaptured = controller.RecaptureValues(new[] { fixture.Point }, FatValueSlot.Value1);

        Assert.True(recaptured.Succeeded, recaptured.Message);
        Assert.Equal("15", fixture.Point.Value1Text);
        Assert.Equal("20", fixture.Point.Value2Text);
        Assert.Equal(oldValue2, fixture.Point.Runtime.Value2Evidence!.EvidenceId);
        Assert.Equal(FatEvidenceCaptureKind.OperatorRecapture, fixture.Point.Runtime.Value1Evidence!.CaptureKind);
    }

    [Fact]
    public void PairRecapture_StagesValue1WithoutChangingCurrentPair_ThenPromotesBothOnValue2()
    {
        var fixture = CreateSnapshotFixture();
        using var controller = fixture.Controller;
        Assert.True(controller.Start(fixture.Ied).Succeeded);
        CaptureInitialPair(controller, fixture, "10", "20");
        var oldV1 = fixture.Point.Runtime.Value1Evidence!.EvidenceId;
        var oldV2 = fixture.Point.Runtime.Value2Evidence!.EvidenceId;

        fixture.LivePoint.Value = "30";
        fixture.LivePoint.Sequence = 3;
        var staged = controller.BeginPairRecapture(new[] { fixture.Point });

        Assert.True(staged.Succeeded, staged.Message);
        Assert.Equal(oldV1, fixture.Point.Runtime.Value1Evidence!.EvidenceId);
        Assert.Equal(oldV2, fixture.Point.Runtime.Value2Evidence!.EvidenceId);
        Assert.Equal("10", fixture.Point.Value1Text);
        Assert.Equal("20", fixture.Point.Value2Text);

        fixture.LivePoint.Value = "40";
        fixture.LivePoint.Sequence = 4;
        var committed = controller.RecaptureValues(new[] { fixture.Point }, FatValueSlot.Value2);

        Assert.True(committed.Succeeded, committed.Message);
        Assert.Equal("30", fixture.Point.Value1Text);
        Assert.Equal("40", fixture.Point.Value2Text);
        Assert.Equal(FatEvidenceCaptureKind.OperatorRecapture, fixture.Point.Runtime.Value1Evidence!.CaptureKind);
        Assert.Equal(FatEvidenceCaptureKind.OperatorRecapture, fixture.Point.Runtime.Value2Evidence!.CaptureKind);
        var journal = File.ReadAllText(controller.JournalPath);
        Assert.Contains("fat_value_recapture_pair_staged", journal, StringComparison.Ordinal);
        Assert.Contains("fat_value_recapture_pair_commit", journal, StringComparison.Ordinal);
    }

    [Fact]
    public void PairRecapture_CancelLeavesPreviousCurrentPairUntouched()
    {
        var fixture = CreateSnapshotFixture();
        using var controller = fixture.Controller;
        Assert.True(controller.Start(fixture.Ied).Succeeded);
        CaptureInitialPair(controller, fixture, "10", "20");
        var oldV1 = fixture.Point.Runtime.Value1Evidence!.EvidenceId;
        var oldV2 = fixture.Point.Runtime.Value2Evidence!.EvidenceId;

        fixture.LivePoint.Value = "50";
        fixture.LivePoint.Sequence = 3;
        Assert.True(controller.BeginPairRecapture(new[] { fixture.Point }).Succeeded);
        var cancelled = controller.CancelPairRecapture(new[] { fixture.Point });

        Assert.True(cancelled.Succeeded, cancelled.Message);
        Assert.Equal(oldV1, fixture.Point.Runtime.Value1Evidence!.EvidenceId);
        Assert.Equal(oldV2, fixture.Point.Runtime.Value2Evidence!.EvidenceId);
        Assert.Equal("10", fixture.Point.Value1Text);
        Assert.Equal("20", fixture.Point.Value2Text);
    }

    [Fact]
    public void BatchJournalFailure_DoesNotPromoteAnyRecapturePointer()
    {
        var fixture = CreateSnapshotFixture(useFailingJournal: true);
        using var controller = fixture.Controller;
        Assert.True(controller.Start(fixture.Ied).Succeeded);
        CaptureInitialPair(controller, fixture, "10", "20");
        var oldV1 = fixture.Point.Runtime.Value1Evidence!.EvidenceId;
        var oldV2 = fixture.Point.Runtime.Value2Evidence!.EvidenceId;
        fixture.FailingJournal!.FailNextBatch = true;

        fixture.LivePoint.Value = "99";
        fixture.LivePoint.Sequence = 4;
        var result = controller.RecaptureValues(new[] { fixture.Point }, FatValueSlot.Value1);

        Assert.False(result.Succeeded);
        Assert.Equal(oldV1, fixture.Point.Runtime.Value1Evidence!.EvidenceId);
        Assert.Equal(oldV2, fixture.Point.Runtime.Value2Evidence!.EvidenceId);
        Assert.Equal(IoTestSessionState.Faulted, controller.State);
    }

    [Fact]
    public void Recapture_PreflightRejectsRemovedRowWithoutChangingEvidence()
    {
        var fixture = CreateSnapshotFixture();
        using var controller = fixture.Controller;
        Assert.True(controller.Start(fixture.Ied).Succeeded);
        CaptureInitialPair(controller, fixture, "10", "20");
        var oldV1 = fixture.Point.Runtime.Value1Evidence!.EvidenceId;
        fixture.Point.RemoveFromFat();
        fixture.LivePoint.Value = "100";

        var result = controller.RecaptureValues(new[] { fixture.Point }, FatValueSlot.Value1);

        Assert.False(result.Succeeded);
        Assert.Equal(oldV1, fixture.Point.Runtime.Value1Evidence!.EvidenceId);
    }

    [Fact]
    public void P3GridSource_UsesExtendedSelection_PreservesSelectedRightClick_AndOffersRecaptureMenu()
    {
        var source = File.ReadAllText(FindRepositoryFile("IoListTestingWindow.FatV2Ux.cs"));

        Assert.Contains("SelectionMode = DataGridSelectionMode.Extended", source, StringComparison.Ordinal);
        Assert.Contains("if (row.IsSelected)", source, StringComparison.Ordinal);
        Assert.Contains("SelectedItems", source, StringComparison.Ordinal);
        Assert.Contains("Header = \"Recapture\"", source, StringComparison.Ordinal);
        Assert.Contains("Header = \"Value 1\"", source, StringComparison.Ordinal);
        Assert.Contains("Header = \"Value 2\"", source, StringComparison.Ordinal);
        Assert.Contains("Header = \"Value 1 & Value 2\"", source, StringComparison.Ordinal);
        Assert.Contains("BeginPairRecapture", source, StringComparison.Ordinal);
    }

    private static void CaptureInitialPair(
        IoTestSessionController controller,
        SnapshotFixture fixture,
        string value1,
        string value2)
    {
        fixture.LivePoint.Value = value1;
        fixture.LivePoint.Sequence = 1;
        Assert.True(controller.CaptureOperatorSnapshot(fixture.Point, FatValueSlot.Value1).Succeeded);
        fixture.LivePoint.Value = value2;
        fixture.LivePoint.Sequence = 2;
        Assert.True(controller.CaptureOperatorSnapshot(fixture.Point, FatValueSlot.Value2).Succeeded);
    }

    private static SnapshotFixture CreateSnapshotFixture(bool useFailingJournal = false)
    {
        var point = NewPoint("AN-SNAPSHOT", FatSignalKind.Analog, FatCaptureMode.OperatorSnapshot);
        point.ObjectReference = "IED1LD0/MMXU1.A.phsA.cVal.mag.f";
        var ied = new IoTestIedPlan
        {
            IedName = point.IedName,
            IpAddress = point.IpAddress,
            TestPoints = { point }
        };
        var project = new IoTestProject
        {
            ProjectId = "P3-FAT",
            SchemaVersion = "ARSAS-FAT-SCL-1.0",
            ProjectName = "P3 FAT",
            Ieds = { ied }
        };
        project.InitializeRuntimeNotifications();

        var device = new Iec61850MonitorDevice
        {
            DeviceId = "p3-device",
            Name = ied.IedName,
            SclIedName = ied.IedName,
            IpAddress = ied.IpAddress,
            Port = 102,
            IsConnected = true,
            IsMonitoring = true,
            Status = "Monitoring"
        };
        var livePoint = new Iec61850MonitorPoint
        {
            DeviceId = device.DeviceId,
            DeviceName = device.Name,
            IpAddress = device.IpAddress,
            SignalName = point.SignalName,
            IecReference = point.ObjectReference,
            FunctionalConstraint = "MX",
            Value = "0",
            Quality = "Good",
            DeviceTimestamp = "2026-09-03T06:00:00.000Z",
            SourceMode = "MMS-POLL",
            Sequence = 0,
            Status = "Live"
        };
        device.Points.Add(livePoint);
        new IoTestLiveBindingService().Bind(project, new[] { device });

        var root = Path.Combine(Path.GetTempPath(), "ARSAS.Tests", "P3", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        FailingBatchJournal? failing = null;
        IoTestSessionController controller;
        if (useFailingJournal)
        {
            controller = new IoTestSessionController(
                project,
                key => Resolves(key, device) ? device : null,
                action => action(),
                root,
                journalFactory: (p, plan, sessionId, startedAt) =>
                {
                    failing = new FailingBatchJournal(IoTestEvidenceJournal.Create(root, p, plan, sessionId, startedAt));
                    return failing;
                });
        }
        else
        {
            controller = new IoTestSessionController(
                project,
                key => Resolves(key, device) ? device : null,
                action => action(),
                root);
        }

        return new SnapshotFixture(project, ied, point, device, livePoint, root, controller, () => failing);
    }

    private static bool Resolves(string key, Iec61850MonitorDevice device)
        => key.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase) ||
           key.Equals(device.Name, StringComparison.OrdinalIgnoreCase) ||
           key.Equals(device.IpAddress, StringComparison.OrdinalIgnoreCase);

    private static IoTestPointPlan NewPoint(string id, FatSignalKind kind, FatCaptureMode mode)
        => new()
        {
            TestPointId = id,
            IedName = "IED1",
            IpAddress = "192.0.2.10",
            SignalName = id,
            ObjectReference = $"IED1LD0/GGIO1.{id}.stVal",
            FunctionalConstraint = kind == FatSignalKind.Analog ? "MX" : "ST",
            ExpectedOnText = "Value 1",
            ExpectedOffText = "Value 2",
            SignalKind = kind,
            CaptureMode = mode,
            WorkspaceSelected = true,
            TestEnabled = true,
            ImportReady = true,
            BindingStatus = IoTestSignalSelectionService.SclWorkspaceAuthorityBindingStatus
        };

    private static IoTestObservation Observation(string raw, long sequence, string quality = "Good")
    {
        var timestamp = new DateTimeOffset(2026, 9, 3, 6, 0, 0, TimeSpan.Zero).AddMilliseconds(sequence * 100);
        return new IoTestObservation(
            null,
            raw,
            timestamp,
            timestamp.AddMilliseconds(-2),
            quality,
            "MMS-POLL",
            sequence,
            1);
    }

    private static IoTestObservation DigitalObservation(bool state, long sequence)
    {
        var timestamp = new DateTimeOffset(2026, 9, 3, 6, 0, 0, TimeSpan.Zero).AddMilliseconds(sequence * 100);
        return new IoTestObservation(
            state,
            state ? "True" : "False",
            timestamp,
            timestamp.AddMilliseconds(-2),
            "Good",
            "BRCB",
            sequence,
            1);
    }

    private static FatValueEvidence Evidence(
        FatValueSlot slot,
        string raw,
        long sequence,
        FatEvidenceCaptureKind kind)
        => new(
            Guid.NewGuid(),
            slot,
            kind,
            raw,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMilliseconds(-2),
            "Good",
            "MMS-POLL",
            sequence,
            1);

    private static string FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }

    private sealed record SnapshotFixture(
        IoTestProject Project,
        IoTestIedPlan Ied,
        IoTestPointPlan Point,
        Iec61850MonitorDevice Device,
        Iec61850MonitorPoint LivePoint,
        string Root,
        IoTestSessionController Controller,
        Func<FailingBatchJournal?> FailingJournalAccessor)
    {
        public FailingBatchJournal? FailingJournal => FailingJournalAccessor();
    }

    private sealed class FailingBatchJournal : IIoTestEvidenceJournal
    {
        private readonly IIoTestEvidenceJournal _inner;

        public FailingBatchJournal(IIoTestEvidenceJournal inner) => _inner = inner;

        public bool FailNextBatch { get; set; }
        public string FilePath => _inner.FilePath;
        public long RecordCount => _inner.RecordCount;
        public string LastHash => _inner.LastHash;

        public IoTestJournalEnvelope Append(IoTestJournalEntry entry) => _inner.Append(entry);

        public IReadOnlyList<IoTestJournalEnvelope> AppendBatch(IEnumerable<IoTestJournalEntry> entries)
        {
            if (FailNextBatch)
            {
                FailNextBatch = false;
                throw new IOException("Synthetic P3 batch journal failure.");
            }
            return _inner.AppendBatch(entries);
        }

        public void Dispose() => _inner.Dispose();
    }
}
