using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatV2WorkspaceRegressionTests
{
    [Fact]
    public void OperatorSnapshot_Value1Value2Recapture_IsJournalFirstAndKeepsSessionRunning()
    {
        var fixture = ManualFixture();
        using var controller = fixture.Controller;

        var started = controller.Start(fixture.Ied);
        Assert.True(started.Succeeded, started.Message);

        var first = controller.CaptureOperatorSnapshot(fixture.Point, FatValueSlot.Value1);
        Assert.True(first.Succeeded, first.Message);
        var firstId = fixture.Point.Runtime.Value1Evidence!.EvidenceId;
        Assert.Equal("12.34", fixture.Point.Value1Text);
        Assert.False(fixture.Point.IsFatEvidenceComplete);

        fixture.LivePoint.Value = "18.90";
        fixture.LivePoint.Sequence = 2;
        var second = controller.CaptureOperatorSnapshot(fixture.Point, FatValueSlot.Value2);
        Assert.True(second.Succeeded, second.Message);
        Assert.True(fixture.Point.IsFatEvidenceComplete);
        Assert.Equal("18.90", fixture.Point.Value2Text);
        Assert.Equal("✔ COMPLETE", fixture.Point.FatResultText);
        Assert.Equal(IoTestSessionState.Running, controller.State);

        fixture.LivePoint.Value = "13.01";
        fixture.LivePoint.Sequence = 3;
        var recaptured = controller.CaptureOperatorSnapshot(fixture.Point, FatValueSlot.Value1);
        Assert.True(recaptured.Succeeded, recaptured.Message);
        Assert.NotEqual(firstId, fixture.Point.Runtime.Value1Evidence!.EvidenceId);
        Assert.Equal("13.01", fixture.Point.Value1Text);
        Assert.True(fixture.Point.IsFatEvidenceComplete);
        Assert.Equal(IoTestSessionState.Running, controller.State);

        controller.Stop();
        var journal = File.ReadAllText(controller.JournalPath);
        Assert.Equal(3, CountOccurrences(journal, "\"eventType\":\"fat_value_snapshot\""));
        Assert.Contains("\"transition\":\"Value1\"", journal, StringComparison.Ordinal);
        Assert.Contains("\"transition\":\"Value2\"", journal, StringComparison.Ordinal);
        Assert.Contains("\"evidenceKind\":\"fat-value-snapshot\"", journal, StringComparison.Ordinal);
        Assert.True(IoTestEvidenceJournal.Verify(controller.JournalPath).IsValid);
    }

    [Fact]
    public void OperatorSnapshot_JournalFailure_DoesNotPromoteCurrentEvidencePointer()
    {
        var fixture = ManualFixture(journalFactory: (_, _, _, _) => new FailAfterStartupJournal());
        using var controller = fixture.Controller;
        Assert.True(controller.Start(fixture.Ied).Succeeded);

        var result = controller.CaptureOperatorSnapshot(fixture.Point, FatValueSlot.Value1);

        Assert.False(result.Succeeded);
        Assert.Null(fixture.Point.Runtime.Value1Evidence);
        Assert.Equal(IoTestSessionState.Faulted, controller.State);
    }

    [Fact]
    public void RemoveRestore_PreservesCheckboxAndCurrentEvidence()
    {
        var fixture = ManualFixture();
        using var controller = fixture.Controller;
        Assert.True(controller.Start(fixture.Ied).Succeeded);
        Assert.True(controller.CaptureOperatorSnapshot(fixture.Point, FatValueSlot.Value1).Succeeded);
        controller.Stop();
        var evidenceId = fixture.Point.Runtime.Value1Evidence!.EvidenceId;

        fixture.Point.RemoveFromFat();

        Assert.False(fixture.Point.IsIncludedInFat);
        Assert.True(fixture.Point.TestEnabled);
        Assert.Equal(evidenceId, fixture.Point.Runtime.Value1Evidence!.EvidenceId);
        Assert.Equal(0, fixture.Ied.EnabledCount);
        Assert.Equal(1, fixture.Ied.RemovedCount);

        fixture.Point.RestoreToFat();

        Assert.True(fixture.Point.IsIncludedInFat);
        Assert.True(fixture.Point.TestEnabled);
        Assert.Equal(evidenceId, fixture.Point.Runtime.Value1Evidence!.EvidenceId);
        Assert.Equal(1, fixture.Ied.EnabledCount);
        Assert.Equal(0, fixture.Ied.RemovedCount);
    }

    [Fact]
    public async Task SourceWorkspaceAndPackage_RoundTripDispositionAndValueEvidence()
    {
        var root = TempDirectory();
        var source = Path.Combine(root, "relay.cid");
        await File.WriteAllTextAsync(source, "<SCL/>" );
        var projectsA = Path.Combine(root, "projects-a");
        var evidenceA = Path.Combine(root, "evidence-a");
        var project = ManualProject();
        var point = project.Ieds[0].TestPoints[0];
        point.Runtime.SetFatValueEvidence(Evidence(FatValueSlot.Value1, "12.34", 1));
        point.Runtime.SetFatValueEvidence(Evidence(FatValueSlot.Value2, "18.90", 2));
        point.RemoveFromFat();

        var opened = await IoTestWorkspaceBootstrapService.OpenSourcesAsync(
            project,
            new[] { new IoFatSourceInput(source, IoFatSourceKinds.Scl) },
            projectsA,
            evidenceA,
            Session);
        var package = Path.Combine(root, "fat-v2.arsas");
        using (opened.Session)
        using (opened.Workspace)
        {
            opened.Workspace.SaveNow();
            await IoFatProjectPackageService.ExportAsync(opened.Workspace, opened.Session, package);
        }

        var reopened = await IoTestWorkspaceBootstrapService.OpenSourcesAsync(
            ManualProject(),
            new[] { new IoFatSourceInput(source, IoFatSourceKinds.Scl) },
            projectsA,
            evidenceA,
            Session);
        using (reopened.Session)
        using (reopened.Workspace)
        {
            var restored = reopened.Project.Ieds[0].TestPoints[0];
            Assert.True(reopened.RestoredProgress);
            Assert.False(restored.IsIncludedInFat);
            Assert.True(restored.TestEnabled);
            Assert.Equal(FatSignalKind.Analog, restored.SignalKind);
            Assert.Equal(FatCaptureMode.OperatorSnapshot, restored.CaptureMode);
            Assert.Equal("12.34", restored.Runtime.Value1Evidence!.RawValue);
            Assert.Equal("18.90", restored.Runtime.Value2Evidence!.RawValue);
            Assert.True(restored.IsFatEvidenceComplete);
        }

        var imported = await IoTestWorkspaceBootstrapService.OpenPackageAsync(
            package,
            Path.Combine(root, "projects-b"),
            Path.Combine(root, "evidence-b"),
            Session);
        using (imported.Session)
        using (imported.Workspace)
        {
            var restored = imported.Project.Ieds[0].TestPoints[0];
            Assert.False(restored.IsIncludedInFat);
            Assert.True(restored.TestEnabled);
            Assert.Equal(FatSignalKind.Analog, restored.SignalKind);
            Assert.Equal(FatCaptureMode.OperatorSnapshot, restored.CaptureMode);
            Assert.Equal("12.34", restored.Runtime.Value1Evidence!.RawValue);
            Assert.Equal("18.90", restored.Runtime.Value2Evidence!.RawValue);
            Assert.True(restored.IsFatEvidenceComplete);
        }
    }

    private static Fixture ManualFixture(
        Func<IoTestProject, IoTestIedPlan, Guid, DateTimeOffset, IIoTestEvidenceJournal>? journalFactory = null)
    {
        var project = ManualProject();
        var ied = project.Ieds[0];
        var point = ied.TestPoints[0];
        var device = new Iec61850MonitorDevice
        {
            DeviceId = "fat-v2-device",
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
            Value = "12.34",
            Quality = "Good",
            DeviceTimestamp = "2026-09-01T03:00:00.000Z",
            SourceMode = "BRCB",
            Sequence = 1,
            Status = "Live"
        };
        device.Points.Add(livePoint);
        new IoTestLiveBindingService().Bind(project, new[] { device });
        var root = TempDirectory();
        var controller = new IoTestSessionController(
            project,
            key => key.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                   key.Equals(device.Name, StringComparison.OrdinalIgnoreCase) ||
                   key.Equals(device.IpAddress, StringComparison.OrdinalIgnoreCase)
                ? device
                : null,
            action => action(),
            root,
            journalFactory: journalFactory);
        return new Fixture(project, ied, point, device, livePoint, root, controller);
    }

    private static IoTestProject ManualProject()
    {
        var point = new IoTestPointPlan
        {
            TestPointId = "P5-ANALOG-001",
            IedName = "IED-P5",
            IpAddress = "192.0.2.55",
            SignalName = "Phase current A",
            ObjectReference = "IED-P5MEAS/MMXU1.A.phsA.cVal.mag.f",
            FunctionalConstraint = "MX",
            ExpectedOnText = "Value 1",
            ExpectedOffText = "Value 2",
            DataType = "FLOAT32",
            SignalKind = FatSignalKind.Analog,
            CaptureMode = FatCaptureMode.OperatorSnapshot,
            ImportReady = true,
            BindingStatus = "SCL_DATASET_AUTHORITY"
        };
        return new IoTestProject
        {
            ProjectId = "P5-V2-PERSISTENCE",
            SchemaVersion = "ARSAS-FAT-SCL-1.0",
            ProjectName = "P5 FAT v2",
            Ieds =
            {
                new IoTestIedPlan
                {
                    IedName = point.IedName,
                    IpAddress = point.IpAddress,
                    TestPoints = { point }
                }
            }
        };
    }

    private static FatValueEvidence Evidence(FatValueSlot slot, string raw, long sequence)
        => new(
            Guid.NewGuid(),
            slot,
            FatEvidenceCaptureKind.OperatorSnapshot,
            raw,
            new DateTimeOffset(2026, 9, 1, 3, 0, 0, TimeSpan.Zero).AddSeconds(sequence),
            new DateTimeOffset(2026, 9, 1, 3, 0, 0, TimeSpan.Zero).AddSeconds(sequence).AddMilliseconds(-2),
            "Good",
            "BRCB",
            sequence,
            1);

    private static IoTestSessionController Session(IoTestProject project, string evidenceRoot)
        => new(project, _ => null, action => action(), evidenceRoot);

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ARSAS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed record Fixture(
        IoTestProject Project,
        IoTestIedPlan Ied,
        IoTestPointPlan Point,
        Iec61850MonitorDevice Device,
        Iec61850MonitorPoint LivePoint,
        string Root,
        IoTestSessionController Controller);

    private sealed class FailAfterStartupJournal : IIoTestEvidenceJournal
    {
        private long _sequence;
        public string FilePath { get; } = Path.Combine(Path.GetTempPath(), $"p5-fail-{Guid.NewGuid():N}.jsonl");
        public long RecordCount => _sequence;
        public string LastHash { get; private set; } = new('a', 64);

        public IoTestJournalEnvelope Append(IoTestJournalEntry entry)
            => throw new IOException("synthetic durable journal failure");

        public IReadOnlyList<IoTestJournalEnvelope> AppendBatch(IEnumerable<IoTestJournalEntry> entries)
        {
            var result = new List<IoTestJournalEnvelope>();
            foreach (var entry in entries)
            {
                _sequence++;
                LastHash = _sequence.ToString("x64");
                result.Add(new IoTestJournalEnvelope(_sequence, new('0', 64), LastHash, entry));
            }
            return result;
        }

        public void Dispose() { }
    }
}