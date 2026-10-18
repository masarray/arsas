using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P0XcbrParentLeafFieldParityTests
{
    private const string Prefix = "BCUGEF650/XCBR1.";

    private static LiveIedDataObjectModel Object(string name, params (string Leaf, string Fc, string Type)[] leaves)
        => new()
        {
            Reference = Prefix + name, Name = name, InferredCdc = "INS",
            Attributes = leaves.Select(x => new LiveIedDataAttributeModel
            {
                ObjectReference = Prefix + name,
                AttributePath = name + "." + x.Leaf,
                MmsReference = Prefix + name + "." + x.Leaf,
                FunctionalConstraint = x.Fc,
                SclBType = x.Type
            }).ToArray()
        };

    [Fact]
    public void Xcbr01_ActValStObservedHasOneUnambiguousParentReference()
    {
        var obj = Object("SumSwARs", ("actVal", "ST", "INT32U"),
            ("q", "ST", "Quality"), ("t", "ST", "Timestamp"),
            ("units.SIUnit", "CF", "Enum"),
            ("units.multiplier", "CF", "Enum"));
        Assert.True(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(obj,
            out var reference, out var fc));
        Assert.Equal(Prefix + "SumSwARs.actVal", reference);
        Assert.Equal("ST", fc);
        var row = IedBrowserStructuredDetails.DataObject(obj, "ST");
        Assert.Equal(reference, row.ValueReference);
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(association, reference, fc,
            "12", "", "", "MMS Read"));
        Assert.True(ledger.TryGet(row.ValueReference, row.FunctionalConstraint,
            out var observed));
        Assert.Equal("12", observed.Value);
        Assert.False(ledger.TryGet(row.ValueReference, "SP", out _));
    }

    [Fact]
    public void Xcbr02_SetMagSpProvidesObservedParentOnlyWhenNoStOrMxScalar()
    {
        var obj = Object("SumSwATmms", ("setMag", "SP", "FLOAT32"),
            ("units.SIUnit", "CF", "Enum"),
            ("units.multiplier", "CF", "Enum"),
            ("minVal", "CF", "FLOAT32"));
        Assert.True(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(obj,
            out var reference, out var fc));
        Assert.Equal(Prefix + "SumSwATmms.setMag", reference);
        Assert.Equal("SP", fc);
        var row = IedBrowserStructuredDetails.DataObject(obj, null);
        Assert.Equal(reference, row.ValueReference);
        Assert.Equal("SP", row.FunctionalConstraint);
        var ledger = new IedBrowserLiveValues();
        var a = ledger.BeginAssociation();
        ledger.PublishForAssociation(a, reference, fc, "0", "", "", "MMS Read");
        Assert.True(ledger.TryGet(row.ValueReference, row.FunctionalConstraint, out var observed));
        Assert.Equal("0", observed.Value);
    }

    [Fact]
    public void Xcbr03_AmbiguousScalarOrWrongFcIsNeverChildFirstFallback()
    {
        var ambiguous = Object("SumSwARs",
            ("stVal", "ST", "INT32U"), ("actVal", "ST", "INT32U"));
        Assert.False(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(ambiguous, out _, out _));
        var differentFc = Object("ThAlSwA",
            ("setMag", "SP", "FLOAT32"), ("actVal", "ST", "INT32U"));
        Assert.True(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(differentFc,
            out var exact, out var fc));
        Assert.Equal(Prefix + "ThAlSwA.actVal", exact);
        Assert.Equal("ST", fc);
        var noProcess = Object("CBOpCap", ("q", "ST", "Quality"),
            ("units.SIUnit", "CF", "Enum"));
        Assert.False(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(noProcess, out _, out _));
    }

    [Fact]
    public void Xcbr04_ActValAndSetMagUnitsNeedFreshExactCfPair()
    {
        var obj1 = Object("SumSwARs", ("actVal", "ST", "INT32U"),
            ("units.SIUnit", "CF", "Enum"), ("units.multiplier", "CF", "Enum"));
        var obj2 = Object("SumSwATmms", ("setMag", "SP", "FLOAT32"),
            ("units.SIUnit", "CF", "Enum"), ("units.multiplier", "CF", "Enum"));
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "BCUGEF650",
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "XCBR1", LnClass = "XCBR", DataObjects = [obj1, obj2]
                }]
            }]
        };
        var values = new IedBrowserLiveValues();
        var association = values.BeginAssociation();
        values.PublishForAssociation(association, Prefix + "SumSwARs.units.SIUnit",
            "CF", "70", "", "", "MMS Read");
        values.PublishForAssociation(association, Prefix + "SumSwATmms.units.SIUnit",
            "CF", "4", "", "", "MMS Read");
        var cf = new IedBrowserCfEvidencePresentation(values, model, null);
        Assert.False(cf.TryFormatVerifiedNumericStatusUnit(
            Prefix + "SumSwARs.actVal", "ST", out _, out _));
        values.PublishForAssociation(association, Prefix + "SumSwARs.units.multiplier",
            "CF", "3", "", "", "MMS Read");
        values.PublishForAssociation(association, Prefix + "SumSwATmms.units.multiplier",
            "CF", "0", "", "", "MMS Read");
        Assert.True(cf.TryFormatVerifiedNumericStatusUnit(
            Prefix + "SumSwARs.actVal", "ST", out var a, out _));
        Assert.Equal("kA²t", a);
        // The SCL canonical projection uses the exact DA as
        // ObjectReference; GVA can use the DO owner. Both are exact
        // declarations of the SAME uniquely indexed scalar, not siblings.
        var selfReferenced = Object("SumSwARs", ("actVal", "ST", "INT32U"),
            ("units.SIUnit", "CF", "Enum"), ("units.multiplier", "CF", "Enum"));
        var attrs = selfReferenced.Attributes.Select(x => new LiveIedDataAttributeModel
        {
            ObjectReference = x.MmsReference, MmsReference = x.MmsReference,
            AttributePath = x.AttributePath,
            FunctionalConstraint = x.FunctionalConstraint, SclBType = x.SclBType
        }).ToArray();
        var selfModel = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "XCBR1", LnClass = "XCBR",
                    DataObjects = [new LiveIedDataObjectModel
                    {
                        Name = "SumSwARs", Reference = Prefix + "SumSwARs",
                        Attributes = attrs
                    }]
                }]
            }]
        };
        var selfCf = new IedBrowserCfEvidencePresentation(values, selfModel, null);
        Assert.True(selfCf.TryFormatVerifiedNumericStatusUnit(
            Prefix + "SumSwARs.actVal", "ST", out var selfUnit, out _));
        Assert.Equal("kA²t", selfUnit);
        Assert.True(cf.TryFormatVerifiedNumericStatusUnit(
            Prefix + "SumSwATmms.setMag", "SP", out var s, out _));
        Assert.Equal("s", s);
        Assert.False(cf.TryFormatVerifiedNumericStatusUnit(
            Prefix + "SumSwATmms.setMag", "SG", out _, out _));
        values.BeginAssociation();
        Assert.False(cf.TryFormatVerifiedNumericStatusUnit(
            Prefix + "SumSwATmms.setMag", "SP", out _, out _));
    }
}
