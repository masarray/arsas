using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatCommissioningRecoveryRegressionTests
{
    [Fact]
    public void FatEvidence_UsesDelayedSoeOnlyAsFallbackAfterDirectObservationRoute()
    {
        var source = ReadRepoFile("MainWindow.IoTesting.CommissioningRecovery.cs");

        Assert.Contains("_runtime.EventRaised += CommissioningRuntime_EventRaised", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke", source, StringComparison.Ordinal);
        Assert.Contains("DeliverFatSoeFallback", source, StringComparison.Ordinal);
        Assert.Contains("FatControllerNeedsSoeFallback", source, StringComparison.Ordinal);
        Assert.Contains("point.Runtime.CurrentValue", source, StringComparison.Ordinal);
        Assert.Contains("controller.Enqueue(entry);", source, StringComparison.Ordinal);
        Assert.Contains("normal operation this becomes a no-op, not a duplicate", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParallelFatSessions_RegisterPrimaryAndEverySiblingForRecovery()
    {
        var source = ReadRepoFile("MainWindow.IoTesting.MultiSessionEvidence.cs");

        Assert.Contains("RegisterFatCommissioningController(coordinator.PrimaryController);", source, StringComparison.Ordinal);
        Assert.Contains("RegisterFatCommissioningController(controller);", source, StringComparison.Ordinal);
        Assert.Contains("ClearFatCommissioningControllers();", source, StringComparison.Ordinal);
        Assert.Contains("coordinator.EnqueueAdditional", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FatReconnect_AutoResumesEachInterruptedControllerOnlyAfterFreshBaselineSettleWindow()
    {
        var source = ReadRepoFile("MainWindow.IoTesting.CommissioningRecovery.cs");

        Assert.Contains("_fatCommissioningControllers.Keys.ToArray()", source, StringComparison.Ordinal);
        Assert.Contains("IoTestSessionState.Interrupted", source, StringComparison.Ordinal);
        Assert.Contains("FatAutoResumeSettleDelay", source, StringComparison.Ordinal);
        Assert.Contains("FatAutoResumeRetryDelay", source, StringComparison.Ordinal);
        Assert.Contains("var resume = controller.Resume();", source, StringComparison.Ordinal);
        Assert.Contains("fresh connection-generation baseline", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FatIedExplorer_AlwaysProjectsIndependentConnectionHealth()
    {
        var source = ReadRepoFile("IoListTestingWindow.CommissioningStatus.cs");

        Assert.Contains("\"ONLINE\"", source, StringComparison.Ordinal);
        Assert.Contains("\"RECONNECTING\"", source, StringComparison.Ordinal);
        Assert.Contains("\"OFFLINE\"", source, StringComparison.Ordinal);
        Assert.Contains("badge.Visibility = Visibility.Visible", source, StringComparison.Ordinal);
        Assert.Contains("Connection health is independent from FAT PASS/FAIL", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateLiveAndSoeDelivery_DoesNotCreateDuplicateAcceptedEvidence()
    {
        var fixture = CreateDigitalFixture(1);
        using var controller = fixture.Controller;
        Assert.True(controller.Start(fixture.Ied).Succeeded);
        var point = fixture.Points[0];

        controller.Enqueue(Edge(fixture.Device, point, "False", "True", 100));
        Assert.True(point.HasValue2Evidence);
        Assert.Equal("True", point.Value2Text);
        var acceptedCount = controller.EvidenceRecordCount;

        // Runtime SOE has its own sequence domain. A delayed duplicate that describes the
        // same physical edge must not promote another Value 2 or append accepted evidence.
        controller.Enqueue(Edge(fixture.Device, point, "False", "True", 1));

        Assert.Equal(acceptedCount, controller.EvidenceRecordCount);
        Assert.Equal("True", point.Value2Text);
    }

    [Fact]
    public void RapidOffOnOff_IsLosslessAndCompletesTransitionEvidence()
    {
        var fixture = CreateDigitalFixture(1);
        using var controller = fixture.Controller;
        Assert.True(controller.Start(fixture.Ied).Succeeded);
        var point = fixture.Points[0];

        controller.Enqueue(Edge(fixture.Device, point, "False", "True", 1));
        controller.Enqueue(Edge(fixture.Device, point, "True", "False", 2));

        Assert.NotNull(point.Runtime.OnEvidence);
        Assert.NotNull(point.Runtime.OffEvidence);
        Assert.Equal(IoTestPointState.Passed, point.Runtime.State);
        Assert.True(point.IsFatEvidenceComplete);
    }

    [Fact]
    public void ManyIndependentDigitalChanges_AllCaptureValue2()
    {
        var fixture = CreateDigitalFixture(8);
        using var controller = fixture.Controller;
        Assert.True(controller.Start(fixture.Ied).Succeeded);

        long sequence = 10;
        foreach (var point in fixture.Points)
            controller.Enqueue(Edge(fixture.Device, point, "False", "True", ++sequence));

        Assert.All(fixture.Points, point =>
        {
            Assert.True(point.HasValue1Evidence);
            Assert.True(point.HasValue2Evidence);
            Assert.Equal("False", point.Value1Text);
            Assert.Equal("True", point.Value2Text);
        });
    }

    [Fact]
    public void ExistingFatSessionController_PreservesLosslessEdgeQueueAndGenerationBoundary()
    {
        var source = ReadRepoFile("Services/IoTesting/IoTestSessionController.cs");

        Assert.Contains("_pendingEdgeSnapshots.Enqueue(queued);", source, StringComparison.Ordinal);
        Assert.Contains("_connectionGeneration++;", source, StringComparison.Ordinal);
        Assert.Contains("current values are treated as a new baseline image", source, StringComparison.Ordinal);
    }

    private static DigitalFixture CreateDigitalFixture(int count)
    {
        var points = Enumerable.Range(1, count)
            .Select(index => NewDigitalPoint($"DI-{index}"))
            .ToList();
        var ied = new IoTestIedPlan
        {
            IedName = "IED1",
            IpAddress = "192.0.2.10",
            TestPoints = points
        };
        var project = new IoTestProject
        {
            ProjectId = "COMMISSIONING-RECOVERY",
            SchemaVersion = "ARSAS-FAT-SCL-1.0",
            ProjectName = "Commissioning recovery regression",
            Ieds = { ied }
        };
        project.InitializeRuntimeNotifications();

        var device = new Iec61850MonitorDevice
        {
            DeviceId = "commissioning-device",
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
                DeviceId = device.DeviceId,
                DeviceName = device.Name,
                IpAddress = device.IpAddress,
                SignalName = point.SignalName,
                IecReference = point.ObjectReference,
                FunctionalConstraint = "ST",
                IecDataType = "Boolean",
                Category = "Status",
                Value = "False",
                Quality = "Good",
                DeviceTimestamp = "2026-09-03T06:00:00.0000000Z",
                SourceMode = "MMS polling",
                Sequence = 0,
                Status = "Live"
            });
        }

        new IoTestLiveBindingService().Bind(project, new[] { device });
        var root = Path.Combine(Path.GetTempPath(), "ARSAS.Tests", "CommissioningRecovery", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var controller = new IoTestSessionController(
            project,
            key => Resolves(key, device) ? device : null,
            action => action(),
            root);

        return new DigitalFixture(ied, points, device, controller);
    }

    private static IoTestPointPlan NewDigitalPoint(string id)
        => new()
        {
            TestPointId = id,
            IedName = "IED1",
            IpAddress = "192.0.2.10",
            SignalName = id,
            ObjectReference = $"IED1LD0/GGIO1.{id}.stVal",
            FunctionalConstraint = "ST",
            ExpectedOnText = "TRUE",
            ExpectedOffText = "FALSE",
            DataType = "Boolean",
            SignalKind = FatSignalKind.Discrete,
            CaptureMode = FatCaptureMode.AutomaticTransition,
            WorkspaceSelected = true,
            TestEnabled = true,
            ImportReady = true,
            BindingStatus = IoTestSignalSelectionService.SclWorkspaceAuthorityBindingStatus
        };

    private static Iec61850EventEntry Edge(
        Iec61850MonitorDevice device,
        IoTestPointPlan point,
        string oldValue,
        string newValue,
        long sequence)
        => new()
        {
            Sequence = sequence,
            DeviceId = device.DeviceId,
            PointKey = $"{device.DeviceId}|{point.ObjectReference.ToLowerInvariant()}",
            DeviceTimestamp = $"2026-09-03T06:00:00.{sequence % 10000000:0000000}Z",
            DeviceName = device.Name,
            IpAddress = device.IpAddress,
            SignalName = point.SignalName,
            IecReference = point.ObjectReference,
            IecDataType = "Boolean",
            OldValue = oldValue,
            NewValue = newValue,
            Quality = "Good",
            SourceMode = "MMS polling",
            Reason = "commissioning regression edge"
        };

    private static bool Resolves(string key, Iec61850MonitorDevice device)
        => key.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase) ||
           key.Equals(device.Name, StringComparison.OrdinalIgnoreCase) ||
           key.Equals(device.IpAddress, StringComparison.OrdinalIgnoreCase);

    private static string ReadRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }

    private sealed record DigitalFixture(
        IoTestIedPlan Ied,
        List<IoTestPointPlan> Points,
        Iec61850MonitorDevice Device,
        IoTestSessionController Controller);
}
