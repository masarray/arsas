using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoTestSignalSelectionServiceTests
{
    private readonly IoTestSignalSelectionService _service = new();

    [Fact]
    public void ExactReference_SelectsOnlyImportedSignal()
    {
        var ied = Ied(Point("TP-001", "AA1C1F00R1ADD/GGIO2.LockoutOp.stVal"));
        var device = Device(
            Signal("Lockout", "AA1C1F00R1ADD/GGIO2.LockoutOp.stVal"),
            Signal("Unrelated", "AA1C1F00R1ADD/GGIO2.Other.stVal"));

        var result = _service.Resolve(ied, device);

        Assert.True(result.Succeeded);
        var match = Assert.Single(result.Matches);
        Assert.Equal("TP-001", match.TestPoint.TestPointId);
        Assert.Equal("Lockout", match.Signal.Name);
        Assert.False(match.UsedNormalizedIedPrefix);
    }

    [Fact]
    public void UniqueIedPrefixDifference_IsAccepted()
    {
        var ied = Ied(Point("TP-001", "ADD/GGIO2.LockoutOp.stVal"));
        var device = Device(Signal("Lockout", "AA1C1F00R1ADD/GGIO2.LockoutOp.stVal"));

        var result = _service.Resolve(ied, device);

        Assert.True(result.Succeeded);
        Assert.True(Assert.Single(result.Matches).UsedNormalizedIedPrefix);
    }

    [Fact]
    public void Rev3ApplicationDisplayWrapper_MatchesDiscoveredMmsReference()
    {
        var ied = Ied(Point(
            "UCC-IEC-0769",
            "AA1C1F00R1Application/ADD/GGIO2.LockoutOp.stVal"));
        var device = Device(Signal(
            "Lockout operated",
            "AA1C1F00R1ADD/GGIO2.LockoutOp.stVal"));

        var result = _service.Resolve(ied, device);

        Assert.True(result.Succeeded);
        var match = Assert.Single(result.Matches);
        Assert.Equal("Lockout operated", match.Signal.Name);
        Assert.True(match.UsedNormalizedIedPrefix);
    }

    [Fact]
    public void ExactEventLogReference_IsUsedWhenDisplayReferenceDiffers()
    {
        var ied = Ied(Point(
            "UCC-IEC-0773",
            "AA1C1F00R1Application/CB1/XCBR1.TripOpnCmd.stVal",
            eventLogReference: "CB1/XCBR1.TripOpnCmd",
            sourceIecReference: "CB1/XCBR1.TripOpnCmd",
            dataAttribute: "stVal"));
        var device = Device(Signal(
            "Protection operated",
            "AA1C1F00R1CB1/XCBR1.TripOpnCmd.stVal"));

        var result = _service.Resolve(ied, device);

        Assert.True(result.Succeeded);
        Assert.Single(result.Matches);
    }

    [Fact]
    public void Aa1C1F13R4_FunctionGroupFoldersMatchConcatenatedMmsLnPrefixes()
    {
        var ied = FieldIed(
            FieldPoint("UCC-IEC-0698", "ADD/GGIO1.LocOpnCMDsta"),
            FieldPoint("UCC-IEC-0699", "ADD/GGIO1.LocClsCMDsta"),
            FieldPoint("UCC-IEC-0700", "ADD/GGIO1.SwLoc"),
            FieldPoint("UCC-IEC-0701", "ADD/GGIO1.SwRem"));
        var device = FieldDevice(
            Signal("Local open command status", "AA1C1F13R4Application/ADDGGIO1.LocOpnCMDsta.stVal"),
            Signal("Local close command status", "AA1C1F13R4Application/ADDGGIO1.LocClsCMDsta.stVal"),
            Signal("Selector local", "AA1C1F13R4Application/ADDGGIO1.SwLoc.stVal"),
            Signal("Selector remote", "AA1C1F13R4Application/ADDGGIO1.SwRem.stVal"));

        var result = _service.Resolve(ied, device);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(4, result.Matches.Count);
        Assert.All(result.Matches, match => Assert.True(match.UsedNormalizedIedPrefix));
        Assert.Empty(result.MissingPoints);
        Assert.Empty(result.AmbiguousPoints);
    }

    [Fact]
    public void SiemensOperationalValuesFolderMatchesConcatenatedMmsLnPrefix()
    {
        var ied = new IoTestIedPlan
        {
            IedName = "AA1C1F13R4",
            IpAddress = "192.168.81.17",
            IedRole = "BCU - 6MD85",
            TestPoints =
            [
                new IoTestPointPlan
                {
                    TestPointId = "UCC-IEC-0738",
                    IedName = "AA1C1F13R4",
                    IpAddress = "192.168.81.17",
                    SignalName = "Line 1 Current",
                    ObjectReference = "AA1C1F13R4Application/VI3p1_OperationalValues/RPRE_MMXU1.A.cVal.mag.f",
                    SourceIecReference = "VI3p1_OperationalValues/RPRE_MMXU1.A",
                    EventLogSearchReference = "VI3p1_OperationalValues/RPRE_MMXU1.A",
                    DataAttribute = "cVal.mag.f",
                    FunctionalConstraint = "MX",
                    ImportReady = true,
                    TestEnabled = true
                }
            ]
        };
        var device = new Iec61850MonitorDevice
        {
            Name = "AA1C1F13R4",
            SclIedName = "AA1C1F13R4",
            IpAddress = "192.168.81.17",
            Port = 102
        };
        device.Signals.Add(Signal(
            "Line 1 Current",
            "AA1C1F13R4Application/VI3p1_OperationalValuesRPRE_MMXU1.A.cVal.mag.f",
            "MX"));

        var result = _service.Resolve(ied, device);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(Assert.Single(result.Matches).UsedNormalizedIedPrefix);
    }

    [Fact]
    public void HierarchyCollapseCollision_IsRejectedAsAmbiguous()
    {
        var ied = FieldIed(FieldPoint("UCC-IEC-0700", "ADD/GGIO1.SwLoc"));
        var device = FieldDevice(
            Signal("Expected form", "AA1C1F13R4Application/ADDGGIO1.SwLoc.stVal"),
            Signal("Colliding hierarchy", "AA1C1F13R4Application/AD/DGGIO1.SwLoc.stVal"));

        var result = _service.Resolve(ied, device);

        Assert.False(result.Succeeded);
        Assert.False(result.CanRetryWithFreshDiscovery);
        Assert.Single(result.AmbiguousPoints);
        Assert.Empty(result.MissingPoints);
    }

    [Fact]
    public void MissingSignal_RequestsOneFreshDiscoveryRetry()
    {
        var ied = Ied(Point("TP-001", "AA1C1F00R1ADD/GGIO2.Missing.stVal"));
        var device = Device(Signal("Other", "AA1C1F00R1ADD/GGIO2.Other.stVal"));

        var result = _service.Resolve(ied, device);

        Assert.False(result.Succeeded);
        Assert.True(result.CanRetryWithFreshDiscovery);
        Assert.Single(result.MissingPoints);
        Assert.Empty(result.AmbiguousPoints);
    }

    [Fact]
    public void DuplicateDiscoveredReference_IsRejectedAsAmbiguous()
    {
        const string reference = "AA1C1F00R1ADD/GGIO2.LockoutOp.stVal";
        var ied = Ied(Point("TP-001", reference));
        var device = Device(Signal("A", reference), Signal("B", reference));

        var result = _service.Resolve(ied, device);

        Assert.False(result.Succeeded);
        Assert.False(result.CanRetryWithFreshDiscovery);
        Assert.Single(result.AmbiguousPoints);
    }

    [Fact]
    public void OneDiscoveredSignal_CannotFeedTwoTestPoints()
    {
        const string reference = "AA1C1F00R1ADD/GGIO2.LockoutOp.stVal";
        var ied = Ied(
            Point("TP-001", reference),
            Point("TP-002", reference));
        var device = Device(Signal("Lockout", reference));

        var result = _service.Resolve(ied, device);

        Assert.False(result.Succeeded);
        Assert.Single(result.Matches);
        Assert.Single(result.AmbiguousPoints);
    }

    [Fact]
    public void KnownFunctionalConstraintMismatch_IsNotAccepted()
    {
        const string reference = "AA1C1F00R1ADD/GGIO2.LockoutOp.stVal";
        var ied = Ied(Point("TP-001", reference, "ST"));
        var device = Device(Signal("Wrong FC", reference, "MX"));

        var result = _service.Resolve(ied, device);

        Assert.False(result.Succeeded);
        Assert.Single(result.MissingPoints);
    }

    private static IoTestIedPlan FieldIed(params IoTestPointPlan[] points) => new()
    {
        IedName = "AA1C1F13R4",
        IpAddress = "192.168.81.17",
        IedRole = "BCU - 6MD85",
        TestPoints = points.ToList()
    };

    private static IoTestPointPlan FieldPoint(string id, string eventReference) => new()
    {
        TestPointId = id,
        IedName = "AA1C1F13R4",
        IpAddress = "192.168.81.17",
        SignalName = id,
        ObjectReference = $"AA1C1F13R4Application/{eventReference}.stVal",
        SourceIecReference = eventReference,
        EventLogSearchReference = eventReference,
        ReportDisplayReference = $"AA1C1F13R4Application/{eventReference}.stVal [ST]",
        DataAttribute = "stVal",
        LogicalDevice = "AA1C1F13R4Application",
        FunctionalConstraint = "ST",
        ExpectedOnText = "Active",
        ExpectedOffText = "InActive",
        ImportReady = true,
        TestEnabled = true
    };

    private static Iec61850MonitorDevice FieldDevice(params SignalDefinition[] signals)
    {
        var device = new Iec61850MonitorDevice
        {
            Name = "AA1C1F13R4",
            SclIedName = "AA1C1F13R4",
            IpAddress = "192.168.81.17",
            Port = 102
        };
        device.Signals.AddRange(signals);
        return device;
    }

    private static IoTestIedPlan Ied(params IoTestPointPlan[] points) => new()
    {
        IedName = "AA1C1F00R1",
        IpAddress = "192.168.81.2",
        IedRole = "7SJ82 BZ",
        TestPoints = points.ToList()
    };

    private static IoTestPointPlan Point(
        string id,
        string reference,
        string functionalConstraint = "ST",
        string eventLogReference = "",
        string sourceIecReference = "",
        string dataAttribute = "",
        string logicalDevice = "") => new()
    {
        TestPointId = id,
        IedName = "AA1C1F00R1",
        IpAddress = "192.168.81.2",
        SignalName = id,
        ObjectReference = reference,
        FunctionalConstraint = functionalConstraint,
        EventLogSearchReference = eventLogReference,
        SourceIecReference = sourceIecReference,
        DataAttribute = dataAttribute,
        LogicalDevice = logicalDevice,
        ExpectedOnText = "Active",
        ExpectedOffText = "InActive",
        ImportReady = true,
        TestEnabled = true
    };

    private static Iec61850MonitorDevice Device(params SignalDefinition[] signals)
    {
        var device = new Iec61850MonitorDevice
        {
            Name = "AA1C1F00R1",
            SclIedName = "AA1C1F00R1",
            IpAddress = "192.168.81.2",
            Port = 102
        };
        device.Signals.AddRange(signals);
        return device;
    }

    private static SignalDefinition Signal(
        string name,
        string reference,
        string functionalConstraint = "ST") => new()
    {
        Name = name,
        ObjectReference = reference,
        FunctionalConstraint = functionalConstraint
    };
}
