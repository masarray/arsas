using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>Captured field shape: grouped typed FC roots can carry more exact
/// verified children than one 32-target UI hydration slice. No vendor defaults.</summary>
public sealed class P0TypedFcRootScopeBehaviorTests
{
    private const string Ln = "BCUGEF650/SGgndPTOC1";
    private static IedBrowserReadTarget Target(string name, string fc = "SG",
        string ln = Ln) => new(ln + "$" + fc + "$" + name + "$setVal",
            fc, "Enum");

    [Fact]
    public void OneScheduledRootCanVerifyEveryExactSiblingWithoutScanningAnotherLn()
    {
        var full = Enumerable.Range(1, 90).Select(i =>
            Target("Value" + i.ToString("D3"))).ToArray();
        var scheduled = full.Take(32).ToArray();
        var scope = IedBrowserTypedRootScope.Build(
            scheduled, full.Concat(
            [
                Target("Value001", "SP"),
                Target("Value001", "SG", "BCUGEF650/SGgndPTOC2"),
                Target("Value001", "SG", "OTHER/SGgndPTOC1")
            ]).ToArray());

        Assert.Equal(90, scope.Count);
        Assert.All(scope, t => Assert.Equal(Ln + "$SG0",
            IedBrowserSgRootProjection.RootFor(t)));
        Assert.Contains(scope, t => t.Reference.EndsWith(
            "$Value090$setVal", StringComparison.Ordinal));
        Assert.DoesNotContain(scope, t => t.FunctionalConstraint != "SG");

        var type = new MmsTypeSpecificationNode
        {
            MmsType = "structure",
            Children = Enumerable.Range(1, 90)
                .Select(i => new MmsTypeSpecificationNode
                {
                    Name = "Value" + i.ToString("D3"),
                    MmsType = "structure",
                    Children = [new MmsTypeSpecificationNode
                        { Name = "setVal", MmsType = "integer" }]
                }).ToArray()
        };
        var wire = MmsDataValue.Structure(Enumerable.Range(1, 90)
            .Select(i => MmsDataValue.Structure([MmsDataValue.Integer(i)]))
            .ToArray());
        var projected = IedBrowserSgRootProjection.Project(Ln + "$SG0",
            type, wire, scope);
        Assert.Equal(90, projected.Count);
        Assert.Contains(projected, r => r.Target.Reference.EndsWith(
            "$Value090$setVal", StringComparison.Ordinal) && r.Value == "90");
    }

    [Fact]
    public void RootScopeRejectsDuplicateDeclarationsAndDoesNotGrowUnbounded()
    {
        var scheduled = new[] { Target("Value001") };
        var same = Target("Value001");
        var scope = IedBrowserTypedRootScope.Build(scheduled,
            [same, same, Target("Value002")]);
        Assert.Single(scope);
        Assert.Equal(Target("Value002").Reference, scope[0].Reference);

        var many = Enumerable.Range(1, 550).Select(i =>
            Target("Value" + i.ToString("D3"))).ToArray();
        Assert.Empty(IedBrowserTypedRootScope.Build(scheduled, many));
        Assert.Empty(IedBrowserTypedRootScope.Build([], many));
    }

    [Fact]
    public void ExistingPlanRemainsFiniteAndNeverAddsEditableSeOrControlCo()
    {
        var scheduled = new[] { Target("PIOCEna"), Target("InMagTyp") };
        var scope = IedBrowserTypedRootScope.Build(scheduled,
            [..scheduled, Target("Edited", "SE"), Target("Operate", "CO")]);
        Assert.Equal(2, scope.Count);
        Assert.DoesNotContain(scope, t => t.FunctionalConstraint is "SE" or "CO");
    }
}
