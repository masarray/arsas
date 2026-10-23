using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class DynamicReportQualificationCandidateTests
{
    [Fact]
    public void ClassAFilter_KeepsOnlySelectedScalarPrimaryStMxPoints()
    {
        var signals = new[]
        {
            Signal("LD0/GGIO1.Ind1.stVal", "ST", "BOOLEAN", selected: true),
            Signal("LD0/MMXU1.Hz.instMag.f", "MX", "FLOAT32", selected: true),
            Signal("LD0/GGIO1.Ind1.q", "ST", "QUALITY", selected: true),
            Signal("LD0/GGIO1.Ind1.t", "ST", "Timestamp", selected: true),
            Signal("LD0/GGIO1.Vendor", "ST", "STRUCTURE", selected: true),
            Signal("LD0/CSWI1.Pos.Oper", "CO", "BOOLEAN", selected: true, control: true),
            Signal("LD0/GGIO1.Ind2.stVal", "ST", "BOOLEAN", selected: false),
            Signal("LD0/LLN0.Mod.stVal", "CF", "INT32", selected: true)
        };

        var selected = DynamicReportQualificationCommissioningService.SelectScalarClassASignals(signals);

        Assert.Equal(2, selected.Count);
        Assert.Contains(selected, signal => signal.ObjectReference == "LD0/GGIO1.Ind1.stVal");
        Assert.Contains(selected, signal => signal.ObjectReference == "LD0/MMXU1.Hz.instMag.f");
    }

    [Theory]
    [InlineData("BOOLEAN", true)]
    [InlineData("Bool", true)]
    [InlineData("INT32", true)]
    [InlineData("UINT16", true)]
    [InlineData("FLOAT32", true)]
    [InlineData("Double", true)]
    [InlineData("Enum", true)]
    [InlineData("STRUCTURE", false)]
    [InlineData("ArrayOfInt", false)]
    [InlineData("Quality", false)]
    [InlineData("Timestamp", false)]
    [InlineData("VisibleString", false)]
    [InlineData("", false)]
    public void ScalarTypePolicy_IsConservative(string dataType, bool expected)
        => Assert.Equal(expected, DynamicReportQualificationCommissioningService.IsKnownScalarType(dataType));

    [Theory]
    [InlineData("ST", true)]
    [InlineData("MX", true)]
    [InlineData("CO", false)]
    [InlineData("CF", false)]
    [InlineData("DC", false)]
    [InlineData("", false)]
    public void FunctionalConstraintPolicy_UsesOnlyProcessStMx(string fc, bool expected)
        => Assert.Equal(expected, DynamicReportQualificationCommissioningService.IsSafeProcessFunctionalConstraint(fc));

    [Theory]
    [InlineData("LD0/GGIO1.Ind1.stVal", true)]
    [InlineData("LD0/MMXU1.Hz.instMag.f", true)]
    [InlineData("LD0/GGIO1.Ind1.q", false)]
    [InlineData("LD0/GGIO1.Ind1.t", false)]
    [InlineData("LD0/GGIO1.Ind1.quality", false)]
    [InlineData("LD0/GGIO1.Ind1.timestamp", false)]
    public void PrimaryReferencePolicy_RejectsQualityAndTimestampLeaves(string reference, bool expected)
        => Assert.Equal(expected, DynamicReportQualificationCommissioningService.IsPrimaryProcessReference(reference));

    [Fact]
    public void DuplicateObjectReferences_AreCollapsedBeforeQualification()
    {
        var a = Signal("LD0/GGIO1.Ind1.stVal", "ST", "BOOLEAN", selected: true);
        var b = Signal("ld0/ggio1.ind1.stval", "ST", "BOOLEAN", selected: true);

        var selected = DynamicReportQualificationCommissioningService.SelectScalarClassASignals([a, b]);

        Assert.Single(selected);
    }

    private static SignalDefinition Signal(
        string reference,
        string fc,
        string dataType,
        bool selected,
        bool control = false)
        => new()
        {
            IsSelected = selected,
            IsControlSignal = control,
            ObjectReference = reference,
            FunctionalConstraint = fc,
            DataType = dataType
        };
}
