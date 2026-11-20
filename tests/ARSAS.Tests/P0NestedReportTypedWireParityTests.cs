using AR.Iec61850.Asn1;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// IEC WYE/DEL typed MMS Report fixture. Positions are captured LN-root
/// GetVariableAccessAttributes order, NEVER sorted by DA name or quality kind.
/// </summary>
public sealed class P0NestedReportTypedWireParityTests
{
    private const string Root = "LD/MMXU1.PPV";

    private static LiveIedDataAttributeModel Attr(
        string phase, string leaf, string order, string fc = "MX",
        string source = "GetVariableAccessAttributesLogicalNodeTree")
        => new()
        {
            ObjectReference = Root,
            AttributePath = "PPV." + phase + "." + leaf,
            MmsReference = "LD/MMXU1$" + fc + "$PPV$" + phase + "$" + leaf,
            FunctionalConstraint = fc,
            SclBType = leaf == "q" ? "Quality" : "Timestamp",
            MmsType = leaf == "q" ? "bit-string" : "utc-time",
            TypeDiscoveryStatus = "Exact",
            TypeSource = source,
            TypeDeclarationOrder = order
        };

    private static LiveIedModelDiscoveryDocument Model(
        params LiveIedDataAttributeModel[] attributes)
        => new()
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD",
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "MMXU1", LnClass = "MMXU",
                    DataObjects = [new LiveIedDataObjectModel
                    {
                        Reference = Root, Name = "PPV", InferredCdc = "DEL",
                        Attributes = attributes
                    }]
                }]
            }]
        };

    private static LiveIedModelDiscoveryDocument ProvenModel()
        => Model(
            Attr("phsAB", "q", "000000.000002.000000.000001"),
            Attr("phsAB", "t", "000000.000002.000000.000002"),
            Attr("phsBC", "q", "000000.000002.000001.000001"),
            Attr("phsBC", "t", "000000.000002.000001.000002"));

    private static MmsDataValue WireUtc(byte flags = 0x3F)
    {
        var wire = BerWriter.EncodeTlv(
            BerWriter.EncodeIdentifier(BerClass.ContextSpecific, false, 17),
            new byte[] { 0, 0, 0, 1, 0, 0, 0, flags });
        return Assert.Single(MmsDataCodec.DecodeAllData(wire));
    }

    private static MmsReportFrame Frame(
        string fc = "MX", string? dataReference = null,
        bool malformedSecondQuality = false)
        => new()
        {
            Values = [new MmsReportValue
            {
                Member = new MmsDataSetDirectoryMember
                {
                    UserReference = Root, FunctionalConstraint = fc
                },
                DataReference = dataReference,
                Value = MmsDataValue.Structure([
                    MmsDataValue.Structure([
                        MmsDataValue.Boolean(true),
                        MmsDataValue.BitString(3, [0x00, 0x00]),
                        WireUtc(0x3F)]),
                    MmsDataValue.Structure([
                        MmsDataValue.Boolean(false),
                        malformedSecondQuality
                            ? MmsDataValue.BitString(0, [0x00, 0x00])
                            : MmsDataValue.BitString(3, [0x40, 0x00]),
                        WireUtc(0x00)])
                ])
            }]
        };

    [Fact]
    public void WyeDel01_NestedTypedMmsPhasesReachExactLedgerAndTimeQuality()
    {
        var model = ProvenModel();
        var raw = IedBrowserReportWireEvidence.Extract(Frame(), model);
        Assert.Equal(4, raw.Count);
        Assert.Equal(new[]
        {
            Root + ".phsAB.q", Root + ".phsAB.t",
            Root + ".phsBC.q", Root + ".phsBC.t"
        }, raw.Select(x => x.CarrierReference).ToArray());
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        foreach (var item in raw)
        {
            if (item.QualityBits is { } q)
                Assert.True(ledger.PublishForAssociation(association,
                    item.CarrierReference, item.FunctionalConstraint, q.Summary,
                    "", "", "Report quality · raw MMS", qualityEvidence: q));
            if (item.TimeBits is { } t)
                Assert.True(ledger.PublishForAssociation(association,
                    item.CarrierReference, item.FunctionalConstraint, t.EngineeringUtc,
                    "", "", "Report timestamp · raw MMS", utcTimeEvidence: t));
        }
        Assert.True(ledger.TryGet(Root + ".phsAB.q", "MX", out var ab));
        Assert.True(ledger.TryGet(Root + ".phsBC.q", "MX", out var bc));
        Assert.Equal("Good", ab.QualityBits?.Validity);
        Assert.Equal("Invalid", bc.QualityBits?.Validity);
        Assert.True(ledger.TryGet(Root + ".phsAB.t", "MX", out var abTime));
        Assert.True(ledger.TryGet(Root + ".phsBC.t", "MX", out var bcTime));
        Assert.Equal("true", IedBrowserUtcTimeQualityPresentation.FormatField(
            abTime.TimeBits, "ClockNotSynchronized"));
        Assert.Equal("false", IedBrowserUtcTimeQualityPresentation.FormatField(
            bcTime.TimeBits, "ClockNotSynchronized"));
        ledger.BeginAssociation();
        Assert.False(ledger.PublishForAssociation(association, Root + ".phsAB.q",
            "MX", "Good", "", "", "Report quality", qualityEvidence: ab.QualityBits));
        Assert.False(ledger.TryGet(Root + ".phsAB.q", "MX", out _));
    }

    [Fact]
    public void WyeDel02_WrongFcOrDataRefAndUnprovenGvaNeverHydrateNestedBits()
    {
        var model = ProvenModel();
        Assert.Empty(IedBrowserReportWireEvidence.Extract(Frame("ST"), model));
        Assert.Empty(IedBrowserReportWireEvidence.Extract(
            Frame(dataReference: "LD/MMXU1$ST$PPV"), model));
        Assert.Empty(IedBrowserReportWireEvidence.Extract(Frame(),
            Model(Attr("phsAB", "q", "000000.000002.000000.000001",
                source: "NameListHeuristic"))));
        Assert.Empty(IedBrowserReportWireEvidence.Extract(Frame(), Model(
            Attr("phsAB", "q", ""),
            Attr("phsBC", "q", ""))));
    }

    [Fact]
    public void WyeDel03_BadRawQualityWidthFailsClosedWithoutDroppingOtherPhases()
    {
        var raw = IedBrowserReportWireEvidence.Extract(
            Frame(malformedSecondQuality: true), ProvenModel());
        Assert.Equal(3, raw.Count);
        Assert.DoesNotContain(raw, item => item.CarrierReference == Root + ".phsBC.q");
        Assert.Contains(raw, item => item.CarrierReference == Root + ".phsAB.q" &&
            item.QualityBits?.Validity == "Good");
        Assert.Contains(raw, item => item.CarrierReference == Root + ".phsBC.t" &&
            item.TimeBits is { IsDecoded: true, HasWireProvenance: true });
    }

    [Fact]
    public void WyeDel04_AmbiguousDeclarationAndCrossCarrierOrderFailClosed()
    {
        var duplicate = Model(
            Attr("phsAB", "q", "000000.000002.000000.000001"),
            Attr("phsAB", "q", "000000.000002.000000.000001"));
        Assert.Empty(IedBrowserReportWireEvidence.Extract(Frame(), duplicate));
        var inconsistent = Model(
            Attr("phsAB", "q", "000000.000002.000000.000001"),
            Attr("phsBC", "q", "000000.000003.000001.000001"));
        Assert.Empty(IedBrowserReportWireEvidence.Extract(Frame(), inconsistent));
    }
}
