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
