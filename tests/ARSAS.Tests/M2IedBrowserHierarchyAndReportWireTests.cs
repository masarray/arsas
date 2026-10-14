using AR.Iec61850.Asn1;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;
using ArIED61850Tester.Protocol.Mms;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>Physical field-data shape regression: DO→DA→BDA and original q/t wire evidence.</summary>
public sealed class M2IedBrowserHierarchyAndReportWireTests
{
    private static MmsDataValue WireUtc(byte quality = 0x3F)
    {
        byte[] raw = [0, 0, 0, 1, 0, 0, 0, quality];
        var encoded = BerWriter.EncodeTlv(
            BerWriter.EncodeIdentifier(BerClass.ContextSpecific, false, 17), raw);
        return Assert.Single(MmsDataCodec.DecodeAllData(encoded));
    }

    private static MmsReportFrame Frame(string reference, MmsDataValue payload)
        => new()
        {
            Values =
            [
                new MmsReportValue
                {
                    Member = new MmsDataSetDirectoryMember
                    {
                        UserReference = reference, FunctionalConstraint = "ST"
                    },
                    Value = payload
                }
            ]
        };

    [Fact]
    public void DirectReportedQualityBitstring_DecodesAllFlags_NotOnlyValidity()
    {
        var raw = MmsDataValue.BitString(3, [0, 0]);
        var observed = Assert.Single(IedBrowserReportWireEvidence.Extract(
            Frame("LD/GGIO6.CBPond.q", raw)));
        Assert.Equal("LD/GGIO6.CBPond.q", observed.CarrierReference);
        Assert.Equal("Good", observed.QualityBits?.Validity);
        Assert.Equal("false", observed.QualityBits?.FormatField("Overflow"));
        Assert.Equal("false", observed.QualityBits?.FormatField("OperatorBlocked"));
        Assert.Null(observed.TimeBits);
    }

    [Fact]
    public void ReportedBooleanDoStructure_ExtractsDirectTypedQAndRawUtcT()
    {
        var value = MmsDataValue.Structure(
            [MmsDataValue.Boolean(false), MmsDataValue.BitString(3, [0, 0]), WireUtc()]);
        var observed = Assert.Single(IedBrowserReportWireEvidence.Extract(
            Frame("LD/GGIO6.CBPond", value)));
        Assert.Equal("Good", observed.QualityBits?.Validity);
        Assert.Equal("process", observed.QualityBits?.Source);
        Assert.Equal("false", observed.QualityBits?.FormatField("Test"));
        Assert.True(observed.TimeBits?.HasWireProvenance);
        Assert.Equal("true", IedBrowserUtcTimeQualityPresentation.FormatField(
            observed.TimeBits, "ClockNotSynchronized"));
        Assert.Equal("unspecified", IedBrowserUtcTimeQualityPresentation.FormatField(
            observed.TimeBits, "TimeAccuracy"));
    }

    [Fact]
    public void MultipleOrNestedQualityCandidates_DoNotGuessCompanion()
    {
        var multiple = MmsDataValue.Structure(
            [MmsDataValue.BitString(3, [0, 0]),
             MmsDataValue.BitString(3, [0, 0]), WireUtc()]);
        var reported = Assert.Single(IedBrowserReportWireEvidence.Extract(
            Frame("LD/CSWI1.Pos", multiple)));
        Assert.Null(reported.QualityBits);
        Assert.NotNull(reported.TimeBits);
        var nested = MmsDataValue.Structure(
            [MmsDataValue.Structure([MmsDataValue.BitString(3, [0, 0])]),
             MmsDataValue.Boolean(false)]);
        Assert.Empty(IedBrowserReportWireEvidence.Extract(
            Frame("LD/CSWI1.Pos", nested)));
        Assert.Empty(IedBrowserReportWireEvidence.Extract(
            Frame("LD/CSWI1.Pos", MmsDataValue.Boolean(false))));
    }

    [Fact]
    public void SyntheticUtcTime_AndWrongWidthQualityNeverClaimRawEvidence()
    {
        var synthetic = MmsDataValue.UtcTime(
            Iec61850UtcTime.FromBytes([0, 0, 0, 1, 0, 0, 0, 0x3F]));
        Assert.Empty(IedBrowserReportWireEvidence.Extract(
            Frame("LD/CSWI1.Pos.t", synthetic)));
        Assert.Empty(IedBrowserReportWireEvidence.Extract(
            Frame("LD/CSWI1.Pos.q", MmsDataValue.BitString(0, [0, 0]))));
    }

