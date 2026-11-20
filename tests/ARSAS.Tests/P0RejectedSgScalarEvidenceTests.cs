using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P0RejectedSgScalarEvidenceTests
{
    [Fact]
    public void RejectedDecodedIntegerSettingKeepsItsActualClrAndProjectionContext()
    {
        var input = new Iec61850ReadValue
        {
            Value = 0L, DisplayValue = "Unknown",
            ReadReference = "BCUGEF650/SGgndPIOC1$SG$InMagTyp$setVal",
            Projection = "typed-enum"
        };
        var reason = IedBrowserRejectedScalarEvidence.Explain(input,
            "IED returned an unresolved value; raw MMS type/evidence needs inspection.",
            "INT32", "SG", "last unrelated success");
        Assert.Contains("CLR=Int64", reason);
        Assert.Contains("display='Unknown'", reason);
        Assert.Contains("FC=SG", reason);
        Assert.Contains("modelType=INT32", reason);
        Assert.Contains("GVA/invoke raw not captured", reason);
        Assert.DoesNotContain("unrelated success", reason);
    }

    [Fact]
    public void NullReadUsesActualLastErrorInsteadOfPretendingTheIedReturnedAValue()
    {
        var reason = IedBrowserRejectedScalarEvidence.Explain(
            null, "empty", "INT32", "SG", "MMS AccessResult failure code 3");
        Assert.Equal("MMS AccessResult failure code 3", reason);
    }

    [Fact]
    public void UnboundedVendorDisplayCannotOverflowDiagnosticCsv()
    {
        var reason = IedBrowserRejectedScalarEvidence.Explain(
            new Iec61850ReadValue { DisplayValue = new string('A', 500) },
            "Rejected", "INT32", "SG", "");
        Assert.True(reason.Length < 350);
        Assert.DoesNotContain(new string('A', 100), reason);
    }
}
