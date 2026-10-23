using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoTestSessionScopeTests
{
    [Fact]
    public void Start_RejectsPartiallyBoundEnabledScope()
    {
        var first = Point("TP-001", "AA1C1F03R4ADD/GGIO6.CBClsd.stVal");
        var second = Point("TP-002", "AA1C1F03R4ADD/GGIO6.CBOpn.stVal");
        var ied = new IoTestIedPlan
        {
            IedName = "AA1C1F03R4",
            IpAddress = "192.168.81.70",
            TestPoints = { first, second }
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
        device.Points.Add(LivePoint(device, first));
        new IoTestLiveBindingService().Bind(project, new[] { device });

        var root = Path.Combine(Path.GetTempPath(), "ARSAS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var controller = new IoTestSessionController(
            project,
            _ => device,
            action => action(),
            root);

        var result = controller.Start(ied);

        Assert.False(result.Succeeded);
        Assert.Equal(IoTestSessionState.Idle, controller.State);
        Assert.Contains("1 of 2", result.Message, StringComparison.Ordinal);
    }

    private static IoTestPointPlan Point(string id, string reference) => new()
    {
        TestPointId = id,
        IedName = "AA1C1F03R4",
        IpAddress = "192.168.81.70",
        SignalName = id,
        ObjectReference = reference,
        FunctionalConstraint = "ST",
        ExpectedOnText = "Active",
        ExpectedOffText = "InActive",
        ImportReady = true,
        BindingStatus = "CID_DATASET_EXACT"
    };

    private static Iec61850MonitorPoint LivePoint(
        Iec61850MonitorDevice device,
        IoTestPointPlan point) => new()
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
}
