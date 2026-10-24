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
            ["Control Block attributes", "Trigger options", "Optional fields", "Information received in last Report", "Data"],
            details.Where(r => r.Kind == "Section").Select(r => r.Name).ToArray());
        var members = details.Where(r => r.Kind == "Data").ToArray();
        Assert.Equal(2, members.Length);
        Assert.Equal("LLN0.A [ST]", members[0].Name);
        Assert.Equal("LLN0.A [ST]", members[1].Name);
        Assert.All(members, row => Assert.Equal("Not read", row.Value));
        Assert.Contains(details, row => row.Kind == "Property" &&
            row.Name == "Last Report status" && row.Value == "No matching received Report");
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
        Assert.Contains(details, x => x.Kind == "Section" && x.Name == "Control Block attributes");
        Assert.Contains(details, x => x.Kind == "Property" && x.Name == "Number of Setting Groups" && x.Value == "Not read");
        Assert.Contains(details, x => x.Kind == "Message" && x.Name.Contains("No model is available"));
    }

    [Fact]
    public void ExactIedPrefixIsStrippedOnlyFromLdLabels_NeverKeysOrReferences()
    {
        const string ied = "AA1E1F06R4";
        var model = new LiveIedModelDiscoveryDocument
        {
            IedName = ied,
            LogicalDevices = [new LiveIedLogicalDeviceModel {
                MmsDomain = ied + "ADD"
            }, new LiveIedLogicalDeviceModel {
                MmsDomain = ied + "Application"
            }],
            DataSets = [new LiveIedDataSetModel {
                Domain = ied + "Application", LogicalNode = "LLN0",
                Name = "Analog", Reference = ied + "Application/LLN0.Analog"
            }]
        };
        var rcb = new RcbExportRow {
            Reference = ied + "Application/LLN0.RP.Unbuffer01",
            Name = "Unbuffer01"
        };
        var roots = IedBrowserPresentation.Roots(model, [rcb]);
        var modelRoot = Assert.Single(roots.Where(x => x.Key == "category/model"));
        Assert.Equal(["ADD", "Application"],
            modelRoot.GetChildren().Select(x => x.Title).ToArray());
        Assert.Equal("model/ld/" + ied + "Application",
            modelRoot.GetChildren()[1].Key);
        var ds = Assert.Single(roots.Where(x => x.Key == "category/datasets"));
        Assert.Equal("Application", Assert.Single(ds.GetChildren()).Title);
        var reports = Assert.Single(roots.Where(x => x.Key == "category/reports"));
        Assert.Equal("Application", Assert.Single(reports.GetChildren()).Title);
        Assert.Equal("AA1E1F06R4Application",
            IedBrowserPresentation.LdDisplayName("AA1E1F06R4Application", "OTHER"));
        Assert.Equal("Application", IedBrowserPresentation.LdDisplayName(
            "AA1E1F06R4Application", ied));
    }

    [Fact]
    public void CategoriesFollowEngineeringNavigationOrder_WithoutMmsCalls()
    {
        var roots = IedBrowserPresentation.Roots(new LiveIedModelDiscoveryDocument(),
            Array.Empty<RcbExportRow>());
        var projection = new IedBrowserTreeProjection();
        projection.Reset(roots);
        Assert.Equal(
            ["Reports", "DataSets", "Data Model", "Files", "Setting Groups", "GOOSE"],
            projection.Rows.Select(x => x.Title).ToArray());
        Assert.All(projection.Rows, x => Assert.Equal(0, x.Depth));
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
        var roots = IedBrowserPresentation.Roots(null, [row]);
        tree.Reset(roots);
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
        var roots = IedBrowserPresentation.Roots(model, Array.Empty<RcbExportRow>());
        projection.Reset(roots);
        Assert.Equal(6, projection.Rows.Count); // IED is selected in the existing Explorer.
        var dataModel = Assert.Single(projection.Rows.Where(x => x.Key == "category/model"));
        projection.Expand(dataModel);
        Assert.Equal(7, projection.Rows.Count); // + LD, but no DO/DA visual rows.
    }

    [Fact]
    public void ReportContainers_RenderLeafListInRightGrid_AndNavigateToTheExistingTreeNode()
    {
        var r1 = new RcbExportRow { Name = "urcb01", Reference = "ZIVLD/LLN0.RP.urcb01",
            Inspection = RcbInspectionEvidence.FromSource("ReportIdOne", null) };
        var r2 = new RcbExportRow { Name = "brcb01", Reference = "ZIVLD/LLN0.BR.brcb01",
            Buffered = true, Inspection = RcbInspectionEvidence.FromSource("ReportIdTwo", null) };
        var roots = IedBrowserPresentation.Roots(null, [r2, r1]);
        var reports = Assert.Single(roots.Where(r => r.Key == "category/reports"));
        var list = IedBrowserPresentation.Details(reports, null, false);
        Assert.Equal(["urcb01", "brcb01"],
            list.Where(r => r.Kind == "Object").Select(r => r.Name).ToArray());
        Assert.DoesNotContain(list, r => r.Name == "ReportIdOne");
        var target = Assert.Single(list.Where(r => r.Name == "brcb01")).NavigateTo!;
        var ln = target.Parent!;
        var ld = ln.Parent!;
        Assert.Equal(2, IedBrowserPresentation.Details(ld, null, false)
            .Count(r => r.NavigateTo?.Kind == "RCB"));
        Assert.Equal(2, IedBrowserPresentation.Details(ln, null, false)
            .Count(r => r.NavigateTo?.Kind == "RCB"));
        var projection = new IedBrowserTreeProjection();
        projection.Reset(roots);
        projection.Reveal(target);
        Assert.Same(target, projection.FindVisible(target.Key));
        Assert.Same(ln, target.Parent);
        Assert.True(reports.IsExpanded);
    }

    [Fact]
    public void GooseAndDataSetCategories_RenderActualObjectsWithoutImplicitReads()
    {
        var model = new LiveIedModelDiscoveryDocument
        {
            GooseControlBlocks = [
                new LiveIedControlBlockModel { Name = "LLN0.gcb01", Kind = "GOOSE",
                    Reference = "ZIVLD/LLN0.GO.gcb01", Domain = "ZIVLD", LogicalNode = "LLN0" },
                new LiveIedControlBlockModel { Name = "LLN0.gcb02", Kind = "GOOSE",
                    Reference = "ZIVLD/LLN0.GO.gcb02", Domain = "ZIVLD", LogicalNode = "LLN0" }
            ],
            DataSets = [
                new LiveIedDataSetModel { Name = "LLN0.DS_Signal", Reference = "ZIVLD/LLN0.DS_Signal",
                    Domain = "ZIVLD", LogicalNode = "LLN0", MemberCount = 22 }
            ]
        };
        var roots = IedBrowserPresentation.Roots(model, Array.Empty<RcbExportRow>());
        var goose = Assert.Single(roots.Where(x => x.Key == "category/goose"));
        Assert.Equal(["LLN0.gcb01", "LLN0.gcb02"],
            IedBrowserPresentation.Details(goose, model, false)
                .Where(r => r.Kind == "Object").Select(r => r.Name).ToArray());
        var datasets = Assert.Single(roots.Where(x => x.Key == "category/datasets"));
        var dsRow = Assert.Single(IedBrowserPresentation.Details(datasets, model, false)
            .Where(r => r.Kind == "Object"));
        Assert.Equal("LLN0.DS_Signal", dsRow.Name);
        Assert.Equal("22 members", dsRow.Value);
        Assert.Equal("DataSet", dsRow.NavigateTo?.Kind);

        var gooseLeaf = IedBrowserPresentation.Details(goose, model, false)
            .First(row => row.Kind == "Object").NavigateTo!;
        var gooseDetails = IedBrowserPresentation.Details(gooseLeaf, model, false);
        Assert.Contains(gooseDetails, row => row.Kind == "Section" &&
            row.Name == "Control Block attributes");
        Assert.DoesNotContain(gooseDetails, row => row.Kind == "Object");

        var datasetDetails = IedBrowserPresentation.Details(dsRow.NavigateTo!, model, false);
        Assert.Contains(datasetDetails, row => row.Kind == "Section" &&
            row.Name == "DataSet attributes");
        Assert.DoesNotContain(datasetDetails, row => row.Kind == "Object");
    }

    [Fact]
    public void ModelHierarchy_RendersLDThenLNThenDOLists_OnTheRight()
    {
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [
                new LiveIedLogicalDeviceModel {
                    MmsDomain = "ZIVLD",
                    LogicalNodes = [
                        new LiveIedLogicalNodeModel { Name = "LLN0", LnClass = "LLN0",
                            DataObjects = [new LiveIedDataObjectModel { Name = "Beh", Reference = "ZIVLD/LLN0.Beh" }] },
                        new LiveIedLogicalNodeModel { Name = "ATCC1", LnClass = "ATCC" }
                    ]
                }
            ]
        };
        var roots = IedBrowserPresentation.Roots(model, Array.Empty<RcbExportRow>());
        var dataModel = Assert.Single(roots.Where(x => x.Key == "category/model"));
        var ld = Assert.Single(IedBrowserPresentation.Details(dataModel, model, false)
            .Where(r => r.Kind == "Object"));
        Assert.Equal("ZIVLD", ld.Name);
        var lns = IedBrowserPresentation.Details(ld.NavigateTo!, model, false)
            .Where(r => r.NavigateTo?.Kind == "LN").ToArray();
        Assert.Equal(["LLN0", "ATCC1"], lns.Select(r => r.Name).ToArray());
        // M2.6B: LN's right pane projects real DO→DA/BDA data rows, not
        // navigation cards. The left tree still stops at LN.
        var details = IedBrowserPresentation.Details(lns[0].NavigateTo!, model, false);
        var doRow = Assert.Single(details.Where(r => r.IsDataObject));
        Assert.Equal("Beh", doRow.Name);
        Assert.Equal("DO", doRow.Glyph);
        Assert.Equal("ZIVLD/LLN0.Beh", doRow.ValueReference);
        Assert.DoesNotContain(details, r => r.NavigateTo?.Kind == "DO");
    }
    [Fact]
    public void ReportFlags_UseEngineReservedBitMapping_AndUnknownDoesNotMeanDisabled()
    {
        // First BER octet is unused-bit count; canonical engine mapping owns bit numbering.
        var triggers = IedBrowserReportFlags.TriggerOptions("0204");
        Assert.False(triggers.Single(x => x.Label == "Data change").IsEnabled);
        Assert.True(triggers.Single(x => x.Label == "General interrogation (GI)").IsEnabled);
        var options = IedBrowserReportFlags.OptionalFields("067E80");
        Assert.Equal(9, options.Count);
        Assert.Contains(options, x => x.IsEnabled == true);
        Assert.All(IedBrowserReportFlags.TriggerOptions("04FF"), x => Assert.Null(x.IsEnabled));
        Assert.All(IedBrowserReportFlags.OptionalFields(""), x => Assert.Null(x.IsEnabled));
        Assert.All(IedBrowserReportFlags.TriggerOptions("mystery-value"), x => Assert.Null(x.IsEnabled));
        Assert.True(IedBrowserReportFlags.TriggerOptions("dchg, gi")
            .Single(x => x.Label == "Data change").IsEnabled);
    }

    [Fact]
    public void BrowserSelectionDefersAndDetachesBeforeDetailCollectionReset()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null &&
               !File.Exists(Path.Combine(root.FullName, "MainWindow.IedBrowser.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root!.FullName, "MainWindow.IedBrowser.cs"));
        Assert.Contains("Dispatcher.BeginInvoke(DispatcherPriority.Background", source);
        Assert.Contains("ticket != _iedBrowserNavigationRenderTicket", source);
        var method = source.IndexOf("private void ReplaceIedBrowserDetails(", StringComparison.Ordinal);
        Assert.True(method >= 0);
        var detach = source.IndexOf("IedBrowserDetailList.ItemsSource = null;", method, StringComparison.Ordinal);
        var reset = source.IndexOf("_iedBrowserDetails.Reset(rows);", method, StringComparison.Ordinal);
        var attach = source.IndexOf("IedBrowserDetailList.ItemsSource = _iedBrowserDetails.Rows;", method, StringComparison.Ordinal);
        Assert.True(detach > method && reset > detach && attach > reset);
    }

    [Fact]
    public void DetailProjection_ExpandsOnlySelectedDataAttribute_AndKeepsQualityUnknown()
    {
        var q = new LiveIedDataAttributeModel
        {
            ObjectReference = "LD/LLN0.Pos", AttributePath = "Pos.q",
            FunctionalConstraint = "ST", MmsReference = "LD/LLN0$ST$Pos$q"
        };
        var stVal = new LiveIedDataAttributeModel
        {
            ObjectReference = "LD/LLN0.Pos", AttributePath = "Pos.stVal",
            FunctionalConstraint = "ST", MmsReference = "LD/LLN0$ST$Pos$stVal"
        };
        var t = new LiveIedDataAttributeModel
        {
            ObjectReference = "LD/LLN0.Pos", AttributePath = "Pos.t",
            FunctionalConstraint = "ST", MmsReference = "LD/LLN0$ST$Pos$t"
        };
        var rows = IedBrowserStructuredDetails.AttributeRows([stVal, q, t], "Pos");
        Assert.Equal(3, rows.Count);
        var projection = new IedBrowserDetailProjection();
        projection.Reset(rows);
        Assert.Equal(3, projection.Rows.Count);
        var quality = rows.Single(r => r.Name.StartsWith("q "));
        projection.Toggle(quality);
        Assert.Equal(8, projection.Rows.Count);
        Assert.Contains(projection.Rows, r => r.Name == "Validity" && r.Value == "Not read");
        Assert.Contains(projection.Rows, r => r.Name == "Quality Details" && r.HasChildren);
        projection.Toggle(quality);
        Assert.Equal(3, projection.Rows.Count);
        var timestamp = rows.Single(r => r.Name.StartsWith("t "));
        projection.Toggle(timestamp);
        Assert.Contains(projection.Rows, r => r.Name == "ClockNotSynchronized" && r.Value == "Not read");
    }

    [Fact]
    public void SettingGroupAndGooseDetails_ExposeRealModelEvidenceWithoutInventedLiveValues()
    {
        var settings = new LiveIedDataAttributeModel {
            ObjectReference = "LD/ATCC1.BndWid", AttributePath = "BndWid.setMag",
            FunctionalConstraint = "SG", MmsReference = "LD/ATCC1$SG$BndWid$setMag"
        };
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD",
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "ATCC1", DataObjects = [new LiveIedDataObjectModel
                    {
                        Name = "BndWid", Reference = "LD/ATCC1.BndWid", Attributes = [settings]
                    }]
                }]
            }],
            DataSets = [new LiveIedDataSetModel
            {
                Reference = "LD/LLN0.Signals", Members = [new LiveIedDataSetMemberModel
                {
                    Index = 0, Reference = "LD/ATCC1.BndWid", FunctionalConstraint = "SG"
                }]
            }]
        };
        var sg = new LiveIedControlBlockModel { Kind = "SGCB", Domain = "LD",
            Name = "SGCB", Reference = "LD/LLN0.SGCB" };
        var sgRows = IedBrowserPresentation.Details(
            new IedBrowserTreeRow("sg", "SGCB", "SGCB", 3, sg), model, true);
        Assert.Contains(sgRows, r => r.Kind == "Section" && r.Name == "Settings");
        var settingRoot = Assert.Single(sgRows.Where(r => r.Kind == "Data" &&
            r.Name == "ATCC1.BndWid.setMag [SG]"));
        Assert.Equal("Not read", settingRoot.Value);
        Assert.Equal("LD/ATCC1$SG$BndWid$setMag", settingRoot.ValueReference);
        Assert.Equal("SG", settingRoot.FunctionalConstraint);
        var goose = new LiveIedControlBlockModel { Kind = "GOOSE", Domain = "LD",
            Reference = "LD/LLN0.GO.cb1", DataSetReference = "LD/LLN0.Signals" };
        var gRows = IedBrowserPresentation.Details(
            new IedBrowserTreeRow("g", "cb1", "GoCB", 3, goose), model, true);
        Assert.Contains(gRows, r => r.Kind == "Section" && r.Name == "Information received in last GOOSE");
        Assert.Contains(gRows, r => r.Kind == "Data" && r.HasChildren && r.Value == "Not read");
    }

    [Fact]
    public void LeftDataModelStopsAtLogicalNode_WhileRightDrillsDoAndDa()
    {
        var attribute = new LiveIedDataAttributeModel
        {
            AttributePath = "Pos.stVal", ObjectReference = "LD/CSWI1.Pos",
            MmsReference = "LD/CSWI1$ST$Pos$stVal", FunctionalConstraint = "ST"
        };
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "LD",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "CSWI1",
                            DataObjects = [new LiveIedDataObjectModel
                            {
                                Name = "Pos", Reference = "LD/CSWI1.Pos",
                                Attributes = [attribute]
                            }]
                        }
                    ]
                }
            ]
        };
        var tree = new IedBrowserTreeProjection();
        var categories = IedBrowserPresentation.Roots(model, []);
        tree.Reset(categories);
        var dataModel = Assert.Single(categories.Where(r => r.Key == "category/model"));
        tree.Expand(dataModel);
        var ld = Assert.Single(tree.Rows.Where(r => r.Kind == "LD"));
        tree.Expand(ld);
        var ln = Assert.Single(tree.Rows.Where(r => r.Kind == "LN"));
        Assert.False(ln.HasChildren);
        Assert.Equal(System.Windows.Visibility.Hidden, ln.ChevronVisibility);
        tree.Expand(ln); // Cannot add DO/DA to the left pane, even via API.
        Assert.DoesNotContain(tree.Rows, r => r.Kind is "DO" or "DA");

        // The LN detail pane now expands DO and DA natively, without
        // manufacturing a duplicate navigation hierarchy in the left tree.
        var rows = IedBrowserPresentation.Details(ln, model, false);
        var doRow = Assert.Single(rows.Where(r => r.IsDataObject));
        Assert.Equal("Pos", doRow.Name);
        Assert.Equal("DO", doRow.Glyph);
        Assert.True(doRow.HasChildren);
        Assert.Null(doRow.NavigateTo);
        var hierarchy = new IedBrowserDetailProjection();
        hierarchy.Reset(rows);
        hierarchy.Toggle(doRow);
        var daRow = Assert.Single(hierarchy.Rows.Where(r => r.Name == "stVal [ST]"));
        Assert.Equal("LD/CSWI1$ST$Pos$stVal", daRow.ValueReference);
        Assert.Equal("ST", daRow.FunctionalConstraint);
        Assert.Equal("DA", daRow.Glyph);
        Assert.DoesNotContain(tree.Rows, r => r.Kind is "DO" or "DA");
    }

    [Fact]
    public void FolderHistory_GoesBackToPreviouslyViewedPage_NotGuessedProtocolParent()
    {
        var reports = new IedBrowserTreeRow("category/reports", "Reports", "Category", 0);
        var ld = new IedBrowserTreeRow("reports/ld/LD", "LD", "LD", 1);
        var rcb = new IedBrowserTreeRow("reports/rcb/LD/LLN0.RP.rcb01", "rcb01", "RCB", 3);
        var trail = new IedBrowserNavigationTrail();

        trail.SelectFromTree(reports);
        Assert.False(trail.CanGoBack);
        Assert.True(trail.Enter(rcb)); // Flat list skips LD and LN by design.
        Assert.True(trail.CanGoBack);
        Assert.Same(reports, trail.Back());
        Assert.False(trail.CanGoBack);
        Assert.Null(trail.Back());

        trail.Enter(ld);
        trail.Enter(rcb);
        Assert.Same(ld, trail.Back());
        Assert.Same(reports, trail.Back());
        trail.Enter(rcb);
        trail.SelectFromTree(ld); // Explicit new tree selection resets right navigation.
        Assert.False(trail.CanGoBack);
        Assert.Same(ld, trail.Current);
    }

    [Fact]
    public void RepeatedRightPageSelection_DoesNotPushRedundantHistory()
    {
        var page = new IedBrowserTreeRow("category/model", "Data Model", "Category", 0);
        var trail = new IedBrowserNavigationTrail();
        trail.SelectFromTree(page);
        Assert.False(trail.Enter(page));
        Assert.False(trail.CanGoBack);
    }

    [Fact]
    public void SharedValueLedger_UsesExactFcAndModelReference_AcrossReportAndRead()
    {
        var values = new IedBrowserLiveValues();
        values.Publish("LD/CSWI1.Pos.stVal", "ST", "on", "good",
            "2026-10-13 10:00", "Report");
        // A DO-to-DA alias is not protocol evidence by itself. It is permitted
        // only when the actual SCL/discovery model declares a unique scalar leaf.
        Assert.False(values.TryGet("LD/CSWI1.Pos", "ST", out _));
        var schema = new IedBrowserSchemaIndex(new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD",
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "CSWI1",
                    DataObjects = [new LiveIedDataObjectModel
                    {
                        Name = "Pos", Reference = "LD/CSWI1.Pos",
                        Attributes = [new LiveIedDataAttributeModel
                        {
                            ObjectReference = "LD/CSWI1.Pos",
                            AttributePath = "Pos.stVal",
                            FunctionalConstraint = "ST",
                            MmsReference = "LD/CSWI1$ST$Pos$stVal"
                        }]
                    }]
                }]
            }]
        });
        Assert.True(values.TryGet("LD/CSWI1.Pos", "ST", out var byDo,
            schema: schema));
        Assert.Equal("on", byDo.Value);
        Assert.False(values.TryGet("LD/CSWI1.Pos", "MX", out _, schema: schema));

        values.Publish("LD/CSWI1$ST$Pos$q", "ST", "good", "", "",
            "MMS Read");
        Assert.True(values.TryGet("LD/CSWI1.Pos.q", "ST", out var quality));
        Assert.Equal("good", quality.Value);

        var versionBefore = values.Version;
        values.Clear(); // MMS association cannot borrow values from a previous session.
        Assert.True(values.Version > versionBefore);
        Assert.False(values.TryGet("LD/CSWI1.Pos.stVal", "ST", out _));
    }

    [Fact]
    public void ReportIngress_IsAuthoritative_AndUpdatesSharedLedgerWithoutChangingEventHistory()
    {
        var values = new IedBrowserLiveValues();
        var point = new Iec61850MonitorPoint
        {
            IecReference = "LD/CSWI1.Pos", FunctionalConstraint = "ST",
            QualityReference = "LD/CSWI1.Pos.q", TimestampReference = "LD/CSWI1.Pos.t",
            Value = "off", Quality = "good", DeviceTimestamp = "10:25:09",
            SourceMode = "StaticBRCB", IsOffline = false
        };
        values.PublishReport(point, observedReportIngress: true);
        Assert.True(values.TryGet("LD/CSWI1.Pos", "ST", out var status));
        Assert.Equal("off", status.Value);
        Assert.Equal("Report", status.Source);
        Assert.True(values.TryGet("LD/CSWI1.Pos.q", "ST", out var quality));
        Assert.Equal("good", quality.Value);
        Assert.True(values.TryGet("LD/CSWI1.Pos.t", "ST", out var time));
        Assert.Equal("10:25:09", time.Value);
        point.IsOffline = true;
        values.PublishReport(point, observedReportIngress: true);
        Assert.Equal("off", status.Value);
    }

    [Fact]
    public void ReadPlan_FollowsExactDataSetMembers_AndReadAllIncludesUnselectedModelLeaves()
    {
        var op = new LiveIedDataAttributeModel
        {
            ObjectReference = "LD/CSWI1.Pos", AttributePath = "Pos.stVal",
            MmsReference = "LD/CSWI1$ST$Pos$stVal", FunctionalConstraint = "ST"
        };
        var measured = new LiveIedDataAttributeModel
        {
            ObjectReference = "LD/MMXU1.TotW", AttributePath = "TotW.mag.f",
            MmsReference = "LD/MMXU1$MX$TotW$mag$f", FunctionalConstraint = "MX"
        };
        var control = new LiveIedDataAttributeModel
        {
            ObjectReference = "LD/CSWI1.Pos", AttributePath = "Pos.Oper.ctlVal",
            MmsReference = "LD/CSWI1$CO$Pos$Oper$ctlVal", FunctionalConstraint = "CO"
        };
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD", LogicalNodes =
                [
                    new LiveIedLogicalNodeModel
                    {
                        Name = "CSWI1", DataObjects = [new LiveIedDataObjectModel
                        {
                            Name = "Pos", Reference = "LD/CSWI1.Pos", Attributes = [op, control]
                        }]
                    },
                    new LiveIedLogicalNodeModel
                    {
                        Name = "MMXU1", DataObjects = [new LiveIedDataObjectModel
                        {
                            Name = "TotW", Reference = "LD/MMXU1.TotW", Attributes = [measured]
                        }]
                    }
                ]
            }],
            DataSets = [new LiveIedDataSetModel
            {
                Reference = "LD/LLN0.DS",
                Members = [new LiveIedDataSetMemberModel
                {
                    Index = 0, Reference = "LD/CSWI1.Pos", FunctionalConstraint = "ST"
                }]
            }]
        };
        var dsNode = new IedBrowserTreeRow("datasets/set", "DS", "DataSet", 2, model.DataSets[0]);
        var subset = IedBrowserReadPlan.Build(model, dsNode, readAll: false);
        Assert.Single(subset);
        Assert.Equal("ST", subset[0].FunctionalConstraint);
        Assert.Equal("LD/CSWI1$ST$Pos$stVal", subset[0].Reference);
        var all = IedBrowserReadPlan.Build(model, null, readAll: true);
        Assert.Equal(2, all.Count);
        Assert.DoesNotContain(all, x => x.FunctionalConstraint == "CO");
    }

    [Fact]
    public void BrowserValueRows_NotifyOnlyOnRealValueChanges()
    {
        var row = new IedBrowserDetailRow("Data", "CSWI1.Pos", "Not read")
        {
            ValueReference = "LD/CSWI1.Pos", FunctionalConstraint = "ST"
        };
        var notifications = 0;
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IedBrowserDetailRow.Value)) notifications++;
        };
        row.Value = "off";
        row.Value = "off";
        row.Value = "on";
        Assert.Equal(2, notifications);
    }

    [Fact]
    public void StaticReportProjection_UsesDatasetMembership_ForUnselectedSignalValues()
    {
        var model = new LiveIedModelDiscoveryDocument
        {
            DataSets = [new LiveIedDataSetModel
            {
                Reference = "LD/LLN0.DS_Signal",
                Members = [
                    new LiveIedDataSetMemberModel { Index = 0, Reference = "LD/ATCC1.TapChg", FunctionalConstraint = "ST" },
                    new LiveIedDataSetMemberModel { Index = 1, Reference = "LD/MMXU1.TotW", FunctionalConstraint = "MX" }
                ]
            }]
        };
        Assert.True(IedBrowserReportValueAuthority.TryResolveFunctionalConstraint(
            model, "LD/LLN0.DS_Signal", "LD/ATCC1.TapChg.posVal", out var fc));
        Assert.Equal("ST", fc);
        Assert.True(IedBrowserReportValueAuthority.TryResolveFunctionalConstraint(
            model, "LD/LLN0.DS_Signal", "LD/MMXU1.TotW.mag.f", out var mxFc));
        Assert.Equal("MX", mxFc);
        Assert.False(IedBrowserReportValueAuthority.TryResolveFunctionalConstraint(
            model, "LD/LLN0.DS_Signal", "LD/CSWI1.Pos.stVal", out _));
        Assert.True(IedBrowserReportValueAuthority.SafeScalar("16"));
        Assert.False(IedBrowserReportValueAuthority.SafeScalar("{ raw MMS struct }"));
    }

    [Fact]
    public void QualityDetailRows_UseRealObservedQualityReference_NotPermanentUnboundPlaceholder()
    {
        var q = new LiveIedDataAttributeModel
        {
            ObjectReference = "LD/CSWI1.Pos", AttributePath = "Pos.q",
            MmsReference = "LD/CSWI1$ST$Pos$q", FunctionalConstraint = "ST"
        };
        var qualityRow = Assert.Single(IedBrowserStructuredDetails.AttributeRows([q], "Pos"));
        var projection = new IedBrowserDetailProjection();
        projection.Reset([qualityRow]);
        projection.Toggle(qualityRow);
        var validity = Assert.Single(projection.Rows.Where(x => x.Name == "Validity"));
        Assert.Equal("Validity", validity.EvidenceField);
        Assert.Equal(q.MmsReference, validity.ValueReference);
        Assert.Equal("ST", validity.FunctionalConstraint);
        var group = Assert.Single(projection.Rows.Where(x => x.Name == "Quality Details"));
        projection.Toggle(group);
        var overflow = Assert.Single(projection.Rows.Where(x => x.Name == "Overflow"));
        Assert.Equal(q.MmsReference, overflow.ValueReference);
        Assert.Equal("Overflow", overflow.EvidenceField);
    }

    [Fact]
    public void BufferedAndUnbufferedBadges_Differ_AndDoNotInventOccupancy()
    {
        var buffered = new RcbExportRow { Name = "brcb01", Reference = "LD/LLN0.BR.brcb01", Buffered = true };
        var unbuffered = new RcbExportRow { Name = "urcb01", Reference = "LD/LLN0.RP.urcb01", Buffered = false };
        var b = new IedBrowserTreeRow("b", buffered.Name, "RCB", 3, buffered);
        var u = new IedBrowserTreeRow("u", unbuffered.Name, "RCB", 3, unbuffered);
        Assert.NotSame(b.GlyphBrush, u.GlyphBrush);
        Assert.Equal(System.Windows.Visibility.Collapsed, b.RcbEnabledVisibility);
        buffered.Availability = AR.Iec61850.Mms.MmsRcbOperationalAvailability.UsedByCaller;
        unbuffered.Availability = AR.Iec61850.Mms.MmsRcbOperationalAvailability.InUse;
        // Distinct ownership badges are no proof that MMS RptEna was read true.
        Assert.Equal(System.Windows.Visibility.Collapsed, b.RcbEnabledVisibility);
        Assert.Equal(System.Windows.Visibility.Collapsed, u.RcbEnabledVisibility);
        Assert.Contains("Active", b.RcbUsageTooltip);
        Assert.Contains("another client", u.RcbUsageTooltip);
    }

    [Fact]
    public void RemoteComtradeFiles_StayInRightGrid_NotDuplicatedInTree()
    {
        var remote = new IedBrowserRemoteFile("FRA00029.cfg", "fault/FRA00029.cfg", 3072L,
            new DateTimeOffset(2026, 10, 13, 10, 20, 0, TimeSpan.Zero));
        var files = IedBrowserPresentation.Roots(null, [], [remote])
            .Single(row => row.Key == "category/files");
        Assert.False(files.HasChildren);
        Assert.DoesNotContain(IedBrowserPresentation.Details(files, null, true),
            row => row.Kind == "Object" && row.NavigateTo?.Kind == "File");
        // The actual remote MMS FileDirectory entry and its metadata must
        // survive in the dedicated right-grid projection, unchanged.
        Assert.Equal("fault/FRA00029.cfg", remote.RemotePath);
        Assert.Equal("3 KB", remote.SizeText);
        Assert.NotEmpty(remote.ModifiedText);
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null &&
               !File.Exists(Path.Combine(root.FullName, "MainWindow.IedBrowser.Files.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var native = File.ReadAllText(
            Path.Combine(root!.FullName, "MainWindow.IedBrowser.Files.cs"));
        Assert.Contains("_iedBrowserFileCatalog?.DisplayRows", native,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RcbGooseAndSgcb_HaveDistinctReadSelectionRoutes()
    {
        var rcb = new IedBrowserTreeRow("r", "r", "RCB", 3,
            new RcbExportRow { Reference = "LD/LLN0.RP.r", DataSetReference = "LD/LLN0.DS" });
        var goose = new IedBrowserTreeRow("g", "g", "GoCB", 3,
            new LiveIedControlBlockModel { Reference = "LD/LLN0.GO.g",
                DataSetReference = "LD/LLN0.DS" });
        var sgcb = new IedBrowserTreeRow("s", "s", "SGCB", 3,
            new LiveIedControlBlockModel { Reference = "LD/LLN0.SGCB", Domain = "LD" });
        Assert.Equal(IedBrowserReadSelectionKind.ReadOnlyRcb, IedBrowserReadPlan.SelectionKind(rcb));
        Assert.Equal(IedBrowserReadSelectionKind.GooseInspectionRequiresSubscriber,
            IedBrowserReadPlan.SelectionKind(goose));
        Assert.Equal(IedBrowserReadSelectionKind.SettingGroupAttributes,
            IedBrowserReadPlan.SelectionKind(sgcb));
        Assert.Equal(IedBrowserReadSelectionKind.FcAttributes,
            IedBrowserReadPlan.SelectionKind(null));
        var model = new LiveIedModelDiscoveryDocument
        {
            DataSets = [new LiveIedDataSetModel { Reference = "LD/LLN0.DS" }]
        };
        Assert.Empty(IedBrowserReadPlan.Build(model, rcb, readAll: false));
        Assert.Empty(IedBrowserReadPlan.Build(model, goose, readAll: false));
    }

    [Fact]
    public void SettingGroup_ReadPlanAndHierarchy_PreserveExactLdAndFunctionalConstraint()
    {
        static LiveIedDataAttributeModel Attr(string ld, string fc) => new()
        {
            ObjectReference = ld + "/ATCC1.BndWid", AttributePath = "BndWid.setMag.f",
            MmsReference = ld + "/ATCC1$" + fc + "$BndWid$setMag$f",
            FunctionalConstraint = fc
        };
        var sg = Attr("LD", "SG");
        var se = Attr("LD", "SE");
        var foreign = Attr("LD2", "SG");
        var mx = Attr("LD", "MX");
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel { MmsDomain = "LD",
                    LogicalNodes = [new LiveIedLogicalNodeModel { Name = "ATCC1",
                        DataObjects = [new LiveIedDataObjectModel { Name = "BndWid",
                            Reference = "LD/ATCC1.BndWid", Attributes = [sg, se, mx] }] }] },
                new LiveIedLogicalDeviceModel { MmsDomain = "LD2",
                    LogicalNodes = [new LiveIedLogicalNodeModel { Name = "ATCC1",
                        DataObjects = [new LiveIedDataObjectModel { Name = "BndWid",
                            Reference = "LD2/ATCC1.BndWid", Attributes = [foreign] }] }] }
            ]
        };
        var node = new IedBrowserTreeRow("s", "SGCB", "SGCB", 3,
            new LiveIedControlBlockModel { Reference = "LD/LLN0.SGCB", Domain = "LD" });
        var targets = IedBrowserReadPlan.Build(model, node, readAll: false);
        Assert.Single(targets);
        Assert.Contains(targets, v => v.Reference == sg.MmsReference && v.FunctionalConstraint == "SG");
        Assert.DoesNotContain(targets, v => v.Reference == se.MmsReference ||
            v.Reference == foreign.MmsReference || v.Reference == mx.MmsReference);
        var rows = IedBrowserPresentation.Details(node, model, connected: true);
        var root = Assert.Single(rows.Where(v => v.Kind == "Data"));
        Assert.Equal("ATCC1.BndWid.setMag [SG]", root.Name);
        Assert.Equal(IedBrowserLiveValues.CanonicalReference(sg.MmsReference),
            IedBrowserLiveValues.CanonicalReference(root.ValueReference));
        var leaf = Assert.Single(root.GetChildren());
        Assert.Equal("f [SG]", leaf.Name);
        Assert.Equal(sg.MmsReference, leaf.ValueReference);
    }

}
