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
    public void TypedSp0WireStructureProjectsOnlyExactDeclaredScalarWithFcFence()
    {
        var type = new MmsTypeSpecificationNode
        {
            MmsType = "structure",
            Children =
            [
                new MmsTypeSpecificationNode
                {
                    Name = "PTOCEna", MmsType = "structure",
                    Children = [new MmsTypeSpecificationNode
                        { Name = "setVal", MmsType = "boolean" }]
                },
                new MmsTypeSpecificationNode
                {
                    Name = "InMagTyp", MmsType = "structure",
                    Children = [new MmsTypeSpecificationNode
                        { Name = "setVal", MmsType = "enumerated" }]
                }
            ]
        };
        var data = MmsDataValue.Structure(
        [
            MmsDataValue.Structure([MmsDataValue.Boolean(false)]),
            MmsDataValue.Structure([MmsDataValue.Integer(0)])
        ]);
        var expected = new[] { T("PTOCEna.setVal"), T("InMagTyp.setVal") };
        var actual = IedBrowserSgRootProjection.Project(FcRoot, type, data, expected);
        Assert.Equal(2, actual.Count);
        Assert.Equal(
            IedBrowserLiveValues.CanonicalReference(expected[0].Reference),
            IedBrowserLiveValues.CanonicalReference(actual[0].Target.Reference));
        Assert.Equal(
            IedBrowserLiveValues.CanonicalReference(expected[1].Reference),
            IedBrowserLiveValues.CanonicalReference(actual[1].Target.Reference));
        Assert.Empty(IedBrowserSgRootProjection.Project(
            FcRoot, type, data, [T("PTOCEna.setVal", "SE")]));
        Assert.Empty(IedBrowserSgRootProjection.Project(
            FcRoot, type, MmsDataValue.Structure(
                [MmsDataValue.Structure([MmsDataValue.Integer(0)])]), expected));
    }

    [Fact]
    public void ActiveSGAndEditableSEMustNotBeAliasedToSpRoot()
    {
        Assert.Equal("BCUGEF650/SGgndPIOC1$SG0",
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
