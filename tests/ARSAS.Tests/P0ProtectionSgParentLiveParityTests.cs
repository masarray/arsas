using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>Actual BCUGE Protection SG process leaves, no SCL defaults as live data.</summary>
public sealed class P0ProtectionSgParentLiveParityTests
{
    private const string Prefix = "BCUGEF650/SGgndPIOC1.";
    private static LiveIedDataObjectModel Obj(string name,
        params (string Suffix, string Fc, string Type)[] leaves)
        => new()
        {
            Reference = Prefix + name, Name = name, InferredCdc = "ENG",
            Attributes = leaves.Select(x => new LiveIedDataAttributeModel
            {
                ObjectReference = Prefix + name,
                AttributePath = name + "." + x.Suffix,
                MmsReference = "BCUGEF650/SGgndPIOC1$" + x.Fc + "$" + name +
                    "$" + x.Suffix.Replace('.', '$'),
                FunctionalConstraint = x.Fc,
                SclBType = x.Type
            }).ToArray()
        };

    [Theory]
    [InlineData("InMagTyp", "setVal", "Enum", "0")]
    [InlineData("PIOCEna", "setVal", "BOOLEAN", "false")]
    [InlineData("OpDlTmms", "setVal", "INT32U", "0")]
    [InlineData("StrVal", "setMag", "FLOAT32", "0")]
    public void ProtectionSgParent_ReusesExactObservedSettingOnly(
        string name, string leaf, string type, string value)
    {
        var obj = Obj(name, (leaf, "SG", type),
            ("d", "DC", "VisibleString"), ("units.SIUnit", "CF", "Enum"),
            ("units.multiplier", "CF", "Enum"));
        Assert.True(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            obj, out var exact, out var fc));
        Assert.Equal(Prefix + name + "." + leaf, exact);
        Assert.Equal("SG", fc);
        var row = IedBrowserStructuredDetails.DataObject(obj, null);
        Assert.Equal(exact, row.ValueReference);
        Assert.Equal(fc, row.FunctionalConstraint);

        var ledger = new IedBrowserLiveValues();
        var a = ledger.BeginAssociation();
        Assert.False(ledger.TryGet(exact, fc, out _));
        Assert.True(ledger.PublishForAssociation(a, exact, fc,
            value, "", "", "MMS Read"));
        Assert.True(IedBrowserDetailEvidenceResolver.TryResolve(ledger,
            row, null, false, out var observed, out var actual));
        Assert.Equal(exact, actual);
        Assert.Equal(value, observed.Value);
    }

    [Fact]
    public void ProtectionSgParent_MixedOrEditingOnlyFailsClosed()
    {
        var mixed = Obj("PIOCEna", ("setVal", "SG", "BOOLEAN"),
            ("setMag", "SG", "FLOAT32"));
        Assert.False(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            mixed, out _, out _));
        var editOnly = Obj("InMagTyp", ("setVal", "SE", "Enum"));
        Assert.False(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            editOnly, out _, out _));
        var crossFc = Obj("StrVal", ("setMag", "SG", "FLOAT32"),
            ("setMag", "SP", "FLOAT32"));
        Assert.False(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            crossFc, out _, out _));
        var processWins = Obj("OpCnt", ("stVal", "ST", "INT32U"),
            ("setVal", "SG", "INT32U"));
        Assert.True(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            processWins, out var exact, out var fc));
        Assert.Equal(Prefix + "OpCnt.stVal", exact);
        Assert.Equal("ST", fc);
    }

    [Fact]
    public void ProtectionSgExactCfUnitIsNeverBorrowedForSettingEnum()
    {
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "BCUGEF650",
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "SGgndPIOC1", LnClass = "PIOC",
                    DataObjects = [
                        Obj("StrVal", ("setMag", "SG", "FLOAT32"),
                            ("units.SIUnit", "CF", "Enum"),
                            ("units.multiplier", "CF", "Enum")),
                        Obj("InMagTyp", ("setVal", "SG", "Enum"))]
                }]
            }]
        };
        var values = new IedBrowserLiveValues();
        var a = values.BeginAssociation();
        values.PublishForAssociation(a, Prefix + "StrVal.units.SIUnit", "CF",
            "5", "", "", "MMS Read");
        var cf = new IedBrowserCfEvidencePresentation(values, model, null);
        Assert.Equal("", IedBrowserEngineeringValuePresentation.ResolveUnit(
            cf.BuildVerifiedUnits([], model), Prefix + "StrVal.setMag", "SG"));
        values.PublishForAssociation(a, Prefix + "StrVal.units.multiplier", "CF",
            "0", "", "", "MMS Read");
        Assert.Equal("A", IedBrowserEngineeringValuePresentation.ResolveUnit(
            cf.BuildVerifiedUnits([], model), Prefix + "StrVal.setMag", "SG"));
        Assert.Equal("", IedBrowserEngineeringValuePresentation.ResolveUnit(
            cf.BuildVerifiedUnits([], model), Prefix + "InMagTyp.setVal", "SG"));
    }
}
