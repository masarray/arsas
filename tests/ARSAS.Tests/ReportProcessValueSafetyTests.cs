using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ReportProcessValueSafetyTests
{
    [Fact]
    public void BooleanSignal_Rejects_ReportBitStringMetadata()
    {
        const string raw = "bits(0000, unused=6)";
        var formatted = Iec61850ValueFormatter.Format(raw, "Boolean", string.Empty);

        var safe = ReportProcessValueSafety.IsSafe(
            raw,
            formatted,
            "Boolean",
            "AA1C1F13R4DSQZ1/CILO1.EnaOpn.stVal",
            out var reason);

        Assert.False(safe);
        Assert.Contains("Boolean", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BIT STRING", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DbposSignal_Allows_ExactTwoBitProcessValue()
    {
        const string raw = "bits(40, unused=6)";
        var formatted = Iec61850ValueFormatter.Format(raw, "Dbpos", string.Empty);

        var safe = ReportProcessValueSafety.IsSafe(
            raw,
            formatted,
            "Dbpos",
            "AA1C1F13R4DSQZ1/CSWI1.Pos.stVal",
            out var reason);

        Assert.True(safe, reason);
        Assert.Equal("Open [01]", formatted);
    }

    [Fact]
    public void DbposSignal_Rejects_InclusionBitmapMasqueradingAsValue()
    {
        const string raw = "bits(FFFFFFFFF0, unused=4)";
        var formatted = Iec61850ValueFormatter.Format(raw, "Dbpos", string.Empty);

        var safe = ReportProcessValueSafety.IsSafe(
            raw,
            formatted,
            "Dbpos",
            "AA1C1F13R4DSQZ1/CSWI1.Pos.stVal",
            out var reason);

        Assert.False(safe);
        Assert.Contains("non-2-bit", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BooleanSignal_Allows_StructuredStVal_WhenFormatterProjectsScalar()
    {
        const string raw = "Structure(3) {stVal=false, q=bits(0000, unused=3), t=2026-08-16 04:00:00 UTC}";
        var formatted = Iec61850ValueFormatter.Format(raw, "Boolean", string.Empty);

        var safe = ReportProcessValueSafety.IsSafe(
            raw,
            formatted,
            "Boolean",
            "AA1C1F13R4ADD/GGIO6.CBOpnd.stVal",
            out var reason);

        Assert.True(safe, reason);
        Assert.Equal("False", formatted);
    }

    [Fact]
    public void ScalarSignal_Rejects_UnprojectedStructure()
    {
        const string raw = "Structure(3) {mag=123.4, q=bits(0000, unused=3), t=2026-08-16 04:00:00 UTC}";
        var formatted = Iec61850ValueFormatter.Format(raw, "Float", "A");

        var safe = ReportProcessValueSafety.IsSafe(
            raw,
            formatted,
            "Float",
            "AA1C1F13R4RPRE_MMXU1/A.phsA.cVal.mag.f",
            out var reason);

        Assert.False(safe);
        Assert.Contains("structured", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NativeBitStringSignal_RemainsAllowed()
    {
        const string raw = "bits(A0, unused=3)";

        var safe = ReportProcessValueSafety.IsSafe(
            raw,
            raw,
            "BitString",
            "IEDLD0/GGIO1.SomeBits.stVal",
            out var reason);

        Assert.True(safe, reason);
    }

    [Fact]
    public void QualitySignal_RemainsAllowedAsNativeBitString()
    {
        const string raw = "bits(0000, unused=3)";

        var safe = ReportProcessValueSafety.IsSafe(
            raw,
            raw,
            "Quality",
            "IEDLD0/GGIO1.Ind1.q",
            out var reason);

        Assert.True(safe, reason);
    }
}
