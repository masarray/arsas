using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// The existing pinned engine is the authority for finite ordered FC-root
/// Confirmed-Read. It must not be replaced by 32 speculative leaf Reads.
/// </summary>
public sealed class P0GroupedFcRootWirePlanTests
{
    [Fact]
    public void CapturedFiveRootMmsReadUsesOneBoundedOrderedInvoke()
    {
        var names = new[] { "CF0", "DC0", "EX0", "SG0", "ST" };
        var targets = names.Select(suffix => new InitialFcReadTarget
        {
            Domain = "LD",
            LogicalNode = "SGgndPTOC1",
            FunctionalConstraint = suffix == "ST" ? "ST" : suffix[..2],
            MmsItemName = "SGgndPTOC1$" + suffix,
            Source = "ExactLiveMmsGvaVerifiedBrowserRoot"
        }).ToArray();
        var plan = InitialFcReadPlanner.Build(targets, targets.Length);
        Assert.True(plan.IsValid);
        var batch = Assert.Single(plan.Batches);
        Assert.Equal(5, batch.References.Count);
        Assert.Equal(names.Select(s => "SGgndPTOC1$" + s),
            batch.References.Select(r => r.Item));
        var raw = MmsReadBatchCodec.BuildRequest(39, batch.References);
        var transmitted = System.Text.Encoding.ASCII.GetString(raw);
        foreach (var name in batch.References.Select(x => x.Item))
            Assert.Contains(name, transmitted);
        Assert.DoesNotContain("SGgndPTOC2", transmitted);
        Assert.DoesNotContain("CO0", transmitted);
        Assert.DoesNotContain("SE0", transmitted);
    }

    [Fact]
    public void PinnedEngineBatchPlannerRejectsUndeclaredOrMissingFcWithoutWrite()
    {
        var invalid = InitialFcReadPlanner.Build(
            [new InitialFcReadTarget
            {
                Domain = "LD", LogicalNode = "",
                FunctionalConstraint = "SG",
                MmsItemName = "SGgndPTOC1$SG0"
            }]);
        Assert.False(invalid.IsValid);
        Assert.Empty(invalid.Batches);
    }
}
