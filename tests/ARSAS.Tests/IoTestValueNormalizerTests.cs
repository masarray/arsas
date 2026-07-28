using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoTestValueNormalizerTests
{
    [Theory]
    [InlineData("True", true)]
    [InlineData("1", true)]
    [InlineData("Active", true)]
    [InlineData("False", false)]
    [InlineData("0", false)]
    [InlineData("InActive", false)]
    public void Normalize_MapsBinaryAndImportedStateLabels(string raw, bool expected)
    {
        Assert.Equal(expected, IoTestValueNormalizer.Normalize(Point(), raw));
    }

    [Theory]
    [InlineData("Closed [10]", true)]
    [InlineData("Open [01]", false)]
    public void Normalize_MapsConventionalDoublePointRenderedStates(string raw, bool expected)
    {
        var point = Point("Closed", "Open");
        Assert.Equal(expected, IoTestValueNormalizer.Normalize(point, raw));
    }

    [Theory]
    [InlineData("Open [01]", true)]
    [InlineData("Closed [10]", false)]
    public void Normalize_ImportedDoublePointLabelsOverrideConventionalFallback(string raw, bool expected)
    {
        var point = Point("Open", "Closed");
        Assert.Equal(expected, IoTestValueNormalizer.Normalize(point, raw));
    }

    [Fact]
    public void Normalize_DoesNotGuessUnknownEngineeringText()
    {
        Assert.Null(IoTestValueNormalizer.Normalize(Point(), "Intermediate state"));
    }

    private static IoTestPointPlan Point(string onText = "Active", string offText = "InActive") => new()
    {
        TestPointId = "TP-001",
        IedName = "AA1C1F03R4",
        IpAddress = "192.168.81.70",
        SignalName = "CB closed",
        ObjectReference = "AA1C1F03R4ADD/GGIO6.CBClsd.stVal",
        FunctionalConstraint = "ST",
        ExpectedOnText = onText,
        ExpectedOffText = offText
    };
}
