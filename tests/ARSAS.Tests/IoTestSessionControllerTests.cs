using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoTestSessionControllerTests
{
    [Fact]
    public void LiveOffOnOffSequence_CompletesSessionAndPersistsEvidence()
    {
        var fixture = CreateFixture();
        using var controller = fixture.Controller;

        var started = controller.Start(fixture.Ied);
        controller.Enqueue(Event(fixture, "False", "True", 1));
        controller.Enqueue(Event(fixture, "True", "False", 2));

        Assert.True(started.Succeeded, started.Message);
        Assert.Equal(IoTestPointState.Passed, fixture.Point.Runtime.State);
        Assert.Equal(IoTestSessionState.Completed, controller.State);
        Assert.NotNull(fixture.Point.Runtime.OnEvidence);
        Assert.NotNull(fixture.Point.Runtime.OffEvidence);
        var verification = IoTestEvidenceJournal.Verify(controller.JournalPath);
        Assert.True(verification.IsValid, verification.Error);
        Assert.True(verification.RecordCount >= 5);
    }

    [Fact]
    public void ResumeAfterOnEvidence_ForcesReviewBecausePausedEdgesCouldBeMissed()
    {
        var fixture = CreateFixture();
        using var controller = fixture.Controller;
        controller.Start(fixture.Ied);
        controller.Enqueue(Event(fixture, "False", "True", 1));
        fixture.LivePoint.Value = "True";
        fixture.LivePoint.Sequence = 1;

        controller.Pause();
        var resumed = controller.Resume();

        Assert.True(resumed.Succeeded, resumed.Message);
        Assert.Equal(IoTestPointState.Review, fixture.Point.Runtime.State);
        Assert.Equal(IoTestSessionState.Completed, controller.State);
        Assert.Contains("continuity cannot be proven", fixture.Point.Runtime.StatusReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resume_RebindsReplacementLivePointAfterMonitorRestart()
    {
        var fixture = CreateFixture();
        using var controller = fixture.Controller;
        controller.Start(fixture.Ied);
        controller.Pause();

        fixture.Device.Points.Clear();
        var replacement = new Iec61850MonitorPoint
        {
            DeviceId = fixture.Device.DeviceId,
            DeviceName = fixture.Device.Name,
            IpAddress = fixture.Device.IpAddress,
            SignalName = fixture.Point.SignalName,
            IecReference = fixture.Point.ObjectReference,
            FunctionalConstraint = "ST",
            Value = "False",
            Quality = "Good",
            DeviceTimestamp = "2026-07-28T08:01:00.000Z",
            SourceMode = "BRCB",
            Sequence = 10,
            Status = "Live"
        };
        fixture.Device.Points.Add(replacement);

        var resumed = controller.Resume();
        controller.Enqueue(Event(fixture, replacement, "False", "True", 11));
        controller.Enqueue(Event(fixture, replacement, "True", "False", 12));

        Assert.True(resumed.Succeeded, resumed.Message);
        Assert.Equal(IoTestPointState.Passed, fixture.Point.Runtime.State);
        Assert.Equal(IoTestSessionState.Completed, controller.State);
    }

    [Fact]
    public void DeviceDisconnect_InterruptsRunningSession()
    {
        var fixture = CreateFixture();
        using var controller = fixture.Controller;
        controller.Start(fixture.Ied);

        fixture.Device.IsConnected = false;

        Assert.Equal(IoTestSessionState.Interrupted, controller.State);
        Assert.Contains("interrupted", controller.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Start_RejectsIedWithoutLiveMonitoringPoint()
    {
        var fixture = CreateFixture();
        fixture.Device.Points.Clear();
        using var controller = NewController(fixture.Project, fixture.Device, fixture.Root);

        var result = controller.Start(fixture.Ied);

        Assert.False(result.Succeeded);
        Assert.Equal(IoTestSessionState.Idle, controller.State);
    }

    private static Fixture CreateFixture()
    {
        var point = new IoTestPointPlan
        {
            TestPointId = "TP-001",
            IedName = "AA1C1F03R4",
            IpAddress = "192.168.81.70",
            SignalName = "CB closed",
            ObjectReference = "AA1C1F03R4ADD/GGIO6.CBClsd.stVal",
            FunctionalConstraint = "ST",
            ExpectedOnText = "Active",
            ExpectedOffText = "InActive",
            ImportReady = true,
            BindingStatus = "CID_DATASET_EXACT"
        };
        var ied = new IoTestIedPlan
        {
            IedName = "AA1C1F03R4",
            IpAddress = "192.168.81.70",
            TestPoints = { point }
        };
        var project = new IoTestProject
        {
            ProjectId = "CCPP-260728",
            SchemaVersion = "ARSAS-FAT-IO-1.0",
            ProjectName = "CCPP FAT",
            SourceWorkbookName = "CCPP.xlsx",
            SourceWorkbookSha256 = new string('a', 64),
            Ieds = { ied }
        };
        project.InitializeRuntimeNotifications();

        var device = new Iec61850MonitorDevice
        {
            DeviceId = "device-1",
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
            FunctionalConstraint = "ST",
            Value = "False",
            Quality = "Good",
            DeviceTimestamp = "2026-07-28T08:00:00.000Z",
            SourceMode = "BRCB",
            Sequence = 0,
            Status = "Live"
        };
        device.Points.Add(livePoint);
        new IoTestLiveBindingService().Bind(project, new[] { device });
        var root = Path.Combine(Path.GetTempPath(), "ARSAS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new Fixture(project, ied, point, device, livePoint, root, NewController(project, device, root));
    }

    private static IoTestSessionController NewController(
        IoTestProject project,
        Iec61850MonitorDevice device,
        string root) => new(
            project,
            key => key.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                   key.Equals(device.Name, StringComparison.OrdinalIgnoreCase) ||
                   key.Equals(device.IpAddress, StringComparison.OrdinalIgnoreCase)
                ? device
                : null,
            action => action(),
            root);

    private static Iec61850EventEntry Event(Fixture fixture, string oldValue, string newValue, long sequence)
        => Event(fixture, fixture.LivePoint, oldValue, newValue, sequence);

    private static Iec61850EventEntry Event(
        Fixture fixture,
        Iec61850MonitorPoint livePoint,
        string oldValue,
        string newValue,
        long sequence) => new()
    {
        Sequence = sequence,
        DeviceId = fixture.Device.DeviceId,
        PointKey = livePoint.PointKey,
        DeviceTimestamp = new DateTimeOffset(2026, 7, 28, 8, 0, 0, TimeSpan.Zero)
            .AddMilliseconds(sequence * 100)
            .ToString("O"),
        DeviceName = fixture.Device.Name,
        IpAddress = fixture.Device.IpAddress,
        SignalName = fixture.Point.SignalName,
        IecReference = livePoint.IecReference,
        OldValue = oldValue,
        NewValue = newValue,
        Quality = "Good",
        SourceMode = "BRCB",
        Reason = "data-change"
    };

    private sealed record Fixture(
        IoTestProject Project,
        IoTestIedPlan Ied,
        IoTestPointPlan Point,
        Iec61850MonitorDevice Device,
        Iec61850MonitorPoint LivePoint,
        string Root,
        IoTestSessionController Controller);
}
