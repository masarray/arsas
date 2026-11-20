using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Cross-vendor IEC model contract: SG setMag + setMag.f are one
/// structured-magnitude value with two declared paths, not two independent
/// process candidates. All Read/Report evidence remains canonical and exact.
/// </summary>
public sealed class P0ModelParentScalarAuthorityTests
{
    private static LiveIedDataAttributeModel Attr(string domain, string fc, string tail)
        => new()
        {
            ObjectReference = domain + "/ATCC1.LimVBndR1",
            AttributePath = "LimVBndR1." + tail,
            MmsReference = domain + "/ATCC1$" + fc + "$LimVBndR1$" +
                tail.Replace(".", "$", StringComparison.Ordinal),
            FunctionalConstraint = fc,
            MmsType = tail.EndsWith(".f", StringComparison.Ordinal)
                ? "floating-point" : "structure"
        };

    private static LiveIedDataObjectModel Obj(string domain,
        params LiveIedDataAttributeModel[] attributes)
        => new()
        {
            Name = "LimVBndR1",
            Reference = domain + "/ATCC1.LimVBndR1",
            InferredCdc = "ASG",
            Attributes = attributes
        };

    [Theory]
    [InlineData("ZIVAVRLD1", "105")]
    [InlineData("SIEMENSLD0", "110")]
    [InlineData("TESTIEDLD1", "0")]
    public void ExactSgStructuredMagnitudeReusesAlreadyReadScalarInDoParent(
        string domain, string observedValue)
    {
        var obj = Obj(domain,
            Attr(domain, "CF", "minVal.f"),
            Attr(domain, "SG", "setMag"),
            Attr(domain, "SG", "setMag.f"),
            Attr(domain, "SE", "setMag"),
            Attr(domain, "SE", "setMag.f"));
        Assert.True(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            obj, out var leaf, out var fc));
        Assert.Equal(domain + "/ATCC1.LimVBndR1.setMag.f", leaf);
        Assert.Equal("SG", fc);
        var row = IedBrowserStructuredDetails.DataObject(obj, null);
        Assert.Equal("SG", row.FunctionalConstraint);
        Assert.Equal(leaf, row.ValueReference);
        Assert.True(row.IsEngineeringValue);
        var ledger = new IedBrowserLiveValues();
        var a = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(
            a, leaf, "SG", observedValue, "", "", "MMS Read"));
        Assert.True(IedBrowserDetailEvidenceResolver.TryResolve(
            ledger, row, schema: null, monitoring: false,
            out var evidence, out var selected));
        Assert.Equal(leaf, selected);
        Assert.Equal(observedValue, evidence.Value);
        Assert.True(ledger.TryGet(leaf, "SG", out var child));
        Assert.Equal(child.Value, evidence.Value);
        Assert.False(ledger.TryGet(leaf, "SE", out _));
    }

    [Fact]
    public void ActiveSettingAliasUsesSingleCorrectFcEvenWhenDeclarationOrderReversed()
    {
        const string ld = "LD1";
        var obj = Obj(ld, Attr(ld, "SE", "setMag.f"),
            Attr(ld, "SG", "setMag.f"), Attr(ld, "SG", "setMag"));
        Assert.True(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(obj, out var refName, out var fc));
        Assert.Equal("LD1/ATCC1.LimVBndR1.setMag.f", refName);
        Assert.Equal("SG", fc);
    }

    [Fact]
    public void DifferentSettingValuesAndMixedSettingFcsFailClosed()
    {
        const string ld = "LD1";
        var twoIndependentValues = Obj(ld, Attr(ld, "SG", "setMag.f"),
            Attr(ld, "SG", "setVal"));
        var mixed = Obj(ld, Attr(ld, "SG", "setMag"),
            Attr(ld, "SP", "setMag.f"));
        Assert.False(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            twoIndependentValues, out _, out _));
        Assert.False(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            mixed, out _, out _));
        Assert.False(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            Obj(ld, Attr(ld, "SE", "setMag"), Attr(ld, "SE", "setMag.f")),
            out _, out _));
    }

    [Fact]
    public void ParentDoesNotBorrowForeignObjectOrDifferentFcOrUnknownVendorLeaf()
    {
        const string ld = "LD1";
        var parent = Obj(ld, Attr(ld, "SG", "vendorPrivateSetting"),
            Attr("DIFFERENTLD", "SG", "setMag.f"));
        Assert.False(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            parent, out _, out _));
        var valid = Obj(ld, Attr(ld, "SG", "setMag.f"));
        var row = IedBrowserStructuredDetails.DataObject(valid, null);
        var values = new IedBrowserLiveValues();
        var a = values.BeginAssociation();
        Assert.True(values.PublishForAssociation(a,
            "LD2/ATCC1.LimVBndR1.setMag.f", "SG", "95", "", "", "MMS Read"));
        Assert.True(values.PublishForAssociation(a,
            "LD1/ATCC1.LimVBndR1.setMag.f", "SE", "777", "", "", "MMS Read"));
        Assert.False(IedBrowserDetailEvidenceResolver.TryResolve(
            values, row, null, false, out _, out _));
    }

    [Fact]
    public void ProcessStLeafKeepsPrecedenceOverUnrelatedSettingEvidence()
    {
        const string ld = "LD1";
        var obj = Obj(ld,
            Attr(ld, "ST", "stVal"),
            Attr(ld, "SG", "setMag"), Attr(ld, "SG", "setMag.f"));
        Assert.True(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            obj, out var leaf, out var fc));
        Assert.Equal("ST", fc);
        Assert.EndsWith(".stVal", leaf, StringComparison.Ordinal);
    }

    [Fact]
    public void NonScalarParentShowsObservedChildrenWithoutInventingScalarOrOtherFc()
    {
        const string ld = "LD1";
        var obj = Obj(ld, Attr(ld, "CF", "units"),
            Attr(ld, "CF", "units.SIUnit"),
            Attr(ld, "CF", "units.multiplier"),
            Attr(ld, "SG", "vendorPrivate"),
            Attr(ld, "SE", "setMag.f"));
        var row = IedBrowserStructuredDetails.DataObject(obj, null);
        Assert.Equal("", row.FunctionalConstraint);
        var ledger = new IedBrowserLiveValues();
        var a = ledger.BeginAssociation();
        Assert.False(IedBrowserObservedParentPresentation.TrySummarize(
            row, ledger, false, out _, out _, out _));

        Assert.True(ledger.PublishForAssociation(a,
            "LD1/ATCC1.LimVBndR1.units.SIUnit", "CF", "5", "", "",
            "MMS Read"));
        Assert.True(IedBrowserObservedParentPresentation.TrySummarize(
            row, ledger, false, out var label, out var source, out var tooltip));
        Assert.Equal("Child values observed", label);
        Assert.Equal("Exact child evidence", source);
        Assert.Contains("units.SIUnit [CF]", tooltip);
        Assert.Contains("no invented parent value", tooltip);
        Assert.DoesNotContain("setMag.f [SE]", tooltip);

        ledger.BeginAssociation();
        Assert.False(IedBrowserObservedParentPresentation.TrySummarize(
            row, ledger, false, out _, out _, out _));
    }

    [Fact]
    public void ParentChildCandidatePlanIsBoundedAndRejectsWrongObjectOrSeOnly()
    {
        const string ld = "LD1";
        var many = Enumerable.Range(0, 500)
            .Select(i => Attr(ld, "CF", "vendor" + i)).ToArray();
        var obj = Obj(ld, many);
        var plan = IedBrowserObservedParentPresentation.BuildCandidates(obj);
        Assert.Equal(24, plan.Count);
        Assert.All(plan, p => Assert.StartsWith("LD1/ATCC1.LimVBndR1.", p.Reference));

        var row = IedBrowserStructuredDetails.DataObject(
            Obj(ld, Attr(ld, "SE", "setMag.f"), Attr(ld, "CF", "vendor")),
            null);
        var ledger = new IedBrowserLiveValues();
        var a = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(
            a, "LD1/ATCC1.LimVBndR1.setMag.f", "SE",
            "999", "", "", "MMS Read"));
        Assert.True(ledger.PublishForAssociation(
            a, "LD2/ATCC1.LimVBndR1.vendor", "CF",
            "777", "", "", "MMS Read"));
        Assert.False(IedBrowserObservedParentPresentation.TrySummarize(
            row, ledger, false, out _, out _, out _));
    }
}
