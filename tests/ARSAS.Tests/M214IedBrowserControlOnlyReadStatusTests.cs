using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M214IedBrowserControlOnlyReadStatusTests
{
    [Fact]
    public void CommandParameters_AreNotDisplayedAsMissingMmsReads()
    {
        const string objectRef = "LD/CSWI1.Pos";
        var attributes = new[]
        {
            new LiveIedDataAttributeModel
            {
                ObjectReference = objectRef,
                AttributePath = "Pos.Oper.ctlVal",
                MmsReference = "LD/CSWI1$CO$Pos$Oper$ctlVal",
                FunctionalConstraint = "CO"
            },
            new LiveIedDataAttributeModel
            {
                ObjectReference = objectRef,
                AttributePath = "Pos.stVal",
                MmsReference = "LD/CSWI1$ST$Pos$stVal",
                FunctionalConstraint = "ST"
            }
        };
        var roots = IedBrowserStructuredDetails.AttributeRows(attributes, "Pos");
        var control = Assert.Single(roots, row =>
            row.Name == "Oper [CO]");
        Assert.Equal("Control only", control.Value);
        Assert.True(control.HasChildren);
        Assert.Equal("Control only", Assert.Single(control.LoadChildren!()).Value);
        var status = Assert.Single(roots, row => row.Name == "stVal [ST]");
        Assert.Equal("Not read", status.Value);
    }

    [Theory]
    [InlineData("CO", "Control only")]
    [InlineData("co", "Control only")]
    [InlineData("ST", "Not read")]
    [InlineData("SG", "Not read")]
    [InlineData("CF", "Not read")]
    [InlineData("", "Not read")]
    public void UnobservedLabel_HasExactFcSemantics(string fc, string expected)
        => Assert.Equal(expected, IedBrowserReadPlan.UnobservedValue(fc));
}
