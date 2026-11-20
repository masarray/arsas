using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P0DReadCompletionAccountingTests
{
    private const string Domain = "TESTLD";
    private static IedBrowserReadTarget Target(string name, string fc = "DC")
        => new(Domain + "/LLN0$" + fc + "$NamPlt$" + name, fc, "VisString255");

    [Fact]
    public void Missing_Ln_Read_Acknowledgements_Are_Deferred_Not_Falsely_Failed()
    {
        var declared = new[] {
            Target("vendor"), Target("swRev"), Target("d"), Target("configRev")
        };
        var scheduled = new[] {declared[0], declared[1], declared[3]};
        var observed = new[] {declared[0], declared[3]}
            .Select(t => t.Reference).ToHashSet(StringComparer.Ordinal);
        var gaps = IedBrowserReadCompletionAccounting.Classify(declared, scheduled,
            target => observed.Contains(target.Reference), _ => false);

        Assert.Equal(2, gaps.Count);
        Assert.Equal("swRev", gaps[0].Target.Reference.Split('$').Last());
        Assert.Equal(IedBrowserDeferredReadKind.TerminalResultUnverified, gaps[0].Kind);
        Assert.Equal("d", gaps[1].Target.Reference.Split('$').Last());
        Assert.Equal(IedBrowserDeferredReadKind.NotScheduled, gaps[1].Kind);
    }

    [Fact]
    public void FailureAndValidObservationCannotBeErasedByDeferredMarker()
    {
        var target = Target("swRev");
        var unrelatedFc = Target("swRev", "ST");
        var list = IedBrowserReadCompletionAccounting.Classify(
            [target, unrelatedFc], [target, unrelatedFc],
            t => t.FunctionalConstraint == "ST",
            t => t.FunctionalConstraint == "DC");
        Assert.Empty(list);
    }

    [Fact]
    public void CaseSensitiveIecReference_AndBoundedPageCannotBorrowSiblingEvidence()
    {
        var correct = Target("swRev");
        var other = Target("SwRev");
        var gap = IedBrowserReadCompletionAccounting.Classify(
            [correct, other], [correct],
            target => target.Reference == correct.Reference, _ => false,
            maxTargets: 2);
        Assert.Equal(IedBrowserDeferredReadKind.NotScheduled,
            Assert.Single(gap).Kind);
        Assert.Empty(IedBrowserReadCompletionAccounting.Classify(
            [correct], [], _ => false, _ => false, maxTargets: 0));
    }
}
