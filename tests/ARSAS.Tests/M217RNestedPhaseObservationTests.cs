using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M217RNestedPhaseObservationTests
{
    [Fact]
    public void PhaseParentCanPresentExactObservedCValWithoutInventingAnMmsParent()
    {
        var magnitude = "LD/MMXU1.A.phsA.cVal.mag.f";
        var angle = "LD/MMXU1.A.phsA.cVal.ang.f";
        var child = new IedBrowserDetailRow("Data", "cVal [MX]", "Not read")
        {
            FunctionalConstraint = "MX",
            VectorComponents = [new IedBrowserVectorComponent("", magnitude, angle, "MX")]
        };
        var parent = new IedBrowserDetailRow("Data", "phsA", "Not read")
        {
            IsDataObject = true,
            FunctionalConstraint = "MX",
            LoadChildren = () => [child]
        };
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        ledger.PublishForAssociation(ledger.Association,
            magnitude, "MX", "0", "", "", "MMS Read");
        ledger.PublishForAssociation(ledger.Association,
            angle, "MX", "0", "", "", "MMS Read");
        Assert.True(IedBrowserNestedObservedPresentation.TryCompose(
            parent, ledger, new Dictionary<string, string?>(), false, out var result));
        Assert.Contains("0", result.Text);
        Assert.Contains("∠", result.Text);
    }

    [Fact]
    public void NoObservedCValOrIncorrectPhaseFunctionalConstraintLeavesParentUnknown()
    {
        var child = new IedBrowserDetailRow("Data", "instCVal [MX]", "Not read")
        {
            FunctionalConstraint = "MX",
            VectorComponents = [new IedBrowserVectorComponent("",
                "LD/MMXU1.A.phsA.instCVal.mag.f",
                "LD/MMXU1.A.phsA.instCVal.ang.f", "MX")]
        };
        var parent = new IedBrowserDetailRow("Data", "phsA", "Not read")
        {
            IsDataObject = true,
            FunctionalConstraint = "MX",
            LoadChildren = () => [child]
        };
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        Assert.False(IedBrowserNestedObservedPresentation.TryCompose(parent,
            ledger, new Dictionary<string, string?>(), false, out _));
        Assert.False(IedBrowserNestedObservedPresentation.TryCompose(parent with
        { FunctionalConstraint = "ST" },
            ledger, new Dictionary<string, string?>(), false, out _));
    }
}
