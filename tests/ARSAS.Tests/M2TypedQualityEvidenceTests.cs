using ArIED61850Tester.Protocol.Mms;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class M2TypedQualityEvidenceTests
{
    [Theory]
    [InlineData(0x00, "Good")]
    [InlineData(0x40, "Invalid")]
    [InlineData(0x80, "Reserved")]
    [InlineData(0xC0, "Questionable")]
    public void WireValidity_UsesOnlyThirteenProvenBits(int first, string expected)
    {
        var raw = new byte[] { 3, (byte)first, 0 };
        Assert.True(MmsQualityDecoder.TryDecode(raw, out var evidence));
        Assert.Equal(expected, evidence.Validity);
        Assert.Equal(expected, MmsQualityDecoder.Decode(raw));
        Assert.Equal("process", evidence.Source);
        Assert.Equal("false", evidence.FormatField("OldData"));
    }

    [Fact]
    public void QualityFlags_AreTypedAndReadableByEveryDetailField()
    {
        // Validity=Good; all eleven independent detail/source flags set.
        var raw = new byte[] { 3, 0x3F, 0xF8 };
        Assert.True(MmsQualityDecoder.TryDecode(raw, out var quality));
        Assert.Equal("Good", quality.Validity);
        Assert.Equal("substituted", quality.Source);
        foreach (var field in new[] {
            "Overflow", "OutOfRange", "BadReference", "Oscillatory",
            "Failure", "OldData", "Inconsistent", "Inaccurate",
            "Test", "OperatorBlocked" })
            Assert.Equal("true", quality.FormatField(field));
        Assert.Equal("Good", Iec61850QualityPresentation.FormatObserved("bits(3FF8,unused=3)"));
        Assert.Equal("good", Iec61850QualityPresentation.FormatObserved("bits(3FF8,unused=3)", "Validity"));
        Assert.Equal("substituted", Iec61850QualityPresentation.FormatObserved("bits(3FF8,unused=3)", "Source"));
        Assert.Equal("true", Iec61850QualityPresentation.FormatObserved("bits(3FF8,unused=3)", "OperatorBlocked"));
        Assert.Contains("OperatorBlocked", quality.Summary);
    }

    [Theory]
    [InlineData("bits(0000,unused=3)", "Good")]
    [InlineData("bits(4000, unused=3)", "Invalid")]
    [InlineData("bits(8000,unused=3)", "Reserved")]
    [InlineData("bits(C000,unused=3)", "Questionable")]
    public void ExistingEngineRendererText_AdaptsToTypedEvidence(string rendered, string validity)
    {
        Assert.True(MmsQualityDecoder.TryDecodeRendered(rendered, out var quality));
        Assert.Equal(validity, quality.Validity);
        Assert.Equal(validity, Iec61850QualityPresentation.FormatObserved(rendered));
    }

    [Theory]
    [InlineData("bits(40,unused=6)")]
    [InlineData("bits(0000,unused=0)")]
    [InlineData("bits(0001,unused=3)")]
    [InlineData("bits(0000,unused=8)")]
    [InlineData("bits(000000,unused=3)")]
    [InlineData("bits(FFFF,unused=3)")]
    [InlineData("bits(nonsense,unused=3)")]
    public void WrongWidthOrNonzeroPadding_NeverCreatesSyntheticQuality(string rendered)
    {
        Assert.False(MmsQualityDecoder.TryDecodeRendered(rendered, out _));
        Assert.Equal("Not exposed", Iec61850QualityPresentation.FormatObserved(rendered, "Validity"));
    }

    [Fact]
    public void NormalizedReportValidity_DoesNotProveMissingIndependentFlags()
    {
        Assert.Equal("Good", Iec61850QualityPresentation.FormatObserved("Good"));
        Assert.Equal("good", Iec61850QualityPresentation.FormatObserved("Good", "Validity"));
        Assert.Equal("Not exposed", Iec61850QualityPresentation.FormatObserved("Good", "Failure"));
        Assert.Equal("Not exposed", Iec61850QualityPresentation.FormatObserved("Invalid / Overflow", "Overflow"));
        Assert.Equal("invalid", Iec61850QualityPresentation.FormatObserved("Invalid / Overflow", "Validity"));
        Assert.Equal("Quality: unavailable", MmsQualityDecoder.Decode(new byte[] { 3, 0, 1 }));
        Assert.False(MmsQualityDecoder.TryDecode(new byte[] { 6, 0x40 }, out _));
    }

    [Theory]
    [InlineData("bits(00,unused=6)", "Intermediate [00]")]
    [InlineData("bits(40,unused=6)", "Open [01]")]
    [InlineData("bits(80,unused=6)", "Close [10]")]
    [InlineData("bits(C0,unused=6)", "Bad state [11]")]
    public void ExistingSharedDbposFormatter_RendersAllFourCodesWithoutQualityLeak(
        string raw, string expected)
    {
        Assert.Equal(expected, Iec61850ValueFormatter.FormatReportProcessValue(
            raw, "Dbpos", "", "Position", "LD/CSWI1.Pos.stVal"));
        Assert.Equal(raw, Iec61850ValueFormatter.FormatReportProcessValue(
            raw, "BitString", "", "Measurement", "LD/GGIO1.RawBits"));
        Assert.False(MmsQualityDecoder.TryDecodeRendered(raw, out _));
    }
}
