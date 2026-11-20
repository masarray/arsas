using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M214GooseSclDirectoryRecoveryTests
{
    private static LiveIedControlBlockModel Block(string name, string domain = "LD")
        => new()
        {
            Name = name, Kind = "GOOSE", Domain = domain,
            LogicalNode = "LLN0", Reference = domain + "/LLN0.GO." + name,
            DataSetReference = domain + "/LLN0.DS"
        };

    private static LiveIedModelDiscoveryDocument Model(
        string domain, LiveIedControlBlockModel[] blocks,
        LiveIedDataSetModel[]? sets = null)
        => new()
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel { MmsDomain = domain }],
            GooseControlBlocks = blocks,
            DataSets = sets ?? []
        };

    [Fact]
    public void MissingLiveGoCb_RestoresSelectedSclControlWithoutClaimingState()
    {
        var live = Model("LD", []);
        var scl = Model("LD", [Block("gcb1")]);
        var resolved = IedBrowserGooseDirectory.Resolve(live, scl);
        var block = Assert.Single(resolved);
        var category = Assert.Single(IedBrowserPresentation.Roots(live,
            Array.Empty<RcbExportRow>(), sclModel: scl)
            .Where(x => x.Key == "category/goose"));
        var ld = Assert.Single(category.GetChildren());
        var ln = Assert.Single(ld.GetChildren());
        var node = Assert.Single(ln.GetChildren());
        Assert.Equal("GoCB", node.Kind);
        Assert.Equal(block.Reference, ((LiveIedControlBlockModel)node.Payload!).Reference);
        var details = IedBrowserPresentation.Details(node, live, true, scl);
        Assert.Contains(details, r => r.Kind == "Property" &&
            r.Name == "Enabled" && r.Value == "Not read");
    }

    [Fact]
    public void OnlineControlIsAuthoritative_SclDuplicateDoesNotReplaceIt()
    {
        var online = Block("gcb1");
        var live = Model("LD", [online]);
        var scl = Model("LD", [Block("gcb1"), Block("gcb2")]);
        var blocks = IedBrowserGooseDirectory.Resolve(live, scl);
        Assert.Equal(2, blocks.Count);
        Assert.Same(online, blocks[0]);
        Assert.Equal(["gcb1", "gcb2"], blocks.Select(x => x.Name).ToArray());
    }

    [Fact]
    public void ForeignSclLogicalDeviceCannotCreatePhantomGoose()
    {
        var live = Model("LD", []);
        var foreign = Model("OTHER", [Block("alien", "OTHER")]);
        Assert.Empty(IedBrowserGooseDirectory.Resolve(live, foreign));
    }

    [Fact]
    public void SclDataSetIsOnlySelectedForExactDomainAndReference()
    {
        var block = Block("gcb1");
        var live = Model("LD", []);
        var set = new LiveIedDataSetModel
        {
            Domain = "LD", LogicalNode = "LLN0", Name = "DS",
            Reference = "LD/LLN0.DS"
        };
        var scl = Model("LD", [block], [set]);
        Assert.Same(scl, IedBrowserGooseDirectory.ModelForDataSet(live, scl, block));
        Assert.Same(live, IedBrowserGooseDirectory.ModelForDataSet(live,
            Model("OTHER", [Block("foreign", "OTHER")],
                [new LiveIedDataSetModel {
                    Domain = "OTHER", Reference = "LD/LLN0.DS"
                }]), block));
    }
}
