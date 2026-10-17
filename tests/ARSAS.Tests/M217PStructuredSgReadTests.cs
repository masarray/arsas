using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M217PStructuredSgReadTests
{
    const string Root = "BCUGEF650/SGgndPIOC1$SG";
    static IedBrowserReadTarget Target(string leaf, string fc = "SG")
        => new("BCUGEF650/SGgndPIOC1$" + fc + "$" +
            leaf.Replace('.', '$'), fc, "Int32");

    static MmsTypeSpecificationNode Type(string name, string mms = "integer",
        params MmsTypeSpecificationNode[] children)
        => new() { Name = name, MmsType = mms, Children = children };

    [Fact]
    public void OneMmsLnFcStructureProjectsOnlyTypeVerifiedExactSgLeaves()
    {
        var type = Type("", "structure",
            Type("InMagTyp", "structure", Type("setVal")),
            Type("PIOCEna", "structure", Type("setVal", "boolean")),
            Type("StrVal", "structure", Type("setMag")));
        var wire = MmsDataValue.Structure([
            MmsDataValue.Structure([MmsDataValue.Integer(2)]),
            MmsDataValue.Structure([MmsDataValue.Boolean(false)]),
            MmsDataValue.Structure([MmsDataValue.Integer(0)])]);
        var targets = new[] { Target("InMagTyp.setVal"),
            Target("PIOCEna.setVal"), Target("StrVal.setMag"),
            Target("InMagTyp.setVal", "SP"),
            new IedBrowserReadTarget("OTHER/SGgndPIOC1$SG$PIOCEna$setVal", "SG", "Boolean") };
        var results = IedBrowserSgRootProjection.Project(Root, type, wire, targets);
        Assert.Equal(3, results.Count);
        Assert.Contains(results, v => v.Target.Reference.EndsWith(
            "$InMagTyp$setVal", StringComparison.Ordinal) && v.Value == "2");
        Assert.Contains(results, v => v.Target.Reference.EndsWith(
            "$PIOCEna$setVal", StringComparison.Ordinal) && v.Value == "false");
        Assert.DoesNotContain(results, v => v.Target.Reference.StartsWith("OTHER/", StringComparison.Ordinal));
        Assert.Equal(Root, IedBrowserSgRootProjection.RootFor(Target("PIOCEna.setVal")));
        Assert.Equal("", IedBrowserSgRootProjection.RootFor(Target("PIOCEna.setVal", "SP")));
    }

    [Fact]
    public void StructureSchemaMismatchesAndAmbiguousDuplicatePathsFailClosed()
    {
        var type = Type("", "structure", Type("PIOCEna", "structure", Type("setVal", "boolean")));
        var wire = MmsDataValue.Structure([MmsDataValue.Structure(
            [MmsDataValue.Boolean(true), MmsDataValue.Integer(0)])]);
        Assert.Empty(IedBrowserSgRootProjection.Project(
            Root, type, wire, [Target("PIOCEna.setVal")]));
        Assert.Empty(IedBrowserSgRootProjection.Project(Root,
            Type("", "structure", Type("PIOCEna"), Type("PIOCEna")),
            MmsDataValue.Structure([MmsDataValue.Integer(1), MmsDataValue.Integer(2)]),
            [Target("PIOCEna.setVal")]));
        Assert.Empty(IedBrowserSgRootProjection.Project(Root, type,
            MmsDataValue.Structure([MmsDataValue.Structure([MmsDataValue.Boolean(true)])]),
            [Target("PIOCEna.setVal"), Target("PIOCEna.setVal")]));
    }

    [Fact]
    public void MatchingPositionButWrongWireScalarTypeCannotPublish()
    {
        var schema = Type("", "structure",
            Type("PIOCEna", "structure", Type("setVal", "integer")));
        var wire = MmsDataValue.Structure([
            MmsDataValue.Structure([MmsDataValue.Boolean(true)])]);
        Assert.Empty(IedBrowserSgRootProjection.Project(
            Root, schema, wire, [Target("PIOCEna.setVal")]));
    }

    [Fact]
    public void SgProjectionIsOnlyEphemeralReadResultAndAssociationFenceStillRequired()
    {
        var type = Type("", "structure", Type("PIOCEna", "structure", Type("setVal", "boolean")));
        var wire = MmsDataValue.Structure([MmsDataValue.Structure([MmsDataValue.Boolean(true)])]);
        var result = Assert.Single(IedBrowserSgRootProjection.Project(
            Root, type, wire, [Target("PIOCEna.setVal")]));
        var ledger = new IedBrowserLiveValues();
        var before = ledger.BeginAssociation();
        ledger.BeginAssociation();
        Assert.False(ledger.PublishForAssociation(before, result.Target.Reference, "SG",
            result.Value, "", "", "MMS Read"));
        Assert.False(ledger.TryGet(result.Target.Reference, "SG", out _));
    }
}
