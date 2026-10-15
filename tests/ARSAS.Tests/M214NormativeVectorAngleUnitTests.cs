using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M214NormativeVectorAngleUnitTests
{
    [Theory]
    [InlineData("LD/MMXU1.PhV.phsA.cVal.ang.f")]
    [InlineData("LD/MMXU1.A.phsB.instCVal.ang.f")]
    public void IecVectorAngleWithoutUnitAttribute_UsesNormativeDegree(string angle)
    {
        var units = new Dictionary<string, string?>();
        Assert.Equal("°",
            IedBrowserEngineeringValuePresentation.ResolveVectorAngleUnit(
                units, angle, "MX"));
        Assert.Equal("°",
            IedBrowserEngineeringValuePresentation.ResolveVectorAngleUnit(
                units, angle, "mx"));
    }

    [Fact]
    public void AngleUnitNeverLeaksIntoScalarOrUnrelatedFields()
    {
        var empty = new Dictionary<string, string?>();
        Assert.Equal("", IedBrowserEngineeringValuePresentation.ResolveVectorAngleUnit(
            empty, "LD/MMXU1.Hz.mag.f", "MX"));
        Assert.Equal("", IedBrowserEngineeringValuePresentation.ResolveVectorAngleUnit(
            empty, "LD/X.myAngle.ang.f", "MX"));
        Assert.Equal("", IedBrowserEngineeringValuePresentation.ResolveVectorAngleUnit(
            empty, "LD/MMXU1.PhV.phsA.cVal.ang.f", "ST"));
    }

    [Fact]
    public void ExplicitVectorAngleUnitAndConflictsHavePriority()
    {
        const string field = "LD/MMXU1.PhV.phsA.cVal.ang.f";
        var index = new Dictionary<string, string?>
        {
            [field + "|MX"] = "rad"
        };
        Assert.Equal("rad", IedBrowserEngineeringValuePresentation.ResolveVectorAngleUnit(
            index, field, "MX"));
        index[field + "|MX"] = null; // conflicting unit declarations
        Assert.Equal("", IedBrowserEngineeringValuePresentation.ResolveVectorAngleUnit(
            index, field, "MX"));
    }
    [Fact]
    public void DegreeFormatterAttachesSymbolDirectlyToObservedNumericAngle()
    {
        Assert.Equal("33°", IedBrowserEngineeringValuePresentation.Format("33", "°"));
        Assert.Equal("0°", IedBrowserEngineeringValuePresentation.Format("0", "°"));
        Assert.Equal("33°", IedBrowserEngineeringValuePresentation.Format("33°", "°"));
        Assert.Equal("33 kV", IedBrowserEngineeringValuePresentation.Format("33", "kV"));
        Assert.Equal("good", IedBrowserEngineeringValuePresentation.Format("good", "°"));
    }

}
