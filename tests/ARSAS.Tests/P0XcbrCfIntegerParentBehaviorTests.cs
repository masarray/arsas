using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P0XcbrCfIntegerParentBehaviorTests
{
    private static LiveIedDataAttributeModel A(string suffix, string fc = "CF") => new()
    {
        ObjectReference = "BCUGEF650/XCBR1.ExNumTr",
        AttributePath = "ExNumTr." + suffix,
        FunctionalConstraint = fc,
        MmsReference = "BCUGEF650/XCBR1$" + fc + "$ExNumTr$" +
            suffix.Replace('.', '$'),
        MmsType = suffix.EndsWith(".i", StringComparison.Ordinal)
            ? "integer" : "structure",
        SclBType = suffix.EndsWith(".i", StringComparison.Ordinal)
            ? "INT32" : "Struct"
    };

    [Theory]
    [InlineData("minVal")]
    [InlineData("maxVal")]
    [InlineData("stepSize")]
    public void XcbrConfigurationParentBindsToItsOnlyDeclaredIntegerLeaf(string field)
    {
        var rows = IedBrowserStructuredDetails.AttributeRows(
            [A(field), A(field + ".i")], "ExNumTr");
        var row = Assert.Single(rows);
        Assert.Equal(field + " [CF]", row.Name);
        Assert.Equal("CF", row.FunctionalConstraint);
        Assert.Equal("BCUGEF650/XCBR1.ExNumTr." + field + ".i",
            IedBrowserLiveValues.CanonicalReference(row.FallbackValueReference));
        // The separate parent value remains unobserved until the ledger
        // contains a real MMS Read for this exact model-proven child.
        Assert.Equal("Not read", row.Value);
    }

    [Fact]
    public void AmbiguousNumericChildrenCannotSilentlyChooseFirst()
    {
        var rows = IedBrowserStructuredDetails.AttributeRows(
            [A("minVal"), A("minVal.i"), A("minVal.f")], "ExNumTr");
        var row = Assert.Single(rows);
        Assert.Equal("", row.FallbackValueReference);
    }

    [Fact]
    public void SameNameButDifferentFcCannotBorrowIntegerValue()
    {
        var rows = IedBrowserStructuredDetails.AttributeRows(
            [A("minVal", "CF"), A("minVal.i", "SP")], "ExNumTr");
        var cf = Assert.Single(rows.Where(r => r.Name == "minVal [CF]"));
        Assert.Equal("", cf.FallbackValueReference);
    }
}
