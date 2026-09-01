using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoTestSessionPointUpdateTests
{
    [Fact]
    public void GoodQualityPointUpdate_RecoversRejectedInitialBaselineAndKeepsCaptureRunning()
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
            IedName = point.IedName,
            IpAddress = point.IpAddress,
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
        var live = new Iec61850MonitorPoint
        {
            DeviceId = device.DeviceId,
            DeviceName = device.Name,
            IpAddress = device.IpAddress,
            SignalName = point.SignalName,
            IecReference = point.ObjectReference,
            FunctionalConstraint = "ST",
            Value = "False",
            Quality = "Invalid",
            DeviceTimestamp = "2026-07-28T08:00:00.000Z",
            SourceMode = "BRCB",
            Sequence = 0,
            Status = "Live"
        };
        device.Points.Add(live);
        new IoTestLiveBindingService().Bind(project, new[] { device });
        var root = Path.Combine(Path.GetTempPath(), "ARSAS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var controller = new IoTestSessionController(project, _ => device, action => action(), root);

        var started = controller.Start(ied);
        controller.Enqueue(Event(device, live, "False", "False", "Good", 1));
        controller.Enqueue(Event(device, live, "False", "True", "Good", 2));
        controller.Enqueue(Event(device, live, "True", "False", "Good", 3));

        Assert.True(started.Succeeded, started.Message);
        Assert.Equal(IoTestPointState.Passed, point.Runtime.State);
        Assert.Equal(IoTestSessionState.Running, controller.State);
        Assert.Contains("Capture remains running", controller.StatusText, StringComparison.OrdinalIgnoreCase);
        var journal = File.ReadAllText(controller.JournalPath);
        Assert.Contains("waiting for good-quality baseline", journal, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"eventType\":\"baseline_state\"", journal, StringComparison.Ordinal);
    }

    private static Iec61850EventEntry Event(
        Iec61850MonitorDevice device,
        Iec61850MonitorPoint point,
        string oldValue,
        string newValue,
        string quality,
        long sequence) => new()
    {
        Sequence = sequence,
        DeviceId = device.DeviceId,
        PointKey = point.PointKey,
        DeviceTimestamp = new DateTimeOffset(2026, 7, 28, 8, 0, 0, TimeSpan.Zero)
            .AddMilliseconds(sequence * 100)
            .ToString("O"),
        DeviceName = device.Name,
        IpAddress = device.IpAddress,
        SignalName = point.SignalName,
        IecReference = point.IecReference,
        OldValue = oldValue,
        NewValue = newValue,
        Quality = quality,
        SourceMode = "BRCB",
        Reason = sequence == 1 ? "quality-change" : "data-change"
    };
}
