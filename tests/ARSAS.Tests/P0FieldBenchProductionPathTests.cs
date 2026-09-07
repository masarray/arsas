using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class P0FieldBenchProductionPathTests
{
    [Theory]
    [InlineData("A", "phsA.cVal.mag.f", "A PhsA")]
    [InlineData("A", "phsB.cVal.mag.f", "A PhsB")]
    [InlineData("A", "phsC.cVal.mag.f", "A PhsC")]
    [InlineData("ThdA", "phsA.instMag.f", "ThdA PhsA")]
    [InlineData("ThdA", "phsB.instMag.f", "ThdA PhsB")]
    [InlineData("ThdA", "phsC.instMag.f", "ThdA PhsC")]
    public void FatFormatter_UsesDataAttributeWhenImportedReferenceIsCollapsed(
        string doName,
        string dataAttribute,
        string expected)
    {
        var point = NewPoint(doName, dataAttribute);

        Assert.Equal(expected, IoFatSignalDisplayNameFormatter.Format(point));
        Assert.Equal("IEDLD/MMXU1." + doName, point.ObjectReference);
        Assert.Equal(dataAttribute, point.DataAttribute);
    }

    [Fact]
    public void RcbRows_CanRemainSelectedIndependently()
    {
        var first = new RcbExportRow { Name = "BRCB01", Reference = "IEDLD/LLN0.BR.BRCB01" };
        var second = new RcbExportRow { Name = "BRCB02", Reference = "IEDLD/LLN0.BR.BRCB02" };
        var third = new RcbExportRow { Name = "URCB01", Reference = "IEDLD/LLN0.RP.URCB01" };

        first.IsSelected = true;
        second.IsSelected = true;
        third.IsSelected = true;

        Assert.True(first.IsSelected);
        Assert.True(second.IsSelected);
        Assert.True(third.IsSelected);
    }

    private static IoTestPointPlan NewPoint(string doName, string dataAttribute)
        => new()
        {
            TestPointId = Guid.NewGuid().ToString("N"),
            IedName = "IED",
            IpAddress = "192.0.2.1",
            SignalName = doName,
            ObjectReference = "IEDLD/MMXU1." + doName,
            FunctionalConstraint = "MX",
            ExpectedOnText = "1",
            ExpectedOffText = "0",
            DataObject = doName,
            DataAttribute = dataAttribute,
            ReportDisplayReference = "IEDLD/MMXU1." + doName
        };
}
