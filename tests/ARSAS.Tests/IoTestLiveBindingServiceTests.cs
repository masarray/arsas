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

    [Fact]
    public void VendorApplicationDollarReference_PreservesLogicalDeviceBeforeLogicalNode()
    {
        Assert.True(CanonicalIecReference.TryParse(
            "AA1C1F13R4Application/ADD$GGIO1$ST$LocOpnCMDsta$stVal",
            "AA1C1F13R4",
            null,
            out var parsed));

        Assert.Equal("AA1C1F13R4", parsed.Ied);
        Assert.Equal("Application", parsed.ApplicationWrapper);
        Assert.Equal("ADD", parsed.LogicalDevice);
        Assert.Equal("GGIO1", parsed.LogicalNode);
        Assert.Equal("LocOpnCMDsta", parsed.DataObject);
        Assert.Equal("stVal", parsed.DataAttribute);
        Assert.Equal("ST", parsed.FunctionalConstraint);
    }

    [Fact]
    public void F13R4Rev4_ThirtyPointResolverBindsCanonicalLiveModel()
    {
        var imported = new[]
        {
            "ADD/GGIO1.LocOpnCMDsta.stVal", "ADD/GGIO1.LocClsCMDsta.stVal", "ADD/GGIO1.SwLoc.stVal",
            "ADD/GGIO1.SwRem.stVal", "ADD/GGIO1.SwSupervsry.stVal", "ADD/GGIO6.CBOpnd.stVal",
            "ADD/GGIO6.CBClsd.stVal", "ADD/GGIO3.CBSprgChrg.stVal", "ADD/GGIO6.QZ1cnctd.stVal",
            "ADD/GGIO6.QZ1isltd.stVal", "ADD/GGIO6.QZ1earth.stVal", "ADD/GGIO6.QZ2cnctd.stVal",
            "ADD/GGIO6.QZ2isltd.stVal", "ADD/GGIO6.QZ2earth.stVal", "ADD/GGIO5.SF61stComm.stVal",
            "ADD/GGIO5.SF62ndBBI.stVal", "ADD/GGIO5.SF62ndCB.stVal", "ADD/GGIO5.SF62ndBBJ.stVal",
            "ADD/GGIO6.Q18Clsd.stVal", "ADD/GGIO6.Q18Opnd.stVal", "ADD/GGIO6.Q28Clsd.stVal",
            "ADD/GGIO6.Q28Opnd.stVal", "ADD/GGIO2.Lockout1Op.stVal", "ADD/GGIO2.Lockout2Op.stVal",
            "AA1C1F13R4Application/.TCS1Fail.stVal", "AA1C1F13R4Application/.TCS2Fail.stVal",
            "Q0_25Synchronization/CK_RSYN1.Rel", "ADD/GGIO2.ComFail.stVal", "ADD/GGIO2.TimeSynchrnz.stVal",
            "ADD/GGIO2.FWUpdated.stVal"
        };
        var live = imported
            .Select(reference => reference.Contains("TCS1Fail", StringComparison.OrdinalIgnoreCase)
                ? "AA1C1F13R4ADD/GGIO2.TCS1Fail.stVal"
                : reference.Contains("TCS2Fail", StringComparison.OrdinalIgnoreCase)
                    ? "AA1C1F13R4ADD/GGIO2.TCS2Fail.stVal"
                    : reference.StartsWith("ADD/", StringComparison.OrdinalIgnoreCase)
                        ? "AA1C1F13R4" + reference
                        : reference)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var project = Project(imported[0], "AA1C1F13R4");
        project.Ieds[0].TestPoints.Clear();
        for (var i = 0; i < imported.Length; i++)
            project.Ieds[0].TestPoints.Add(Point(imported[i], $"UCC-IEC-{7000 + i}", "AA1C1F13R4"));

        var device = Device("AA1C1F13R4");
        foreach (var reference in live)
        {
            device.Signals.Add(new SignalDefinition
            {
                Name = reference,
                ObjectReference = reference,
                FunctionalConstraint = "ST"
            });
        }

        var summary = _binding.Bind(project, new[] { device });
        Assert.Equal(30, project.Ieds[0].TestPoints.Count);
        Assert.Equal(30, summary.SignalBoundCount);
        Assert.All(project.Ieds[0].TestPoints, point => Assert.True(point.IsLiveBound, point.LiveBindingDiagnostics));
    }

    [Fact]
    public void F13R1CidDigitalDataSet_BindsNineValidRowsAndLeavesMalformedRowAsFinding()
    {
        var imported = new[]
        {
            "AA1C1F13R1Application/CB1/RBRF1.OpIn.general",
            "AA1C1F13R1Application/CB1/RBRF1.OpEx.general",
            "AA1C1F13R1Application/VI3p1_5051OC3phase1/II_PTOC1.Op.general",
            "AA1C1F13R1Application/VI3p1_5051NOCgndB1/NI_PTOC1.Op.general",
            "AA1C1F13R1Application/.Op.general",
            "AA1C1F13R1Application/VI1p1_27Undervoltage1/PTRC1.Op.general",
            "AA1C1F13R1Application/ADD/GGIO1.ComFail.stVal",
            "AA1C1F13R1Application/ADD/GGIO1.TimeSynchrnz.stVal",
            "AA1C1F13R1Application/ADD/GGIO1.PrSetChgd.stVal",
            "AA1C1F13R1Application/ADD/GGIO1.FWUpdated.stVal"
        };
        var observed = new[]
        {
            "AA1C1F13R1CB1/RBRF1.OpIn.general",
            "AA1C1F13R1CB1/RBRF1.OpEx.general",
            "AA1C1F13R1VI3p1_5051OC3phase1/II_PTOC1.Op.general",
            "AA1C1F13R1VI3p1_5051NOCgndB1/NI_PTOC1.Op.general",
            "AA1C1F13R1VI3p1_27Undervoltage1/PTRC1.Op.general",
            "AA1C1F13R1VI1p1_27Undervoltage1/PTRC1.Op.general",
            "AA1C1F13R1ADD/GGIO1.ComFail.stVal",
            "AA1C1F13R1ADD/GGIO1.TimeSynchrnz.stVal",
            "AA1C1F13R1ADD/GGIO1.PrSetChgd.stVal",
            "AA1C1F13R1ADD/GGIO1.FWUpdated.stVal"
        };
        var project = Project(imported[0], "AA1C1F13R1");
        project.Ieds[0].TestPoints.Clear();
        for (var i = 0; i < imported.Length; i++)
            project.Ieds[0].TestPoints.Add(Point(imported[i], $"UCC-IEC-{727 + i:0000}", "AA1C1F13R1"));

        var device = Device("AA1C1F13R1");
        foreach (var reference in observed)
        {
            device.Signals.Add(new SignalDefinition
            {
                Name = reference,
                ObjectReference = reference,
                FunctionalConstraint = "ST"
            });
        }

        var summary = _binding.Bind(project, new[] { device });

        Assert.Equal(9, summary.SignalBoundCount);
        Assert.Equal(1, summary.MissingSignalCount);
        Assert.Contains("Canonical imported", project.Ieds[0].TestPoints[4].LiveBindingDiagnostics, StringComparison.Ordinal);
        Assert.Contains("Closest live references", project.Ieds[0].TestPoints[4].LiveBindingDiagnostics, StringComparison.Ordinal);
    }

    private static IoTestProject Project(string reference, string iedName = "AA1C1F03R4")
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
                    IedName = iedName,
                    IpAddress = "192.168.81.70",
                    IedRole = "BCU - 6MD85",
                    TestPoints = { Point(reference) }
                }
            }
        };
        project.InitializeRuntimeNotifications();
        return project;
    }

    private static IoTestPointPlan Point(string reference, string id = "TP-001", string iedName = "AA1C1F03R4") => new()
    {
        TestPointId = id,
        IedName = iedName,
        IpAddress = "192.168.81.70",
        SignalName = "CB closed",
        ObjectReference = reference,
        FunctionalConstraint = "ST",
        ExpectedOnText = "Active",
        ExpectedOffText = "InActive",
        ImportReady = true,
        BindingStatus = "CID_DATASET_EXACT"
    };

    private static Iec61850MonitorDevice Device(string iedName = "AA1C1F03R4") => new()
    {
        Name = iedName,
        SclIedName = iedName,
        IpAddress = "192.168.81.70",
        Port = 102,
        Status = "Ready"
    };
}
