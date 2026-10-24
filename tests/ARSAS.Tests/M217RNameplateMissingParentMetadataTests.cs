using ArIED61850Tester.Models;
using AR.Iec61850.Discovery;

namespace ARSAS.Tests;

public sealed class M217RNameplateMissingParentMetadataTests
{
    [Fact]
    public void ExactObservedVendorProjectsToNamPltWithoutAParentScalar()
    {
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        var reference = "BCUGEF650/CSWI1.NamPlt.vendor";
        Assert.True(ledger.PublishForAssociation(ledger.Association,
            reference, "DC", "GE Multilin", "", "", "MMS Read"));
        var row = new IedBrowserDetailRow("Data", "NamPlt", "Not read")
        {
            IsDataObject = true,
            ReadObjectReference = "BCUGEF650/CSWI1.NamPlt"
        };
        var engineering = new IedBrowserCfEvidencePresentation(
            ledger, new LiveIedModelDiscoveryDocument(), null);
        Assert.True(IedBrowserNameplatePresentation.TryProject(
            row, ledger, engineering, out var result));
        Assert.Equal("GE Multilin", result.Value);
        Assert.Contains("MMS Read", result.Source);
    }

    [Fact]
    public void NamPltNeverCopiesOtherObjectOrFunctionalConstraint()
    {
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        ledger.PublishForAssociation(ledger.Association,
            "BCUGEF650/CSWI2.NamPlt.vendor", "DC", "OTHER", "", "", "MMS Read");
        ledger.PublishForAssociation(ledger.Association,
            "BCUGEF650/CSWI1.NamPlt.vendor", "CF", "WRONG", "", "", "MMS Read");
        var engineering = new IedBrowserCfEvidencePresentation(
            ledger, new LiveIedModelDiscoveryDocument(), null);
        var row = new IedBrowserDetailRow("Data", "NamPlt", "Not read")
        {
            IsDataObject = true,
            ReadObjectReference = "BCUGEF650/CSWI1.NamPlt"
        };
        Assert.False(IedBrowserNameplatePresentation.TryProject(
            row, ledger, engineering, out _));
    }
}
