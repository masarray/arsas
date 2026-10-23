using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoTestSmartReferenceMatcherTests
{
    private readonly IoTestSignalSelectionService _service = new();

    [Fact]
    public void MmsFunctionalConstraintTokens_DoNotCauseSignalMissing()
    {
        var ied = Ied(Point("TP-001", "Protection/DifPDIF1.Op.general", "ST", "DifPDIF1"));
        var device = Device(Signal(
            "Differential operate",
            "AA1C1F06R2Protection/DifPDIF1$ST$Op$general",
            "ST"));

        var result = _service.Resolve(ied, device);

        Assert.True(result.Succeeded, result.Message);
        var match = Assert.Single(result.Matches);
        Assert.Equal("Differential operate", match.Signal.Name);
        Assert.True(match.UsedNormalizedIedPrefix);
    }

    [Fact]
    public void IedPrefixedApplicationAndMmsFcTokens_MatchTogether()
    {
        var ied = Ied(Point(
            "TP-002",
            "AA1C1F06R2Application/System/LLN0.SyncSt.stVal",
            "ST",
            "LLN0"));
        var device = Device(Signal(
            "Clock sync status",
            "AA1C1F06R2Application/SystemLLN0$ST$SyncSt$stVal",
            "ST"));

        var result = _service.Resolve(ied, device);

        Assert.True(result.Succeeded, result.Message);
        Assert.Single(result.Matches);
    }

    [Fact]
    public void MissingDefaultStVal_IsAcceptedOnlyWhenCandidateIsUnique()
    {
        var ied = Ied(Point("TP-003", "CTRL/SPo32GGIO1.Ind1", "ST", "SPo32GGIO1"));
        var device = Device(Signal(
            "Binary indication",
            "AA1C1F06R2CTRL/SPo32GGIO1$ST$Ind1$stVal",
            "ST"));

        var result = _service.Resolve(ied, device);

        Assert.True(result.Succeeded, result.Message);
        Assert.Single(result.Matches);
    }

    [Fact]
    public void EqualSmartCandidates_RemainAmbiguousInsteadOfBeingGuessed()
    {
        var ied = Ied(Point("TP-004", "CTRL/SPo32GGIO1.Ind1", "ST", "SPo32GGIO1"));
        var device = Device(
            Signal("A", "AA1C1F06R2CTRL/SPo32GGIO1$ST$Ind1$stVal", "ST"),
            Signal("B", "AA1C1F06R2CTRL/SPo32GGIO1.Ind1.stVal", "ST"));

        var result = _service.Resolve(ied, device);

        Assert.False(result.Succeeded);
        Assert.Single(result.AmbiguousPoints);
        Assert.Empty(result.MissingPoints);
    }

    [Fact]
    public void PartialTcsLeafReferences_ResolveUniquelyWithoutFuzzyGuessing()
    {
        var ied = FieldIed(
            FieldPoint("UCC-IEC-TCS1", ".TCS1Fail"),
            FieldPoint("UCC-IEC-TCS2", ".TCS2Fail"));
        var device = FieldDevice(
            Signal("Trip coil monitoring 1", "AA1C1F13R4Application/ADDGGIO2$ST$TCS1Fail$stVal", "ST"),
            Signal("Trip coil monitoring 2", "AA1C1F13R4Application/ADDGGIO2$ST$TCS2Fail$stVal", "ST"));

        var result = _service.Resolve(ied, device);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(2, result.Matches.Count);
        Assert.Empty(result.MissingPoints);
        Assert.Empty(result.AmbiguousPoints);
        Assert.All(result.Matches, match => Assert.True(match.UsedNormalizedIedPrefix));
    }

    [Fact]
    public void PartialLeafReference_RemainsAmbiguousWhenObjectOccursInTwoLogicalNodes()
    {
        var ied = FieldIed(FieldPoint("UCC-IEC-TCS1", ".TCS1Fail"));
        var device = FieldDevice(
            Signal("TCS from ADD", "AA1C1F13R4Application/ADDGGIO2$ST$TCS1Fail$stVal", "ST"),
            Signal("TCS from alternate LN", "AA1C1F13R4Application/ALTGGIO3$ST$TCS1Fail$stVal", "ST"));

        var result = _service.Resolve(ied, device);

        Assert.False(result.Succeeded);
        Assert.Empty(result.MissingPoints);
        Assert.Single(result.AmbiguousPoints);
    }

    [Fact]
    public void PartialLeafReference_DoesNotUseNearTextSimilarity()
    {
        var ied = FieldIed(FieldPoint("UCC-IEC-TCS1", ".TCS1Fail"));
        var device = FieldDevice(
            Signal("Different numbered failure", "AA1C1F13R4Application/ADDGGIO2$ST$TCS11Fail$stVal", "ST"),
            Signal("Healthy state", "AA1C1F13R4Application/ADDGGIO2$ST$TCS1Healthy$stVal", "ST"));

        var result = _service.Resolve(ied, device);

        Assert.False(result.Succeeded);
        Assert.Single(result.MissingPoints);
        Assert.Empty(result.AmbiguousPoints);
    }

    private static IoTestIedPlan Ied(params IoTestPointPlan[] points) => new()
    {
        IedName = "AA1C1F06R2",
        IpAddress = "192.168.81.53",
        IedRole = "Protection relay",
        TestPoints = points.ToList()
    };

    private static IoTestIedPlan FieldIed(params IoTestPointPlan[] points) => new()
    {
        IedName = "AA1C1F13R4",
        IpAddress = "192.168.81.17",
        IedRole = "BCU - 6MD85",
        TestPoints = points.ToList()
    };

    private static IoTestPointPlan Point(string id, string reference, string fc, string logicalNode) => new()
    {
        TestPointId = id,
        IedName = "AA1C1F06R2",
        IpAddress = "192.168.81.53",
        SignalName = id,
        ObjectReference = reference,
        LogicalNode = logicalNode,
        FunctionalConstraint = fc,
        ExpectedOnText = "Active",
        ExpectedOffText = "InActive",
        ImportReady = true,
        TestEnabled = true
    };

    private static IoTestPointPlan FieldPoint(string id, string reference) => new()
    {
        TestPointId = id,
        IedName = "AA1C1F13R4",
        IpAddress = "192.168.81.17",
        SignalName = id,
        ObjectReference = reference,
        LogicalDevice = "AA1C1F13R4Application",
        LogicalNode = "-",
        DataAttribute = "stVal",
        FunctionalConstraint = "ST",
        ExpectedOnText = "Trip",
        ExpectedOffText = "Normal",
        ImportReady = true,
        TestEnabled = true
    };

    private static Iec61850MonitorDevice Device(params SignalDefinition[] signals)
    {
        var device = new Iec61850MonitorDevice
        {
            Name = "AA1C1F06R2",
            SclIedName = "AA1C1F06R2",
            IpAddress = "192.168.81.53",
            Port = 102
        };
        device.Signals.AddRange(signals);
        return device;
    }

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

    private static SignalDefinition Signal(string name, string reference, string fc) => new()
    {
        Name = name,
        ObjectReference = reference,
        FunctionalConstraint = fc
    };
}
