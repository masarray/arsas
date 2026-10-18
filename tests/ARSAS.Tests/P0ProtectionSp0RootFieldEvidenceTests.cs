using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>BCUGE captured MMS trace: gndPTOC2$SP0 is a typed FC root, not a write.</summary>
public sealed class P0ProtectionSp0RootFieldEvidenceTests
{
    private const string FcRoot = "BCUGEF650/gndPTOC2$SP0";
    private static IedBrowserReadTarget T(string member, string fc = "SP")
        => new("BCUGEF650/gndPTOC2$" + fc + "$" + member.Replace('.', '$'),
            fc, "Enum");

    [Theory]
    [InlineData("PTOCEna.setVal")]
    [InlineData("InMagTyp.setVal")]
    [InlineData("RsMod.setVal")]
    [InlineData("TmACrv.setCharact")]
    [InlineData("StrVal.setMag")]
    [InlineData("TmMult.setMag")]
    public void TypedSpRootMustMatchCapturedBcugeFcName(string member)
    {
        Assert.Equal(FcRoot, IedBrowserSgRootProjection.RootFor(T(member)));
    }

    [Fact]
    public void ActiveSGAndEditableSEMustNotBeAliasedToSpRoot()
    {
        Assert.Equal("BCUGEF650/SGgndPIOC1$SG",
            IedBrowserSgRootProjection.RootFor(
                new IedBrowserReadTarget(
                    "BCUGEF650/SGgndPIOC1$SG$InMagTyp$setVal",
                    "SG", "Enum")));
        Assert.Equal("", IedBrowserSgRootProjection.RootFor(
            T("InMagTyp.setVal", "SE")));
        Assert.Equal("", IedBrowserSgRootProjection.RootFor(
            T("InMagTyp.setVal", "CF")));
    }
}
