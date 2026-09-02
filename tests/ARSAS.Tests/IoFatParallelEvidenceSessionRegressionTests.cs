using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatParallelEvidenceSessionRegressionTests
{
    [Fact]
    public void TwoIeds_CanCaptureEvidenceConcurrently_WithStrictDeviceIsolation()
    {
        var fixture = CreateFixture();
        using var primary = NewController(fixture.Project, fixture.Devices, fixture.Root);
        using var sessions = new IoTestMultiSessionCoordinator(fixture.Project, primary);
        sessions.ConfigureSiblingFactory(() => NewController(fixture.Project, fixture.Devices, fixture.Root));

        sessions.SelectContext(fixture.IedA);
        var startA = sessions.Start(fixture.IedA, new[] { fixture.PointA });
        sessions.SelectContext(fixture.IedB);
        var startB = sessions.Start(fixture.IedB, new[] { fixture.PointB });

        Assert.True(startA.Succeeded, startA.Message);
        Assert.True(startB.Succeeded, startB.Message);
        Assert.Equal(2, sessions.ActiveSessionCount);
        Assert.True(sessions.IsIedSessionActive(fixture.IedA));
        Assert.True(sessions.IsIedSessionActive(fixture.IedB));
        Assert.Same(fixture.IedB, sessions.ActiveIed);

        // Reproduce the production split route: MainWindow sends every event to the
        // primary leaf, while the P2 coordinator sends it only to additional leaves.
        RouteEvent(primary, sessions, Event(fixture.DeviceA, fixture.PointA, false, true, 1));
        RouteEvent(primary, sessions, Event(fixture.DeviceB, fixture.PointB, false, true, 1));
        RouteEvent(primary, sessions, Event(fixture.DeviceA, fixture.PointA, true, false, 2));
        RouteEvent(primary, sessions, Event(fixture.DeviceB, fixture.PointB, true, false, 2));

        Assert.Equal(IoTestPointState.Passed, fixture.PointA.Runtime.State);
        Assert.Equal(IoTestPointState.Passed, fixture.PointB.Runtime.State);
        Assert.Equal(1, fixture.PointA.Runtime.OnEvidence!.Sequence);
        Assert.Equal(2, fixture.PointA.Runtime.OffEvidence!.Sequence);
        Assert.Equal(1, fixture.PointB.Runtime.OnEvidence!.Sequence);
        Assert.Equal(2, fixture.PointB.Runtime.OffEvidence!.Sequence);

        // Stopping B must not seal or interrupt A's independent session/journal.
        var stopB = sessions.Stop("IED B complete");
        Assert.True(stopB.Succeeded, stopB.Message);
        Assert.Equal(1, sessions.ActiveSessionCount);
        Assert.True(sessions.IsIedSessionActive(fixture.IedA));
        Assert.False(sessions.IsIedSessionActive(fixture.IedB));

        sessions.SelectContext(fixture.IedA);
        Assert.Same(fixture.IedA, sessions.ActiveIed);
        Assert.True(sessions.CanStop);
        var stopA = sessions.Stop("IED A complete");
        Assert.True(stopA.Succeeded, stopA.Message);
        Assert.False(sessions.IsSessionActive);
        Assert.Equal(0, sessions.ActiveSessionCount);

        var evidenceFiles = Directory.GetFiles(fixture.Root, "*.evidence.jsonl", SearchOption.AllDirectories);
        Assert.Equal(2, evidenceFiles.Length);
        Assert.All(evidenceFiles, path =>
        {
            var verification = IoTestEvidenceJournal.Verify(path);
            Assert.True(verification.IsValid, verification.Error);
        });
    }

    [Fact]
    public void SelectedContext_EditAndControlState_IsIndependentFromSiblingSession()
    {
        var fixture = CreateFixture();
        using var primary = NewController(fixture.Project, fixture.Devices, fixture.Root);
        using var sessions = new IoTestMultiSessionCoordinator(fixture.Project, primary);
        sessions.ConfigureSiblingFactory(() => NewController(fixture.Project, fixture.Devices, fixture.Root));

        sessions.SelectContext(fixture.IedA);
        Assert.True(sessions.Start(fixture.IedA, new[] { fixture.PointA }).Succeeded);
        Assert.True(sessions.IsSessionActive);
        Assert.True(sessions.IsSelectedSessionActive);
        Assert.False(sessions.CanEditPlan);
        Assert.False(sessions.CanStart);

        sessions.SelectContext(fixture.IedB);

        Assert.True(sessions.IsSessionActive); // A is still running globally.
        Assert.False(sessions.IsSelectedSessionActive);
        Assert.True(sessions.CanEditPlan);
        Assert.True(sessions.CanStart);
        Assert.Null(sessions.ActiveIed);

        Assert.True(sessions.Start(fixture.IedB, new[] { fixture.PointB }).Succeeded);
        Assert.True(sessions.IsSelectedSessionActive);
        Assert.False(sessions.CanEditPlan);
        Assert.Equal(2, sessions.ActiveSessionCount);

        Assert.True(sessions.StopAll("test cleanup").Succeeded);
        Assert.False(sessions.IsSessionActive);
    }

    private static void RouteEvent(
        IoTestSessionController primary,
        IoTestMultiSessionCoordinator sessions,
        Iec61850EventEntry entry)
    {
        primary.Enqueue(entry);
        sessions.EnqueueAdditional(entry);
    }

    private static Fixture CreateFixture()
    {
        var pointA = Point("A-1", "IED_A", "192.168.81.10", "IED_ALD0/GGIO1.Ind1.stVal");
        var pointB = Point("B-1", "IED_B", "192.168.81.11", "IED_BLD0/GGIO1.Ind2.stVal");
        var iedA = Ied("IED_A", "192.168.81.10", pointA);
        var iedB = Ied("IED_B", "192.168.81.11", pointB);
        var project = new IoTestProject
        {
            ProjectId = "P2-PARALLEL",
            SchemaVersion = "ARSAS-FAT-IO-1.0",
            ProjectName = "P2 Parallel Evidence",
            SourceWorkbookName = "P2.xlsx",
            SourceWorkbookSha256 = new string('a', 64),
            Ieds = { iedA, iedB }
        };
        project.InitializeRuntimeNotifications();

        var deviceA = Device("device-a", iedA, pointA);
        var deviceB = Device("device-b", iedB, pointB);
        var devices = new[] { deviceA, deviceB };
        new IoTestLiveBindingService().Bind(project, devices);

        var root = Path.Combine(Path.GetTempPath(), "ARSAS.Tests", "P2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new Fixture(project, iedA, iedB, pointA, pointB, deviceA, deviceB, devices, root);
    }

    private static IoTestPointPlan Point(
        string id,
        string iedName,
        string ipAddress,
        string reference) => new()
    {
        TestPointId = id,
        IedName = iedName,
        IpAddress = ipAddress,
        SignalName = id,
        ObjectReference = reference,
        FunctionalConstraint = "ST",
        ExpectedOnText = "ON",
        ExpectedOffText = "OFF",
        LogicalDevice = iedName + "LD0",
        LogicalNode = "GGIO1",
        DataObject = "Ind",
        DataAttribute = "stVal",
        SourceIecReference = reference,
        EventLogSearchReference = reference,
        ReportDisplayReference = reference + " [ST]",
        BindingStatus = "CID_DATASET_EXACT",
        TestEnabled = true,
        ImportReady = true
    };

    private static IoTestIedPlan Ied(string name, string ipAddress, IoTestPointPlan point) => new()
    {
        IedName = name,
        IpAddress = ipAddress,
        IedRole = "Protection IED",
        TestPoints = { point }
    };

    private static Iec61850MonitorDevice Device(
        string deviceId,
        IoTestIedPlan ied,
        IoTestPointPlan point)
    {
        var device = new Iec61850MonitorDevice
        {
            DeviceId = deviceId,
            Name = ied.IedName,
            SclIedName = ied.IedName,
            IpAddress = ied.IpAddress,
            Port = 102,
            IsConnected = true,
            IsMonitoring = true,
            Status = "Monitoring"
        };
        device.Points.Add(new Iec61850MonitorPoint
        {
            DeviceId = deviceId,
            DeviceName = ied.IedName,
            IpAddress = ied.IpAddress,
            SignalName = point.SignalName,
            IecReference = point.ObjectReference,
            FunctionalConstraint = "ST",
            Value = "False",
            Quality = "Good",
            DeviceTimestamp = "2026-09-02T09:00:00.000Z",
            SourceMode = "BRCB",
            Sequence = 0,
            Status = "Live"
        });
        return device;
    }

    private static IoTestSessionController NewController(
        IoTestProject project,
        IReadOnlyCollection<Iec61850MonitorDevice> devices,
        string root) => new(
            project,
            key => devices.FirstOrDefault(device =>
                key.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                key.Equals(device.Name, StringComparison.OrdinalIgnoreCase) ||
                key.Equals(device.IpAddress, StringComparison.OrdinalIgnoreCase)),
            action => action(),
            root);

    private static Iec61850EventEntry Event(
        Iec61850MonitorDevice device,
        IoTestPointPlan point,
        bool oldValue,
        bool newValue,
        long sequence) => new()
    {
        Sequence = sequence,
        DeviceId = device.DeviceId,
        PointKey = device.Points[0].PointKey,
        DeviceTimestamp = new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero)
            .AddMilliseconds(sequence * 100)
            .ToString("O"),
        DeviceName = device.Name,
        IpAddress = device.IpAddress,
        SignalName = point.SignalName,
        IecReference = point.ObjectReference,
        OldValue = oldValue ? "True" : "False",
        NewValue = newValue ? "True" : "False",
        Quality = "Good",
        SourceMode = "BRCB",
        Reason = "data-change"
    };

    private sealed record Fixture(
        IoTestProject Project,
        IoTestIedPlan IedA,
        IoTestIedPlan IedB,
        IoTestPointPlan PointA,
        IoTestPointPlan PointB,
        Iec61850MonitorDevice DeviceA,
        Iec61850MonitorDevice DeviceB,
        IReadOnlyCollection<Iec61850MonitorDevice> Devices,
        string Root);
}
