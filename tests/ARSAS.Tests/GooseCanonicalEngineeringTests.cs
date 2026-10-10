using AR.Iec61850.Mms;
using ArIED61850Tester.Services;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;
public sealed class GooseCanonicalEngineeringTests
{
    [Fact]
    public void TypedQuality_BecomesReadableWithoutRawBitString()
    {
        var valid = MmsDataValue.BitString(3, new byte[] { 0, 0 });
        Assert.Equal("Good", GooseTypedValueInterpreter.Render(valid, "", "Quality", "C264/CSWI1.Pos.q"));
        var old = MmsDataValue.BitString(3, new byte[] { 0x01, 0x00 });
        Assert.Contains("Old data", GooseTypedValueInterpreter.RenderQuality(old));
    }

    [Theory]
    [InlineData(0, "Intermediate")]
    [InlineData(1, "Off")]
    [InlineData(2, "On")]
    [InlineData(3, "Invalid")]
    public void TypedDbpos_OnlyFromProvenDpc(int state, string expected)
    {
        var value = MmsDataValue.BitString(6, new[] { (byte)(state << 6) });
        Assert.Equal(expected, GooseTypedValueInterpreter.Render(value, "DPC", "Dbpos", "CSWI1.Pos.stVal"));
    }

    [Fact]
    public void UnknownBitString_RemainsRawInsteadOfInventingEngineeringType()
    {
        var value = MmsDataValue.BitString(3, new byte[] { 0, 0 });
        Assert.Contains("bits(", GooseTypedValueInterpreter.Render(value, "", "", ""));
    }

    [Fact]
    public void FourteenWireMembers_BecomeSevenEngineeringSignalsWithQuality()
    {
        var source = new List<GooseLeafValueSnapshot>();
        for (var n = 1; n <= 7; n++)
        {
            var path = $"LD0/CSWI{n}.Pos";
            source.Add(new GooseLeafValueSnapshot(n*2-1, n*2-2, $"CSWI{n}.Pos",
                path + ".stVal", "ST", "DPC", "Dbpos", "Off", "Intermediate", true, "SCL"));
            source.Add(new GooseLeafValueSnapshot(n*2, n*2-1, $"CSWI{n}.Pos.q",
                path + ".q", "ST", "DPC", "Quality", "Good", "Good", false, "SCL"));
        }
        var projected = GooseCanonicalLeafProjection.Project(source);
        Assert.Equal(14, source.Count);
        Assert.Equal(7, projected.Count);
        Assert.All(projected, leaf => Assert.Equal("Good", leaf.Quality));
        Assert.All(projected, leaf => Assert.EndsWith(".stVal", leaf.SignalReference));
    }

    [Fact]
    public void PreviousTypedQualityAndDbpos_AreReadableInChangeSummary()
    {
        Assert.Equal("Good", GooseTypedValueInterpreter.RenderPrevious("bits(0000, unused=3)", "", "Quality"));
        Assert.Equal("Off", GooseTypedValueInterpreter.RenderPrevious("bits(40, unused=6)", "DPC", "Dbpos"));
    }

    [Fact]
    public void CanonicalQ_OnlyJoinsItsMatchingQualifiedStatus()
    {
        static GooseLeafValueSnapshot Leaf(int i,string reference,string value) =>
            new(i+1,i,reference,reference,"ST","DPC","",value,"",false,"SCL");
        var leaves = new[] {
            Leaf(0,"C264/CSWI1.Pos.stVal","Off"),
            Leaf(1,"C264/CSWI1.Pos.q","Good"),
            Leaf(2,"C264/CSWI2.Pos.stVal","On"),
            Leaf(3,"C264/CSWI2.Pos.q","Invalid")
        };
        var projected = GooseCanonicalLeafProjection.Project(leaves);
        Assert.Equal(2,projected.Count);
        Assert.Equal("Good",projected[0].Quality);
        Assert.Equal("Invalid",projected[1].Quality);
    }
}
