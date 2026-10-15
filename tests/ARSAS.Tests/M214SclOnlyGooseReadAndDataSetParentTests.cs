using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M214SclOnlyGooseReadAndDataSetParentTests
{
    private const string Owner = "LD/ATCC1.TapChg";
    private static LiveIedDataAttributeModel Field(string path) => new()
    {
        ObjectReference = Owner, AttributePath = "TapChg." + path,
        FunctionalConstraint = "ST", MmsReference =
            "LD/ATCC1$ST$TapChg$" + path.Replace('.', '$')
    };
    private static LiveIedModelDiscoveryDocument Model(
        LiveIedDataAttributeModel[] attrs)
        => new()
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD",
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "ATCC1",
                    DataObjects = [new LiveIedDataObjectModel
                    {
                        Reference = Owner, Name = "TapChg",
                        Attributes = attrs
                    }]
                }]
            }],
            DataSets = [new LiveIedDataSetModel
            {
                Domain = "LD", LogicalNode = "LLN0",
                Reference = "LD/LLN0.DS",
                Members = [new LiveIedDataSetMemberModel
                {
                    Reference = Owner, FunctionalConstraint = "ST"
                }]
            }]
        };

    [Fact]
    public void ExactSclTypedValueDirectoryRecoversRealAlreadyObservedReportLeaves()
    {
        var live = Model([Field("q"), Field("t")]);
        var scl = Model([Field("valWTr"), Field("posVal"),
            Field("q"), Field("orCat"), Field("orIdent")]);
        var set = Assert.Single(live.DataSets);
        var row = Assert.Single(IedBrowserPresentation.Details(
            new IedBrowserTreeRow("dataset", "DS", "DataSet", 3, set),
            live, true, scl).Where(r => r.Kind == "Data"));
        Assert.Equal(2, row.ScalarComponents.Count);
        Assert.Equal(Owner, IedBrowserLiveValues.CanonicalReference(row.ValueReference));
        var ledger = new IedBrowserLiveValues();
        var token = ledger.BeginAssociation();
        ledger.PublishForAssociation(token, Owner + ".valWTr", "ST", "4",
            "", "", "Report");
        ledger.PublishForAssociation(token, Owner + ".posVal", "ST", "4",
            "", "", "Report");
        Assert.True(IedBrowserMultiLeafPresentation.TryCompose(row, ledger,
            true, out var composed));
        Assert.Equal("valWTr: 4; posVal: 4", composed.Text);
    }

    [Fact]
    public void SclOnlyGoCbMayNavigateButNotProduceSpeculativeMmsReadTargets()
    {
        var live = Model([]);
        var block = new LiveIedControlBlockModel
        {
            Name = "gcb", Kind = "GOOSE", DiscoveryStatus = "SCLOnly", Domain = "LD",
            LogicalNode = "LLN0", Reference = "LD/LLN0.GO.gcb",
            DataSetReference = "LD/LLN0.DS"
        };
        var scl = Model([]);
        var withGoose = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = scl.LogicalDevices,
            DataSets = scl.DataSets,
            GooseControlBlocks = [block]
        };
        var category = Assert.Single(IedBrowserPresentation.Roots(live,
            Array.Empty<RcbExportRow>(), sclModel: withGoose)
            .Where(r => r.Key == "category/goose"));
        var node = Assert.Single(Assert.Single(Assert.Single(
            category.GetChildren()).GetChildren()).GetChildren());
        Assert.Empty(IedBrowserReadPlan.Build(live, node, false));
    }
}