    [Fact]
    public void ReportRegistry_WireQualityAndTimePopulateIndividualChildren_WhenAssociated()
    {
        var observed = Assert.Single(IedBrowserReportWireEvidence.Extract(
            Frame("LD/GGIO6.CBPond", MmsDataValue.Structure(
                [MmsDataValue.Boolean(false), MmsDataValue.BitString(3, [0, 0]), WireUtc()]))));
        var live = new IedBrowserLiveValues();
        var association = live.BeginAssociation();
        Assert.True(live.PublishForAssociation(association, "LD/GGIO6.CBPond.q",
            "ST", observed.QualityBits!.Summary, "", "", "Report quality · raw MMS",
            qualityEvidence: observed.QualityBits));
        Assert.True(live.PublishForAssociation(association, "LD/GGIO6.CBPond.t",
            "ST", observed.TimeBits!.EngineeringUtc, "", "", "Report timestamp · raw MMS",
            utcTimeEvidence: observed.TimeBits));
        Assert.True(live.TryGet("LD/GGIO6.CBPond.q", "ST", out var q));
        Assert.Equal("false", q.QualityBits?.FormatField("OutOfRange"));
        Assert.Equal("process", q.QualityBits?.FormatField("Source"));
        Assert.True(live.TryGet("LD/GGIO6.CBPond.t", "ST", out var t));
        Assert.Equal("true", IedBrowserUtcTimeQualityPresentation.FormatField(
            t.TimeBits, "ClockNotSynchronized"));
        live.BeginAssociation();
        Assert.False(live.PublishForAssociation(association, "LD/GGIO6.CBPond.q",
            "ST", "Good", "", "", "Report quality", qualityEvidence: observed.QualityBits));
        Assert.False(live.TryGet("LD/GGIO6.CBPond.q", "ST", out _));
    }

    [Fact]
    public void CoalescedUiReportMirror_CannotEraseTypedRawWireQualityOrTimestamp()
    {
        var raw = Assert.Single(IedBrowserReportWireEvidence.Extract(
            Frame("LD/GGIO6.CBPond", MmsDataValue.Structure(
                [MmsDataValue.Boolean(false),
                 MmsDataValue.BitString(3, [0, 0]), WireUtc(0x3F)]))));
        var live = new IedBrowserLiveValues();
        var session = live.BeginAssociation();
        Assert.True(live.PublishForAssociation(session, "LD/GGIO6.CBPond.q",
            "ST", raw.QualityBits!.Summary, "", "", "Report quality · raw MMS",
            qualityEvidence: raw.QualityBits));
        Assert.True(live.PublishForAssociation(session, "LD/GGIO6.CBPond.t",
            "ST", raw.TimeBits!.EngineeringUtc, "", "", "Report timestamp · raw MMS",
            utcTimeEvidence: raw.TimeBits));
        var mirroredPoint = new Iec61850MonitorPoint
        {
            IecReference = "LD/GGIO6.CBPond.stVal",
            QualityReference = "LD/GGIO6.CBPond.q",
            TimestampReference = "LD/GGIO6.CBPond.t",
            FunctionalConstraint = "ST",
            Value = "False", Quality = "good",
            DeviceTimestamp = "2026-10-14 10:00:00",
            SourceMode = "StaticBRCB",
            IsOffline = false
        };
        live.PublishReport(mirroredPoint, observedReportIngress: true);
        Assert.True(live.TryGet("LD/GGIO6.CBPond.q", "ST", out var q));
        Assert.Equal("Report quality · raw MMS", q.Source);
        Assert.Equal("false", q.QualityBits?.FormatField("OperatorBlocked"));
        Assert.True(live.TryGet("LD/GGIO6.CBPond.t", "ST", out var t));
        Assert.Equal("Report timestamp · raw MMS", t.Source);
        Assert.Equal("true", IedBrowserUtcTimeQualityPresentation.FormatField(
            t.TimeBits, "ClockNotSynchronized"));
        Assert.True(live.TryGet("LD/GGIO6.CBPond.stVal", "ST", out var stVal));
        Assert.Equal("False", stVal.Value);
        Assert.Equal("Report", stVal.Source);
    }

