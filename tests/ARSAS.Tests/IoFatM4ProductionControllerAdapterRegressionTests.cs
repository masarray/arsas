using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatM4ProductionControllerAdapterRegressionTests
{
    [Fact]
    public void LatchedStart_DoesNotFollowExplorerSelection_AndKeepsFrozenScope()
    {
        var fixture = CreateFixture();
        fixture.PointA2.WorkspaceSelected = false;
        new IoTestLiveBindingService().Bind(fixture.Project, fixture.Devices);

        using var primary = NewController(fixture.Project, fixture.Devices, fixture.Root);
        using var sessions = new IoTestMultiSessionCoordinator(fixture.Project, primary);
        sessions.SelectContext(fixture.IedA);

        var lease = IoFatProductionControllerAdapter.LatchStartTarget(fixture.Project, fixture.IedA);
        Assert.Single(lease.Points);
        Assert.Same(fixture.PointA1, lease.Points[0].Point);

        // Simulate changes that can occur while Engineering connection preparation awaits:
        // the global Explorer now views B and a second row becomes selected. Neither may
        // redirect or widen the already-latched production evidence transaction.
        sessions.SelectContext(fixture.IedB);
        fixture.PointA2.WorkspaceSelected = true;

        var start = IoFatProductionControllerAdapter.StartLatched(fixture.Project, sessions, lease);

        Assert.True(start.Succeeded, start.Message);
        Assert.Same(fixture.IedB, sessions.SelectedIed);
        Assert.True(sessions.IsIedSessionActive(fixture.IedA));
        Assert.False(sessions.IsIedSessionActive(fixture.IedB));
        Assert.Contains(" / 1 complete", primary.ProgressText, StringComparison.Ordinal);

        Assert.True(sessions.Stop(fixture.IedA, "test cleanup").Succeeded);
    }

    [Fact]
    public void LatchedStart_FailsClosed_WhenEngineeringDeviceIdDriftsBeforeStart()
    {
        var fixture = CreateFixture();
        new IoTestLiveBindingService().Bind(fixture.Project, fixture.Devices);

        using var primary = NewController(fixture.Project, fixture.Devices, fixture.Root);
        using var sessions = new IoTestMultiSessionCoordinator(fixture.Project, primary);
        var lease = IoFatProductionControllerAdapter.LatchStartTarget(fixture.Project, fixture.IedA);
        Assert.Equal("device-a", lease.DeviceId, ignoreCase: true);

        fixture.IedA.ApplyLiveDeviceBinding(
            "device-b",
            "simulated Engineering binding drift",
            isConnected: true,
            isMonitoring: true);

        var start = IoFatProductionControllerAdapter.StartLatched(fixture.Project, sessions, lease);

        Assert.False(start.Succeeded);
        Assert.Contains("DeviceId", start.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(sessions.HasActiveSessions);
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.evidence.jsonl", SearchOption.AllDirectories));
    }

    [Fact]
    public void LatchedStart_FailsClosed_WhenFrozenRowEligibilityChanges()
    {
        var fixture = CreateFixture();
        new IoTestLiveBindingService().Bind(fixture.Project, fixture.Devices);

        using var primary = NewController(fixture.Project, fixture.Devices, fixture.Root);
        using var sessions = new IoTestMultiSessionCoordinator(fixture.Project, primary);
        var lease = IoFatProductionControllerAdapter.LatchStartTarget(fixture.Project, fixture.IedA);

        fixture.PointA1.WorkspaceSelected = false;
        var start = IoFatProductionControllerAdapter.StartLatched(fixture.Project, sessions, lease);

        Assert.False(start.Succeeded);
        Assert.Contains("eligibility", start.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(sessions.HasActiveSessions);
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.evidence.jsonl", SearchOption.AllDirectories));
    }

    [Fact]
    public void ContinuePauseAndStop_UseExplicitIedOwner_NotCurrentExplorerProjection()
    {
        var fixture = CreateFixture();
        new IoTestLiveBindingService().Bind(fixture.Project, fixture.Devices);

        using var primary = NewController(fixture.Project, fixture.Devices, fixture.Root);
        using var sessions = new IoTestMultiSessionCoordinator(fixture.Project, primary);
        sessions.SelectContext(fixture.IedA);
        var lease = IoFatProductionControllerAdapter.LatchStartTarget(fixture.Project, fixture.IedA);
        Assert.True(IoFatProductionControllerAdapter.StartLatched(fixture.Project, sessions, lease).Succeeded);

        Assert.True(sessions.Pause(fixture.IedA, "pause A").Succeeded);
        sessions.SelectContext(fixture.IedB);

        // Continue A while B is the viewed IED. The selected projection must remain B,
        // while the active production controller continues its already-owned A session.
        var resume = sessions.Resume(fixture.IedA);
        Assert.True(resume.Succeeded, resume.Message);
        Assert.Same(fixture.IedB, sessions.SelectedIed);
        Assert.True(sessions.IsIedSessionActive(fixture.IedA));
        Assert.False(sessions.IsIedSessionActive(fixture.IedB));

        var stop = sessions.Stop(fixture.IedA, "stop A");
        Assert.True(stop.Succeeded, stop.Message);
        Assert.False(sessions.HasActiveSessions);
        Assert.Same(fixture.IedB, sessions.SelectedIed);
    }

    private static Fixture CreateFixture()
    {
        var pointA1 = Point("A-1", "IED_A", "192.168.81.10", "IED_ALD0/GGIO1.Ind1.stVal");
        var pointA2 = Point("A-2", "IED_A", "192.168.81.10", "IED_ALD0/GGIO1.Ind2.stVal");
        var pointB = Point("B-1", "IED_B", "192.168.81.11", "IED_BLD0/GGIO1.Ind1.stVal");
        var iedA = Ied("IED_A", "192.168.81.10", pointA1, pointA2);
        var iedB = Ied("IED_B", "192.168.81.11", pointB);
        var project = new IoTestProject
        {
            ProjectId = "M4-CAPTURE-LEASE",
            SchemaVersion = "ARSAS-FAT-IO-1.0",
            ProjectName = "M4 Production Adapter",
            SourceWorkbookName = "M4.xlsx",
            SourceWorkbookSha256 = new string('b', 64),
            Ieds = { iedA, iedB }
        };
        project.InitializeRuntimeNotifications();

        var deviceA = Device("device-a", iedA, pointA1, pointA2);
        var deviceB = Device("device-b", iedB, pointB);
        var devices = new[] { deviceA, deviceB };
        var root = Path.Combine(Path.GetTempPath(), "ARSAS.Tests", "M4", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new Fixture(project, iedA, iedB, pointA1, pointA2, pointB, devices, root);
    }

    private static IoTestPointPlan Point(string id, string iedName, string ipAddress, string reference) => new()
    {
        TestPointId = id,
        IedName = iedName,
        IpAddress = ipAddress,
        SignalName = id,
        ObjectReference = reference,
        FunctionalConstraint = "ST",
        ExpectedOnText = "ON",
        ExpectedOffText = "OFF",
        ExpectedOnRaw = 1,
        ExpectedOffRaw = 0,
        DataType = "BOOLEAN",
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

    private static IoTestIedPlan Ied(string name, string ipAddress, params IoTestPointPlan[] points)
    {
        var ied = new IoTestIedPlan
        {
            IedName = name,
            IpAddress = ipAddress,
            IedRole = "Protection IED"
        };
        ied.TestPoints.AddRange(points);
        return ied;
    }

    private static Iec61850MonitorDevice Device(
        string deviceId,
        IoTestIedPlan ied,
        params IoTestPointPlan[] points)
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

        foreach (var point in points)
        {
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
                DeviceTimestamp = "2026-09-10T04:00:00.000Z",
                SourceMode = "BRCB",
                Sequence = 0,
                Status = "Live"
            });
        }
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

    private sealed record Fixture(
        IoTestProject Project,
        IoTestIedPlan IedA,
        IoTestIedPlan IedB,
        IoTestPointPlan PointA1,
        IoTestPointPlan PointA2,
        IoTestPointPlan PointB,
        IReadOnlyCollection<Iec61850MonitorDevice> Devices,
        string Root);
}
