using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class IedBrowserPresentationTests
{
    [Fact]
    public void TreeProjection_MaterializesOnlyVisibleChildren_AndRestoresExpansionByExactKey()
    {
        var calls = 0;
        var child = new IedBrowserTreeRow("data/model", "Data Model", "Category", 1,
            loadChildren: () =>
            {
                calls++;
                return [new IedBrowserTreeRow("data/model/ld", "LD1", "LD", 2)];
            });
        var root = new IedBrowserTreeRow("ied", "IED", "IED", 0, loadChildren: () => [child]);
        var projection = new IedBrowserTreeProjection();

        projection.Reset([root]);
        Assert.Single(projection.Rows);
        Assert.Equal(0, calls);

        projection.Expand(root);
        Assert.Equal(2, projection.Rows.Count);
        Assert.Equal(0, calls);

        projection.Expand(child);
        Assert.Equal(3, projection.Rows.Count);
        Assert.Equal(1, calls);
        Assert.Equal("▾", child.ToggleGlyph);

        var expanded = projection.ExpandedKeys();
        var root2 = new IedBrowserTreeRow("ied", "IED", "IED", 0,
            loadChildren: () => [new IedBrowserTreeRow("data/model", "Data Model", "Category", 1,
                loadChildren: () => [new IedBrowserTreeRow("data/model/ld", "LD1", "LD", 2)])]);
        projection.Reset([root2], expanded);
        Assert.Equal(3, projection.Rows.Count);
        Assert.Equal("data/model/ld", projection.Rows[2].Key);
        projection.Collapse(root2);
        Assert.Single(projection.Rows);
        Assert.False(root2.IsExpanded);
    }

    [Fact]
    public void ReportDetail_UsesOneContiguousThreeSectionLayout_AndPreservesOrderedMembers()
    {
        var first = new LiveIedDataSetMemberModel { Index = 1, Reference = "LD/LLN0.A", FunctionalConstraint = "ST" };
        var duplicated = new LiveIedDataSetMemberModel { Index = 2, Reference = "LD/LLN0.A", FunctionalConstraint = "ST" };
        var model = new LiveIedModelDiscoveryDocument
        {
            DataSets = [new LiveIedDataSetModel
            {
                Name = "Signals", Reference = "LD/LLN0.Signals", Domain = "LD", LogicalNode = "LLN0",
                MemberCount = 2, Members = [first, duplicated]
            }]
        };
        var rcb = new RcbExportRow
        {
            Name = "brcb01",
            Reference = "LD/LLN0.BR.brcb01",
            DataSetReference = "LD/LLN0.Signals",
            Inspection = RcbInspectionEvidence.FromSource("Rpt_0", null),
            Buffered = true
        };
        var node = new IedBrowserTreeRow("reports/rcb", "brcb01", "RCB", 4, rcb);
        var details = IedBrowserPresentation.Details(node, model, connected: false);
        Assert.Equal(
            ["Control Block attributes", "Information received in last Report", "Data"],
            details.Where(r => r.Kind == "Section").Select(r => r.Name).ToArray());
        var members = details.Where(r => r.Kind == "Data").ToArray();
        Assert.Equal(2, members.Length);
        Assert.Equal("1: LD/LLN0.A [ST]", members[0].Name);
        Assert.Equal("2: LD/LLN0.A [ST]", members[1].Name);
        Assert.All(members, row => Assert.Equal("Not read", row.Value));
        Assert.Contains(details, row => row.Kind == "Message"
            && row.Name.Contains("No report-reception snapshot", StringComparison.Ordinal));
    }

    [Fact]
    public void SettingsAreMetadataOnly_WhenEngineHasNoVerifiedGroupValueRead()
    {
        var sg = new LiveIedControlBlockModel
        {
            Reference = "LD/LLN0.SGCB",
            Name = "SGCB",
            Domain = "LD",
            LogicalNode = "LLN0",
            Attributes = ["NumOfSG", "ActSG"]
        };
        var node = new IedBrowserTreeRow("settings/sgcb", "SGCB", "SGCB", 4, sg);
        var details = IedBrowserPresentation.Details(node, null, connected: true);
        Assert.Contains(details, x => x.Kind == "Section" && x.Name == "Setting Group attributes");
        Assert.Contains(details, x => x.Kind == "Data" && x.Name == "NumOfSG" && x.Value == "Not read");
        Assert.Contains(details, x => x.Kind == "Message" && x.Name.Contains("explicit supported engine read"));
    }

    [Fact]
    public void CategoriesFollowEngineeringNavigationOrder_WithoutMmsCalls()
    {
        var roots = IedBrowserPresentation.Roots("IED", new LiveIedModelDiscoveryDocument(),
            Array.Empty<RcbExportRow>());
        var projection = new IedBrowserTreeProjection();
        projection.Reset(roots);
        projection.Expand(roots[0]);
        Assert.Equal(
            ["GOOSE", "Reports", "Setting Groups", "Files", "DataSets", "Data Model"],
            projection.Rows.Skip(1).Select(x => x.Title).ToArray());
        Assert.All(projection.Rows, x => Assert.True(x.Depth <= 1));
    }

    [Fact]
    public void RcbIdentityIsExact_NoSyntheticNumericSuffix()
    {
        var row = new RcbExportRow
        {
            Name = "brcb", Reference = "IEDLD/LLN0.BR.brcb",
            Inspection = RcbInspectionEvidence.FromSource("ReportIdIsNotName", null)
        };
        var tree = new IedBrowserTreeProjection();
        var roots = IedBrowserPresentation.Roots("IED", null, [row]);
        tree.Reset(roots);
        tree.Expand(roots[0]);
        var report = Assert.Single(tree.Rows.Where(x => x.Key == "category/reports"));
        tree.Expand(report);
        var ld = Assert.Single(tree.Rows.Where(x => x.Kind == "LD"));
        tree.Expand(ld);
        var ln = Assert.Single(tree.Rows.Where(x => x.Kind == "LN"));
        tree.Expand(ln);
        var rcbNode = Assert.Single(tree.Rows.Where(x => x.Kind == "RCB"));
        Assert.Equal("brcb", rcbNode.Title);
        Assert.Equal("IEDLD/LLN0.BR.brcb", ((RcbExportRow)rcbNode.Payload!).Reference);
    }

    [Fact]
    public void OneHundredThousandDataObjects_DoNotProduceHundredThousandVisibleTreeRows()
    {
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD",
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "LLN0",
                    DataObjects = Enumerable.Range(0, 100_000)
                        .Select(i => new LiveIedDataObjectModel { Name = "DO" + i, Reference = "LD/LLN0.DO" + i })
                        .ToArray()
                }]
            }]
        };
        var projection = new IedBrowserTreeProjection();
        var roots = IedBrowserPresentation.Roots("IED", model, Array.Empty<RcbExportRow>());
        projection.Reset(roots);
        projection.Expand(roots[0]);
        Assert.Equal(7, projection.Rows.Count); // IED + 6 categories.
        var dataModel = Assert.Single(projection.Rows.Where(x => x.Key == "category/model"));
        projection.Expand(dataModel);
        Assert.Equal(8, projection.Rows.Count); // + LD, but no DO/DA visual rows.
    }
}
