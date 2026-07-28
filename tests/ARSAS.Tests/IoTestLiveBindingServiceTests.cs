using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoTestLiveBindingServiceTests
{
    private readonly IoTestLiveBindingService _binding = new();

    [Fact]
    public void ExactDiscoveredSignal_IsBoundToImportedPoint()
    {
        var project = Project("AA1C1F03R4ADD/GGIO6.CBClsd.stVal");
        var device = Device();
        device.Signals.Add(new SignalDefinition
        {
            Name = "CB closed",
            ObjectReference = "AA1C1F03R4ADD/GGIO6.CBClsd.stVal",
            FunctionalConstraint = "ST"
        });

        var summary = _binding.Bind(project, new[] { device });

        Assert.Equal(1, summary.DeviceBoundCount);
        Assert.Equal(1, summary.SignalBoundCount);
        Assert.Equal(IoTestLiveBindingState.BoundExact, project.Ieds[0].TestPoints[0].LiveBindingState);
    }

    [Fact]
    public void IedPrefixDifference_IsNormalizedWhenUnique()
    {
        var project = Project("ADD/GGIO6.CBClsd.stVal");
        var device = Device();
        device.Signals.Add(new SignalDefinition
        {
            Name = "CB closed",
            ObjectReference = "AA1C1F03R4ADD/GGIO6.CBClsd.stVal",
            FunctionalConstraint = "ST"
        });

        _binding.Bind(project, new[] { device });

        Assert.Equal(IoTestLiveBindingState.BoundNormalized, project.Ieds[0].TestPoints[0].LiveBindingState);
    }

    [Fact]
    public void ActiveLivePoint_PopulatesCurrentEvidencePreview()
    {
        var project = Project("AA1C1F03R4ADD/GGIO6.CBClsd.stVal");
        var device = Device();
        device.Points.Add(new Iec61850MonitorPoint
        {
            DeviceId = device.DeviceId,
            DeviceName = device.Name,
            IpAddress = device.IpAddress,
            SignalName = "CB closed",
            IecReference = "AA1C1F03R4ADD/GGIO6.CBClsd.stVal",
            FunctionalConstraint = "ST",
            Value = "True",
            Quality = "Good",
            SourceMode = "BRCB"
        });

        var summary = _binding.Bind(project, new[] { device });

        Assert.Equal(1, summary.LivePointCount);
        var point = project.Ieds[0].TestPoints[0];
        Assert.Equal(IoTestLiveBindingState.LivePointReady, point.LiveBindingState);
        Assert.Equal("True", point.Runtime.CurrentValue);
        Assert.Equal("Good", point.Runtime.CurrentQuality);
        Assert.Equal("BRCB", point.Runtime.CurrentSource);
    }

    [Fact]
    public void MissingWorkspaceDevice_IsExplicitlyReported()
    {
        var project = Project("AA1C1F03R4ADD/GGIO6.CBClsd.stVal");

        var summary = _binding.Bind(project, Array.Empty<Iec61850MonitorDevice>());

        Assert.Equal(0, summary.DeviceBoundCount);
        Assert.Equal(IoTestLiveBindingState.DeviceNotLoaded, project.Ieds[0].TestPoints[0].LiveBindingState);
        Assert.Contains("load or connect", project.Ieds[0].TestPoints[0].LiveBindingReason.ToLowerInvariant());
    }

    private static IoTestProject Project(string reference)
    {
        var project = new IoTestProject
        {
            ProjectId = "CCPP-260728",
            SchemaVersion = "ARSAS-FAT-IO-1.0",
            ProjectName = "CCPP FAT",
            Ieds =
            {
                new IoTestIedPlan
                {
                    IedName = "AA1C1F03R4",
                    IpAddress = "192.168.81.70",
                    IedRole = "BCU - 6MD85",
                    TestPoints = { Point(reference) }
                }
            }
        };
        project.InitializeRuntimeNotifications();
        return project;
    }

    private static IoTestPointPlan Point(string reference) => new()
    {
        TestPointId = "TP-001",
        IedName = "AA1C1F03R4",
        IpAddress = "192.168.81.70",
        SignalName = "CB closed",
        ObjectReference = reference,
        FunctionalConstraint = "ST",
        ExpectedOnText = "Active",
        ExpectedOffText = "InActive",
        ImportReady = true,
        BindingStatus = "CID_DATASET_EXACT"
    };

    private static Iec61850MonitorDevice Device() => new()
    {
        Name = "AA1C1F03R4",
        SclIedName = "AA1C1F03R4",
        IpAddress = "192.168.81.70",
        Port = 102,
        Status = "Ready"
    };
}
