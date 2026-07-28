using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoTestSessionPreflightTests
{
    [Fact]
    public void Validate_RejectsEnabledSignalThatStillNeedsReview()
    {
        var ied = Ied(Point("TP-001", "LD/GGIO1.Alm.stVal", importReady: false));

        var result = IoTestSessionPreflight.Validate(ied);

        Assert.False(result.Succeeded);
        Assert.Contains("require import/binding review", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsDuplicateEnabledLiveReference()
    {
        var ied = Ied(
            Point("TP-001", "AA1C1F03R4ADD/GGIO6.CBClsd.stVal"),
            Point("TP-002", "AA1C1F03R4ADD/GGIO6.CBClsd.stVal"));

        var result = IoTestSessionPreflight.Validate(ied);

        Assert.False(result.Succeeded);
        Assert.Contains("multiple enabled test points", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TP-001", result.Message, StringComparison.Ordinal);
        Assert.Contains("TP-002", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AcceptsUniqueImportReadyScope()
    {
        var ied = Ied(
            Point("TP-001", "AA1C1F03R4ADD/GGIO6.CBClsd.stVal"),
            Point("TP-002", "AA1C1F03R4ADD/GGIO6.CBOpn.stVal"));

        var result = IoTestSessionPreflight.Validate(ied);

        Assert.True(result.Succeeded, result.Message);
    }

    private static IoTestIedPlan Ied(params IoTestPointPlan[] points) => new()
    {
        IedName = "AA1C1F03R4",
        IpAddress = "192.168.81.70",
        TestPoints = points.ToList()
    };

    private static IoTestPointPlan Point(string id, string reference, bool importReady = true) => new()
    {
        TestPointId = id,
        IedName = "AA1C1F03R4",
        IpAddress = "192.168.81.70",
        SignalName = id,
        ObjectReference = reference,
        FunctionalConstraint = "ST",
        ExpectedOnText = "Active",
        ExpectedOffText = "InActive",
        ImportReady = importReady,
        BindingStatus = importReady ? "CID_DATASET_EXACT" : "SOURCE_ONLY",
        TestEnabled = true
    };
}
