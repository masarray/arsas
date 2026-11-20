using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>BCUGE PTOC SP protection values from read-only MMS, not invented from SCL.</summary>
public sealed class P0ProtectionSpParentAndFocusedReadTests
{
    private const string Ln = "BCUGEF650/gndPTOC2";

    private static LiveIedDataObjectModel Obj(string name,
        params (string Field, string Fc, string BType)[] fields)
        => new()
        {
            Name = name, Reference = Ln + "." + name,
            Attributes = fields.Select(f => new LiveIedDataAttributeModel
            {
                ObjectReference = Ln + "." + name,
                AttributePath = name + "." + f.Field,
                MmsReference = Ln + "$" + f.Fc + "$" + name + "$" +
                    f.Field.Replace('.', '$'),
                FunctionalConstraint = f.Fc, SclBType = f.BType,
                TypeDiscoveryStatus = "Exact",
                TypeSource = "GetVariableAccessAttributesLogicalNodeTree"
            }).ToArray()
        };

    [Theory]
    [InlineData("PTOCEna", "setVal", "BOOLEAN", "false")]
    [InlineData("InMagTyp", "setVal", "Enum", "0")]
    [InlineData("RsMod", "setVal", "Enum", "0")]
    [InlineData("TmACrv", "setCharact", "Enum", "0")]
    [InlineData("StrVal", "setMag", "FLOAT32", "0")]
    [InlineData("TmMult", "setMag", "FLOAT32", "0")]
    public void ExactPTOCSpScalarIsParentValueWithoutPromotingDcOrCf(
        string name, string field, string type, string value)
    {
        var obj = Obj(name, (field, "SP", type), ("d", "DC", "VisString255"),
            ("units.SIUnit", "CF", "Enum"),
            ("minVal", "CF", "FLOAT32"));
        Assert.True(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            obj, out var leaf, out var fc));
        Assert.Equal(Ln + "." + name + "." + field, leaf);
        Assert.Equal("SP", fc);

        var parent = IedBrowserStructuredDetails.DataObject(obj, null);
        Assert.Equal(leaf, parent.ValueReference);
        Assert.Equal(fc, parent.FunctionalConstraint);
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        Assert.False(IedBrowserDetailEvidenceResolver.TryResolve(
            ledger, parent, null, false, out _, out _));
        Assert.True(ledger.PublishForAssociation(association, leaf,
            fc, value, "", "", "MMS Read"));
        Assert.True(IedBrowserDetailEvidenceResolver.TryResolve(
            ledger, parent, null, false, out var observed, out var exact));
        Assert.Equal(value, observed.Value);
        Assert.Equal(leaf, exact);
    }

    [Fact]
    public void SetCharactIsEnumNotASettingMagnitudeEvenWithAncestorUnits()
    {
        var units = new Dictionary<string, string?>(
            StringComparer.Ordinal)
        {
            [Ln + ".TmACrv|SP"] = "s",
            [Ln + ".TmACrv.setCharact|SP"] = null
        };
        Assert.Equal(IedBrowserEngineeringRole.StatusEnum,
            IedBrowserEngineeringValuePresentation.Classify(
                Ln + ".TmACrv.setCharact", "SP"));
        Assert.Equal("", IedBrowserEngineeringValuePresentation.ResolveUnit(
            units, Ln + ".TmACrv.setCharact", "SP"));
        Assert.Equal(IedBrowserEngineeringRole.SettingMagnitude,
            IedBrowserEngineeringValuePresentation.Classify(
                Ln + ".TmMult.setMag", "SP"));
    }

    [Fact]
    public void UniqueSpSettingRefusesEditedOrCompetingCandidates()
    {
        Assert.False(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            Obj("PTOCEna", ("setVal", "SE", "BOOLEAN")), out _, out _));
        Assert.False(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            Obj("InMagTyp", ("setVal", "SP", "Enum"),
                ("setVal", "SG", "Enum")), out _, out _));
        Assert.False(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            Obj("TmACrv", ("setCharact", "SP", "Enum"),
                ("setMag", "SP", "FLOAT32")), out _, out _));
        Assert.True(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            Obj("Mod", ("stVal", "ST", "Enum"),
                ("setVal", "SP", "Enum")), out var leaf, out var fc));
        Assert.Equal(Ln + ".Mod.stVal", leaf);
        Assert.Equal("ST", fc);
    }

    [Fact]
    public void OneShotFocusedPTOCReadPrefersStrGeneralBeforeUnrelatedCf()
    {
        var obj = Obj("Str", ("general", "ST", "BOOLEAN"),
            ("neut", "ST", "BOOLEAN"), ("dirGeneral", "ST", "Enum"),
            ("q", "ST", "Quality"), ("t", "ST", "Timestamp"),
            ("units.SIUnit", "CF", "Enum"),
            ("units.multiplier", "CF", "Enum"), ("d", "DC", "VisString255"));
        var ln = new LiveIedLogicalNodeModel
        {
            Name = "gndPTOC2", LnClass = "PTOC", DataObjects = [obj]
        };
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            { MmsDomain = "BCUGEF650", LogicalNodes = [ln] }]
        };
        var page = new IedBrowserTreeRow(
            "model/ln/BCUGEF650/gndPTOC2", "gndPTOC2", "LN", 2, ln);
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        var planned = IedBrowserReadPlan.BuildFocused(
            model, page, ledger, budget: 1, selectedDoReference: obj.Reference);
        Assert.Single(planned);
        Assert.Equal("ST", planned[0].FunctionalConstraint);
        Assert.Equal(Ln + ".Str.general",
            IedBrowserLiveValues.CanonicalReference(planned[0].Reference));
    }

    [Fact]
    public void OneShotFocusedPTOCReadAdmitsScalarBeforeHeavyCfAndDc()
    {
        var obj = Obj("StrVal", ("setMag", "SP", "FLOAT32"),
            ("units.SIUnit", "CF", "Enum"),
            ("units.multiplier", "CF", "Enum"),
            ("minVal", "CF", "FLOAT32"), ("maxVal", "CF", "FLOAT32"),
            ("stepSize", "CF", "FLOAT32"), ("d", "DC", "VisString255"),
            ("dataNs", "EX", "VisString255"));
        var ln = new LiveIedLogicalNodeModel
        {
            Name = "gndPTOC2", LnClass = "PTOC", DataObjects = [obj]
        };
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            { MmsDomain = "BCUGEF650", LogicalNodes = [ln] }]
        };
        var page = new IedBrowserTreeRow(
            "model/ln/BCUGEF650/gndPTOC2", "gndPTOC2", "LN", 2, ln);
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        var planned = IedBrowserReadPlan.BuildFocused(
            model, page, ledger, budget: 1, selectedDoReference: obj.Reference);
        Assert.Single(planned);
        Assert.Equal("SP", planned[0].FunctionalConstraint);
        Assert.Equal(Ln + ".StrVal.setMag",
            IedBrowserLiveValues.CanonicalReference(planned[0].Reference));
        Assert.DoesNotContain(planned, x => x.FunctionalConstraint == "SE");
    }
}
