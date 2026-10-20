using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P0ReportQualityRoutingTests
{
    private static LiveIedDataObjectModel Object(string owner, string process, string fc) =>
        new()
        {
            Name = owner[(owner.LastIndexOf('.') + 1)..],
            Reference = owner,
            Attributes =
            [
                Attribute(owner, process, fc),
                Attribute(owner, "q", fc),
                Attribute(owner, "t", fc)
            ]
        };

    private static LiveIedDataAttributeModel Attribute(
        string owner, string leaf, string fc) => new()
    {
        ObjectReference = owner,
        AttributePath = owner[(owner.LastIndexOf('.') + 1)..] + "." + leaf,
        MmsReference = owner[..owner.IndexOf('.')] + "$" + fc + "$" +
            owner[(owner.IndexOf('.') + 1)..].Replace('.', '$') + "$" +
            leaf.Replace('.', '$'),
        FunctionalConstraint = fc,
        SclBType = leaf == "q" ? "Quality" : "Check",
        TypeDiscoveryStatus = "Exact"
    };

    private static IedBrowserSchemaIndex Schema() =>
        new(new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    DataObjects =
                    [
                        Object("LD/ATCC1.TapChg", "valWTr.posVal", "ST"),
                        Object("LD/CSWI1.Pos", "stVal", "ST"),
                        Object("LD/CSWI2.Pos", "stVal", "ST")
                    ]
                }]
            }]
        });

    private static Iec61850MonitorPoint Point(string valueReference, string quality = "") =>
        new()
        {
            DeviceId = "IED1",
            IecReference = valueReference,
            QualityReference = quality,
            FunctionalConstraint = "ST"
        };

    [Fact]
    public void TapChgQualityBelongsToTheExactSelectedTapPositionNotAnotherCswi()
    {
        var tap = Point("LD/ATCC1.TapChg.valWTr.posVal");
        var cswi = Point("LD/CSWI1.Pos.stVal");
        var index = new Iec61850ReportQualityRouteIndex(Schema(), [tap, cswi]);

        Assert.Same(tap, Assert.Single(index.FindExact("LD/ATCC1.TapChg.q", "ST")));
        Assert.Same(cswi, Assert.Single(index.FindExact("LD/CSWI1.Pos.q", "ST")));
        Assert.Empty(index.FindExact("LD/CSWI2.Pos.q", "ST"));
        Assert.Empty(index.FindExact("LD/ATCC1.TapChg.q", "SV"));
        Assert.Empty(index.FindExact("LD/ATCC10.TapChg.q", "ST"));
        Assert.Empty(index.FindExact("LD/ATCC1.TapChg.t", "ST"));
        Assert.Empty(index.FindExact("LD/atcc1.TapChg.q", "ST"));
    }

    [Fact]
    public void ContradictoryExplicitPointCompanionNeverOverridesLiveSchema()
    {
        var tap = Point("LD/ATCC1.TapChg.valWTr.posVal",
            "LD/CSWI1.Pos.q");
        var index = new Iec61850ReportQualityRouteIndex(Schema(), [tap]);
        Assert.Empty(index.FindExact("LD/ATCC1.TapChg.q", "ST"));
        Assert.Empty(index.FindExact("LD/CSWI1.Pos.q", "ST"));
    }

    [Fact]
    public void NewMonitorSelectionOnlyRoutesItsOwnPoints()
    {
        var tap = Point("LD/ATCC1.TapChg.valWTr.posVal");
        var cswi = Point("LD/CSWI1.Pos.stVal");
        var schema = Schema();
        var first = new Iec61850ReportQualityRouteIndex(schema, [tap]);
        var second = new Iec61850ReportQualityRouteIndex(schema, [cswi]);
        Assert.Single(first.FindExact("LD/ATCC1.TapChg.q", "ST"));
        Assert.Empty(first.FindExact("LD/CSWI1.Pos.q", "ST"));
        Assert.Empty(second.FindExact("LD/ATCC1.TapChg.q", "ST"));
        Assert.Single(second.FindExact("LD/CSWI1.Pos.q", "ST"));
    }
}