    private static LiveIedDataObjectModel ModelDo()
    {
        LiveIedDataAttributeModel Attr(string path, string fc = "ST") => new()
        {
            ObjectReference = "LD/CSWI1.Pos", AttributePath = "Pos." + path,
            MmsReference = "LD/CSWI1$" + fc + "$Pos$" + path.Replace('.', '$'),
            FunctionalConstraint = fc
        };
        return new LiveIedDataObjectModel
        {
            Name = "Pos", Reference = "LD/CSWI1.Pos",
            Attributes = [Attr("stVal"), Attr("q"), Attr("t"),
                Attr("origin.orCat"), Attr("origin.orIdent"),
                Attr("SBOw.ctlVal", "CO"), Attr("SBOw.origin.orCat", "CO"),
                Attr("SBOw.origin.orIdent", "CO")]
        };
    }

    [Fact]
    public void IdenticalNestedAttributePathsInDifferentFc_StayInSeparateBranches()
    {
        LiveIedDataAttributeModel Attribute(string fc, string suffix) => new()
        {
            ObjectReference = "LD/CSWI1.Pos",
            AttributePath = "Pos.origin." + suffix,
            MmsReference = "LD/CSWI1$" + fc + "$Pos$origin$" + suffix,
            FunctionalConstraint = fc
        };

        // Adversarial model: an identical intermediate DA path is declared
        // in two FCs. A name-only grouping merges both branches and loses FC.
        var roots = IedBrowserStructuredDetails.AttributeRows(
            [Attribute("ST", "orCat"), Attribute("CO", "orIdent")], "Pos");
        Assert.Equal(["origin [ST]", "origin [CO]"],
            roots.Select(row => row.Name).ToArray());
        Assert.Equal(["ST", "CO"],
            roots.Select(row => row.FunctionalConstraint).ToArray());

        var visible = new IedBrowserDetailProjection();
        visible.Reset(roots);
        visible.Toggle(roots[0]);
        var st = Assert.Single(visible.Rows.Where(row => row.Name == "orCat [ST]"));
        Assert.Equal("LD/CSWI1$ST$Pos$origin$orCat", st.ValueReference);
        Assert.DoesNotContain(visible.Rows, row => row.Name == "orIdent [CO]");

        visible.Toggle(roots[1]);
        var co = Assert.Single(visible.Rows.Where(row => row.Name == "orIdent [CO]"));
        Assert.Equal("LD/CSWI1$CO$Pos$origin$orIdent", co.ValueReference);
        Assert.Equal("CO", co.FunctionalConstraint);
    }

