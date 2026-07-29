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

    private static IoTestIedPlan Ied(params IoTestPointPlan[] points) => new()
    {
        IedName = "AA1C1F00R1",
        IpAddress = "192.168.81.2",
        IedRole = "7SJ82 BZ",
        TestPoints = points.ToList()
    };

    private static IoTestPointPlan Point(string id, string reference, string functionalConstraint = "ST") => new()
    {
        TestPointId = id,
        IedName = "AA1C1F00R1",
        IpAddress = "192.168.81.2",
        SignalName = id,
        ObjectReference = reference,
        FunctionalConstraint = functionalConstraint,
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
