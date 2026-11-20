using AR.Iec61850.Mms;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class M214ReportQualityDataRefIdentityTests
{
    private const string QualityReference = "LD/CSWI1.Pos.q";

    private static MmsReportFrame Frame(
        string? dataReference,
        string memberReference = QualityReference)
        => new()
        {
            Values =
            [
                new MmsReportValue
                {
                    Index = 0,
                    Member = new MmsDataSetDirectoryMember
                    {
                        UserReference = memberReference,
                        FunctionalConstraint = "ST"
                    },
                    DataReference = dataReference ?? "",
                    // IEC 61850 Quality: Overflow, OutOfRange, OldData and
                    // Inaccurate set, while Source remains process.
                    Value = MmsDataValue.BitString(3, new byte[] { 0x31, 0x40 })
                }
            ]
        };

    [Theory]
    [InlineData("LD/CSWI1.Pos.q")]
    [InlineData("LD/CSWI1$ST$Pos$q")]
    [InlineData(null)]
    public void ExactOrAbsentOptionalDataRef_PreservesActualRawQuality(string? dataReference)
    {
        var raw = Assert.Single(IedBrowserReportWireEvidence.Extract(Frame(dataReference)));
        Assert.Equal(QualityReference, raw.CarrierReference);
        Assert.Equal("ST", raw.FunctionalConstraint);
        var quality = Assert.IsType<ArIED61850Tester.Protocol.Mms.MmsQualityEvidence>(raw.QualityBits);
        Assert.Equal("true", quality.FormatField("Overflow"));
        Assert.Equal("true", quality.FormatField("OutOfRange"));
        Assert.Equal("true", quality.FormatField("OldData"));
        Assert.Equal("true", quality.FormatField("Inaccurate"));
        Assert.Equal("process", quality.FormatField("Source"));
    }

    [Theory]
    [InlineData("LD/CSWI1.Mod.q")]
    [InlineData("OTHER/CSWI1.Pos.q")]
    [InlineData("LD/CSWI2.Pos.q")]
    [InlineData("LD/CSWI1$MX$Pos$q")]
    public void ConflictingOnWireDataRef_CannotAttributeBitsToAnotherObject(
        string contradictoryReference)
    {
        Assert.Empty(IedBrowserReportWireEvidence.Extract(
            Frame(contradictoryReference)));
    }

    [Fact]
    public void MissingMemberIdentity_CannotBeRepairedFromOnWireText()
    {
        var frame = new MmsReportFrame
        {
            Values =
            [
                new MmsReportValue
                {
                    Index = 0,
                    DataReference = "LD/CSWI1$ST$Pos$q",
                    Value = MmsDataValue.BitString(3, new byte[] { 0x31, 0x40 })
                }
            ]
        };
        Assert.Empty(IedBrowserReportWireEvidence.Extract(frame));
    }
}