    [Fact]
    public void DataModel_DoValueUsesOnlyUniqueExactObservedProcessLeaf()
    {
        var obj = ModelDo();
        var projected = IedBrowserStructuredDetails.DataObject(obj, "ST");
        Assert.Equal("LD/CSWI1.Pos.stVal", projected.ValueReference);
        Assert.Equal("ST", projected.FunctionalConstraint);

        var values = new IedBrowserLiveValues();
        var session = values.BeginAssociation();
        Assert.True(values.PublishForAssociation(session, "LD/CSWI1$ST$Pos$stVal",
            "ST", "Open [01]", "", "", "Report"));
        Assert.True(values.TryGet(projected.ValueReference,
            projected.FunctionalConstraint, out var observed));
        Assert.Equal("Open [01]", observed.Value);

        var sibling = new LiveIedDataObjectModel
        {
            Name = obj.Name, Reference = obj.Reference,
            Attributes = obj.Attributes.Concat([
                new LiveIedDataAttributeModel
                {
                    ObjectReference = obj.Reference, AttributePath = "Pos.posVal",
                    MmsReference = "LD/CSWI1$ST$Pos$posVal",
                    FunctionalConstraint = "ST"
                }
            ]).ToArray()
        };
        var ambiguous = IedBrowserStructuredDetails.DataObject(sibling, "ST");
        Assert.Equal(obj.Reference, ambiguous.ValueReference);
        Assert.False(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            sibling, out _, out _));
        Assert.False(values.TryGet(ambiguous.ValueReference,
            ambiguous.FunctionalConstraint, out _));
    }

    [Fact]
    public void AnalogDataSetFcdAndSingleLeafParentsBindToActualReportedMmsValue()
    {
        const string owner = "LD/MMXU1.PPV.phsCA";
        LiveIedDataAttributeModel Attr(string path) => new()
        {
            ObjectReference = owner,
            AttributePath = "phsCA." + path,
            FunctionalConstraint = "MX",
            MmsReference = "LD/MMXU1$MX$PPV$phsCA$" + path.Replace('.', '$')
        };
        var obj = new LiveIedDataObjectModel
        {
            Name = "phsCA", Reference = owner,
            Attributes = [Attr("cVal.mag.f"), Attr("q"), Attr("t")]
        };
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                LogicalNodes = [new LiveIedLogicalNodeModel { DataObjects = [obj] }]
            }]
        };
        var member = new LiveIedDataSetMemberModel
        {
            Reference = "LD/MMXU1$MX$PPV$phsCA", FunctionalConstraint = "MX"
        };
        var root = IedBrowserStructuredDetails.Member(member, model);
        Assert.Equal(owner + ".cVal.mag.f", root.ValueReference);

        var rows = new IedBrowserDetailProjection();
        rows.Reset(IedBrowserStructuredDetails.DataSetMemberChildren(member, model, 1));
        var cval = Assert.Single(rows.Rows.Where(r => r.Name == "cVal [MX]"));
        Assert.Equal(root.ValueReference, cval.ValueReference);
        rows.Toggle(cval);
        var mag = Assert.Single(rows.Rows.Where(r => r.Name == "mag [MX]"));
        Assert.Equal(root.ValueReference, mag.ValueReference);
        rows.Toggle(mag);
        var f = Assert.Single(rows.Rows.Where(r => r.Name == "f [MX]"));
        var live = new IedBrowserLiveValues();
        var session = live.BeginAssociation();
        Assert.True(live.PublishForAssociation(session, "LD/MMXU1$MX$PPV$phsCA$cVal$mag$f",
            "MX", "0", "", "", "Report"));
        foreach (var row in new[] { root, cval, mag, f })
        {
            Assert.True(live.TryGet(row.ValueReference, row.FunctionalConstraint, out var evidence));
            Assert.Equal("0", evidence.Value);
        }
        var ambiguous = IedBrowserStructuredDetails.AttributeRows(
            [Attr("cVal.mag.f"), Attr("cVal.ang.f")], "phsCA");
        Assert.Equal(owner + ".cVal", ambiguous[0].ValueReference);
    }

    [Theory]
    [InlineData("TotW", "mag.f", "instMag.f")]
    [InlineData("phsA", "cVal.mag.f", "instCVal.mag.f")]
    public void MeasuredValueNormalAndInstantaneousPair_ResolveExactParentWithoutFabrication(
        string doName, string normalRelative, string instantRelative)
    {
        var owner = "LD/MMXU1." + doName;
        LiveIedDataAttributeModel Attr(string relative) => new()
        {
            ObjectReference = owner,
            AttributePath = doName + "." + relative,
            FunctionalConstraint = "MX",
            MmsReference = "LD/MMXU1$MX$" + doName + "$" +
                relative.Replace('.', '$')
        };
        var obj = new LiveIedDataObjectModel
        {
            Name = doName, Reference = owner,
            Attributes = [Attr(instantRelative), Attr(normalRelative),
                Attr("q"), Attr("t")]
        };
        var normal = owner + "." + normalRelative;
        var instant = owner + "." + instantRelative;
        Assert.True(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            obj, out var selected, out var fc));
        Assert.Equal(normal, selected);
        Assert.Equal("MX", fc);
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                LogicalNodes = [new LiveIedLogicalNodeModel { DataObjects = [obj] }]
            }]
        };
        var schema = new IedBrowserSchemaIndex(model);
        Assert.True(schema.TryGetPrimaryLeaf(owner, "MX", out var primary));
        Assert.Equal(normal, primary);
        var dataSetRow = IedBrowserStructuredDetails.Member(
            new LiveIedDataSetMemberModel
            {
                Reference = owner, FunctionalConstraint = "MX"
            }, model);
        var modelRow = IedBrowserStructuredDetails.DataObject(obj, "MX");
        Assert.Equal(normal, dataSetRow.ValueReference);
        Assert.Equal(normal, modelRow.ValueReference);

        var live = new IedBrowserLiveValues();
        var association = live.BeginAssociation();
        Assert.True(live.PublishForAssociation(association, instant,
            "MX", "99", "", "", "MMS Read"));
        // Parent never borrows the instantaneous value before its normal
        // IEC process magnitude has real observation evidence.
        Assert.False(live.TryGet(owner, "MX", out _,
            schema: schema));
        Assert.True(live.PublishForAssociation(association, normal,
            "MX", "0", "", "", "Report"));
        Assert.True(live.TryGet(owner, "MX", out var observed,
            schema: schema));
        Assert.Equal("0", observed.Value);
        Assert.Equal("Report", observed.Source);
        Assert.True(live.TryGet(instant, "MX", out var instantObserved));
        Assert.Equal("99", instantObserved.Value);
    }

    [Fact]
    public void UnrelatedProcessLeavesAndCrossFcCandidates_DoNotSelectArbitraryValue()
    {
        const string owner = "LD/CSWI1.Pos";
        LiveIedDataAttributeModel Attr(string fc, string leaf) => new()
        {
            ObjectReference = owner, FunctionalConstraint = fc,
            AttributePath = "Pos." + leaf,
            MmsReference = "LD/CSWI1$" + fc + "$Pos$" + leaf
        };
        var ambiguous = new LiveIedDataObjectModel
        {
            Name = "Pos", Reference = owner,
            Attributes = [Attr("ST", "stVal"), Attr("ST", "posVal")]
        };
        Assert.False(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            ambiguous, out _, out _));
        var differentFc = new LiveIedDataObjectModel
        {
            Name = "Pos", Reference = owner,
            Attributes = [Attr("ST", "stVal"), Attr("MX", "posVal")]
        };
        Assert.False(IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            differentFc, out _, out _));
    }

    [Fact]
    public void FcdaSubmemberAndIntermediateDAsUseNormalVerifiedLeaf_NotQTimeOrInstant()
    {
        const string owner = "LD/MMXU1.A";
        const string memberRef = "LD/MMXU1.A.phsA";
        LiveIedDataAttributeModel Attribute(string tail) => new()
        {
            ObjectReference = owner,
            AttributePath = "A.phsA." + tail,
            FunctionalConstraint = "MX",
            MmsReference = "LD/MMXU1$MX$A$phsA$" + tail.Replace('.', '$')
        };
        var obj = new LiveIedDataObjectModel {
            Name = "A", Reference = owner,
            Attributes = [Attribute("instCVal.mag.f"), Attribute("cVal.mag.f"),
                Attribute("q"), Attribute("t")]
        };
        var model = new LiveIedModelDiscoveryDocument {
            LogicalDevices = [new LiveIedLogicalDeviceModel {
                LogicalNodes = [new LiveIedLogicalNodeModel {
                    DataObjects = [obj]
                }]
            }]
        };
        var member = new LiveIedDataSetMemberModel {
            Reference = memberRef, FunctionalConstraint = "MX"
        };
        var exactLeaf = memberRef + ".cVal.mag.f";
        var root = IedBrowserStructuredDetails.Member(member, model);
        Assert.Equal(exactLeaf, root.ValueReference);
        Assert.True(root.IsEngineeringValue);
        var projection = new IedBrowserDetailProjection();
        projection.Reset(IedBrowserStructuredDetails.DataSetMemberChildren(member, model, 1));
        var phsA = Assert.Single(projection.Rows.Where(r => r.Name == "phsA [MX]"));
        Assert.Equal(exactLeaf, phsA.ValueReference);
        projection.Toggle(phsA);
        var cval = Assert.Single(projection.Rows.Where(r => r.Name == "cVal [MX]"));
        Assert.Equal(exactLeaf, cval.ValueReference);
        projection.Toggle(cval);
        var mag = Assert.Single(projection.Rows.Where(r => r.Name == "mag [MX]"));
        Assert.Equal(exactLeaf, mag.ValueReference);
        projection.Toggle(mag);
        var f = Assert.Single(projection.Rows.Where(r => r.Name == "f [MX]"));
        Assert.False(f.IsEngineeringValue);
        var live = new IedBrowserLiveValues();
        var session = live.BeginAssociation();
        Assert.True(live.PublishForAssociation(session, exactLeaf, "MX",
            "0", "", "", "Report"));
        foreach (var row in new[] { root, phsA, cval, mag, f })
            Assert.True(live.TryGet(row.ValueReference, row.FunctionalConstraint, out _));
        var index = IedBrowserEngineeringValuePresentation.BuildUnitIndex(
            [new SignalDefinition {
                ObjectReference = memberRef, FunctionalConstraint = "MX", Unit = "A"
            }]);
        foreach (var row in new[] { root, phsA, cval, mag })
            Assert.Equal("0 A", IedBrowserEngineeringValuePresentation.Format(
                "0", IedBrowserEngineeringValuePresentation.ResolveUnit(
                    index, row.EngineeringUnitReference, row.FunctionalConstraint)));
        Assert.Equal("0", live.TryGet(f.ValueReference, "MX", out var primitive)
            ? primitive.Value : "Not read");
        Assert.Equal("0", IedBrowserEngineeringValuePresentation.Format("0", ""));

        var ambiguous = IedBrowserStructuredDetails.AttributeRows(
            [Attribute("cVal.mag.f"), Attribute("cVal.ang.f")], "A");
        var rootGroup = Assert.Single(ambiguous);
        Assert.Equal("LD/MMXU1.A.phsA", rootGroup.ValueReference);
    }

    [Fact]
    public void UnitsMustBeProvenByExactReferenceAndFc_NoCrossObjectBorrowOrDuplication()
    {
        var units = IedBrowserEngineeringValuePresentation.BuildUnitIndex(
            [new SignalDefinition {
                ObjectReference = "LD/MMXU1.Hz", FunctionalConstraint = "MX", Unit = "Hz"
            }, new SignalDefinition {
                ObjectReference = "LD/MMXU1.TotW", FunctionalConstraint = "MX", Unit = "kW"
            }]);
        Assert.Equal("Hz", IedBrowserEngineeringValuePresentation.ResolveUnit(
            units, "LD/MMXU1.Hz.mag.f", "MX"));
        Assert.Equal("", IedBrowserEngineeringValuePresentation.ResolveUnit(
            units, "LD/MMXU1.Hz.mag.f", "ST"));
        Assert.Equal("", IedBrowserEngineeringValuePresentation.ResolveUnit(
            units, "LD/MMXU1.TotVA.mag.f", "MX"));
        Assert.Equal("0 Hz", IedBrowserEngineeringValuePresentation.Format("0", "Hz"));
        Assert.Equal("0 Hz", IedBrowserEngineeringValuePresentation.Format("0 Hz", "Hz"));
        Assert.Equal("good", IedBrowserEngineeringValuePresentation.Format("good", "Hz"));
    }

    [Fact]
    public void DataModel_LnPageShowsExpandableDosAndNestedAttributes_NotFlatNavigation()
    {
        var obj = ModelDo();
        var ln = new LiveIedLogicalNodeModel
        {
            Name = "CSWI1", LnClass = "CSWI", DataObjects = [obj]
        };
        var page = new IedBrowserTreeRow(
            "model/ln/LD/CSWI1", "CSWI1", "LN", 2, ln);
        var rows = IedBrowserPresentation.Details(page, null, connected: true);
        var doRow = Assert.Single(rows.Where(row => row.IsDataObject));
        Assert.Equal("Pos", doRow.Name);
        Assert.True(doRow.HasChildren);
        Assert.Equal("DO", doRow.Glyph);
        Assert.DoesNotContain(rows, row => row.Kind == "Object" && row.Name == "Pos");
        var tree = new IedBrowserDetailProjection();
        tree.Reset(rows);
        tree.Toggle(doRow);
        Assert.Contains(tree.Rows, row => row.Name == "stVal [ST]");
        Assert.Contains(tree.Rows, row => row.Name == "q [ST]");
        Assert.Contains(tree.Rows, row => row.Name == "t [ST]");
        var origin = Assert.Single(tree.Rows.Where(row => row.Name == "origin [ST]"));
        Assert.True(origin.HasChildren);
        tree.Toggle(origin);
        Assert.Contains(tree.Rows, row => row.Name == "orCat [ST]");
    }

    [Fact]
    public void DataModel_DoPageUsesStructuredHierarchyInsteadOfFlatteningDottedDaNames()
    {
        var obj = ModelDo();
        var page = new IedBrowserTreeRow(
            "model/do/LD/CSWI1.Pos", "Pos", "DO", 3, obj);
        var rows = IedBrowserPresentation.Details(page, null, connected: true);
        Assert.DoesNotContain(rows, row => row.Kind == "Object");
        var sbow = Assert.Single(rows.Where(row => row.Name == "SBOw [CO]"));
        var tree = new IedBrowserDetailProjection();
        tree.Reset(rows);
        tree.Toggle(sbow);
        var nested = Assert.Single(tree.Rows.Where(row => row.Name == "origin [CO]"));
        tree.Toggle(nested);
        Assert.Contains(tree.Rows, row => row.Name == "orCat [CO]");
        Assert.DoesNotContain(tree.Rows,
            row => row.Name == "SBOw.origin.orCat [CO]");
    }
}
