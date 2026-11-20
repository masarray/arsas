using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>Regression for a dense setting-group inventory with failed leaves in the first chunk.</summary>
public sealed class P0SgcbDeferredContinuationBehaviorTests
{
    private const string Domain = "BCUGEF650";
    private static string Key(IedBrowserReadTarget target)
        => IedBrowserLiveValues.CanonicalReference(target.Reference) + "|" +
           target.FunctionalConstraint;

    private static (LiveIedModelDiscoveryDocument Model, IedBrowserTreeRow Page)
        BuildInventory(int count)
    {
        var objects = Enumerable.Range(1, count).Select(i =>
        {
            var name = "SGSetting" + i.ToString("D3");
            return new LiveIedDataObjectModel
            {
                Name = name, Reference = Domain + "/LLN0." + name,
                Attributes = [new LiveIedDataAttributeModel
                {
                    ObjectReference = Domain + "/LLN0." + name,
                    AttributePath = name + ".setVal",
                    MmsReference = Domain + "/LLN0$SG$" + name + "$setVal",
                    FunctionalConstraint = "SG", SclBType = "BOOLEAN",
                    TypeDiscoveryStatus = "Exact"
                }]
            };
        }).ToArray();
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = Domain,
                LogicalNodes = [new LiveIedLogicalNodeModel
                    { Name = "LLN0", DataObjects = objects }]
            }]
        };
        var page = new IedBrowserTreeRow("sgcb/BCUGE", "LLN0.SGCB",
            "SGCB", 2, new LiveIedControlBlockModel
            { Kind = "SettingGroupControl", Domain = Domain,
              LogicalNode = "LLN0", Name = "SGCB" });
        return (model, page);
    }

    [Fact]
    public void FailedFirstChunkDoesNotStarveUnattemptedSgLeavesOrTriggerReadAll()
    {
        var (model, page) = BuildInventory(90);
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        var first = IedBrowserReadPlan.BuildFocused(model, page, ledger);
        Assert.Equal(32, first.Count);
        var failed = first.Take(12).Select(Key).ToHashSet(StringComparer.Ordinal);
        foreach (var target in first.Skip(12))
            Assert.True(ledger.PublishForAssociation(association, target.Reference,
                target.FunctionalConstraint, "false", "", "", "MMS Read"));

        // A failed read is absence evidence, not a value and not an instruction to
        // retry the same first 12 leaves indefinitely during bounded continuation.
        var second = IedBrowserReadPlan.BuildFocused(model, page, ledger,
            skipPreviouslyFailed: target => failed.Contains(Key(target)));
        Assert.Equal(32, second.Count);
        Assert.DoesNotContain(second, target => failed.Contains(Key(target)));
        Assert.All(second, t => Assert.Equal("SG", t.FunctionalConstraint));
        Assert.Equal(90, IedBrowserReadPlan.Build(model, page, readAll: false).Count);

        // Changing association discards old absence exclusions; a new operator
        // request can retry an exact failed target without fabricating a value.
        ledger.BeginAssociation();
        var afterReconnect = IedBrowserReadPlan.BuildFocused(model, page, ledger);
        Assert.Equal(32, afterReconnect.Count);
        Assert.Contains(afterReconnect, t => failed.Contains(Key(t)));
    }

    [Fact]
    public void TypedRootBatchSupportsMoreThanOld128SiblingLimitButNotUnboundedInventory()
    {
        Assert.True(IedBrowserSgRootProjection.CanReadRoot(200));
        Assert.False(IedBrowserSgRootProjection.CanReadRoot(513));
        Assert.False(IedBrowserSgRootProjection.CanReadRoot(0));
    }
}
