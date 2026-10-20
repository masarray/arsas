using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Ensures Browser selection never trusts stale/detached UI nodes as MMS inventory.
/// All targets and types must originate in the active live discovery model.
/// </summary>
public sealed class P0HBrowserModelInventoryAuthorityTests
{
    private const string Prefix = "IEDLD0/XCBR1";

    private static LiveIedDataAttributeModel Leaf(
        string leaf, string fc, string type = "Boolean") => new()
    {
        ObjectReference = Prefix + ".Pos",
        AttributePath = "Pos." + leaf,
        MmsReference = Prefix + "$" + fc + "$Pos$" + leaf,
        FunctionalConstraint = fc,
        SclBType = type,
        MmsType = type,
        TypeDiscoveryStatus = "Exact"
    };

    private static LiveIedModelDiscoveryDocument Model(
        params LiveIedDataAttributeModel[] attributes) => new()
    {
        LogicalDevices =
        [
            new LiveIedLogicalDeviceModel
            {
                MmsDomain = "IEDLD0",
                LogicalNodes =
                [
                    new LiveIedLogicalNodeModel
                    {
                        Name = "XCBR1",
                        LnClass = "XCBR",
                        DataObjects =
                        [
                            new LiveIedDataObjectModel
                            {
                                Name = "Pos",
                                Reference = Prefix + ".Pos",
                                Attributes = attributes
                            }
                        ]
                    }
                ]
            }
        ]
    };

    private static IedBrowserTreeRow DetachedPage(
        params LiveIedDataAttributeModel[] attributes) =>
        new(Prefix + ".Pos", "Pos", "DO", 2, new LiveIedDataObjectModel
        {
            Name = "Pos",
            Reference = Prefix + ".Pos",
            Attributes = attributes
        });

    [Fact]
    public void DetachedUiPage_CannotInjectUnknownSvOrCoReferences()
    {
        var live = Model(Leaf("subEna", "SV"), Leaf("stVal", "ST"));
        var page = DetachedPage(
            Leaf("subEna", "SV", "ForgedDataType"),
            Leaf("opRcvd", "OR"), // not in active model
            Leaf("blkEna", "BL"), // not in active model
            Leaf("Oper", "CO"));  // control is never a scalar Read
        var selected = IedBrowserReadPlan.Build(live, page, readAll: false);
        var target = Assert.Single(selected);
        Assert.Equal(Prefix + "$SV$Pos$subEna", target.Reference);
        Assert.Equal("SV", target.FunctionalConstraint);
        Assert.Equal("Boolean", target.DataType); // type comes from live model, not UI
    }

    [Fact]
    public void SameReferenceWrongFc_IsNotAuthorizedByCatalog()
    {
        var live = Model(Leaf("stVal", "ST"));
        var page = DetachedPage(Leaf("stVal", "SV"));
        Assert.Empty(IedBrowserReadPlan.Build(live, page, readAll: false));
    }

    [Fact]
    public void DuplicateModelWireIdentity_FailsClosedInsteadOfChoosingFirstType()
    {
        var live = Model(Leaf("subVal", "SV", "Dbpos"),
            Leaf("subVal", "SV", "Boolean"));
        var page = DetachedPage(Leaf("subVal", "SV", "Dbpos"));
        Assert.Empty(IedBrowserReadPlan.Build(live, page, readAll: false));
        Assert.Empty(IedBrowserReadPlan.Build(live, null, readAll: true));
    }

    [Fact]
    public void LegitimateLiveModel_StillReadsExactSvOrBlAndSt()
    {
        var live = Model(Leaf("subEna", "SV"),
            Leaf("opOk", "OR"), Leaf("blkEna", "BL"), Leaf("stVal", "ST"));
        var page = DetachedPage(Leaf("subEna", "SV"),
            Leaf("opOk", "OR"), Leaf("blkEna", "BL"), Leaf("stVal", "ST"));
        var selected = IedBrowserReadPlan.Build(live, page, readAll: false);
        Assert.Equal(4, selected.Count);
        Assert.Equal(new[] { "SV", "OR", "BL", "ST" },
            selected.Select(target => target.FunctionalConstraint));
    }
}
