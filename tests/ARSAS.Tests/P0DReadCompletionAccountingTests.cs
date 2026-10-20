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
    public void SgcbFocusedChunk_DoesNotConflateQueuedUnknownWithNeverScheduled()
    {
        var declared = new[] {
            Target("vendor", "SG"),
            Target("swRev", "SG"),
            Target("d", "SG"),
            Target("configRev", "SG"),
            Target("swRev", "SE")
        };
        var scheduled = new[] {declared[0], declared[1], declared[2]};
        var coverage = IedBrowserReadCompletionAccounting.Summarize(
            declared, scheduled,
            target => target.Reference == declared[0].Reference ||
                      target.Reference == declared[4].Reference,
            target => target.Reference == declared[2].Reference);

        Assert.Equal(5, coverage.ConsideredTargets);
        Assert.Equal(2, coverage.ObservedTargets);
        Assert.Equal(1, coverage.TerminalFailures);
        Assert.False(coverage.Truncated);
        Assert.Equal(2, coverage.Gaps.Count);
        Assert.Equal(declared[1], coverage.Gaps[0].Target);
        Assert.Equal(IedBrowserDeferredReadKind.TerminalResultUnverified,
            coverage.Gaps[0].Kind);
        Assert.Equal(declared[3], coverage.Gaps[1].Target);
        Assert.Equal(IedBrowserDeferredReadKind.NotScheduled,
            coverage.Gaps[1].Kind);
        // The SG target must never borrow the same-name SE observation.
        Assert.DoesNotContain(coverage.Gaps, gap =>
            gap.Target.FunctionalConstraint == "SE");
    }

    [Fact]
    public void InterruptedQueuedRead_IsNotATerminalAccessResultFailure()
    {
        var requested = Target("setMag", "SG");
        var summary = IedBrowserReadCompletionAccounting.Summarize(
            [requested], [requested],
            _ => false,
            // An interrupted response has NO confirmed terminal wire result.
            _ => false);
        Assert.Equal(0, summary.TerminalFailures);
        Assert.Equal(IedBrowserDeferredReadKind.TerminalResultUnverified,
            Assert.Single(summary.Gaps).Kind);
    }

    [Fact]
    public void SummaryBoundsAllEvidenceAndDoesNotExecuteBeyondScope()
    {
        var targets = Enumerable.Range(0, 8)
            .Select(i => Target("attr" + i, "SG")).ToArray();
        var called = 0;
        var summary = IedBrowserReadCompletionAccounting.Summarize(
            targets, targets,
            _ => { called++; return false; }, _ => false, maxTargets: 3);
        Assert.Equal(3, called);
        Assert.Equal(3, summary.ConsideredTargets);
        Assert.True(summary.Truncated);
        Assert.Equal(3, summary.Gaps.Count);
        Assert.All(summary.Gaps,
            gap => Assert.Equal(IedBrowserDeferredReadKind.TerminalResultUnverified, gap.Kind));
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
