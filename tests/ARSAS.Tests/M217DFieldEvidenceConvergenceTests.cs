using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// M2.17-D: physical-feedback-driven UI/evidence boundary regression.
/// Never substitute guessed SCL state for a received PDU or verified MMS read.
/// </summary>
public sealed class M217DFieldEvidenceConvergenceTests
{
    private static IedBrowserGooseExpectedIdentity Expected(string dataSet = "")
        => new("BCUGE", "BCUGEF650/LLN0.GO.BCU", dataSet,
            "BCUGE03", 33, 1, "", "", null);

    private static GooseStreamSnapshot Frame(
        string goCb = "BCUGEF650/LLN0$GO$BCU",
        string dataSet = "BCUGEF650/LLN0$GOOSE1",
        string ied = "", string appId = "0x0021",
        IReadOnlyList<GooseLeafValueSnapshot>? leaves = null)
        => new("physical-wire", appId, goCb, "BCUGE03", dataSet,
            "02:00:4C:4F:5B:50", "01:0C:CD:01:00:33", "VID 0 / PCP 4",
            "1", "42", "Healthy", "20000 ms", "1", ied, "Unbound", "",
            "14:22:24.498", 43, 0, false, false,
            leaves ?? [], "0", "4", leaves?.Count ?? 0);

    private static GooseLeafValueSnapshot Leaf(int slot, string value)
        => new(slot + 1, slot, "unbound", "", "", "", "",
            value, "", false, "Unbound");

    [Fact]
    public void ExactGoCbWireIdentityRecoversLiveMetadataWithoutMmsDataSetInventory()
    {
        var frame = Frame(leaves: [Leaf(0, "Intermediate [00]"),
            Leaf(1, "Intermediate [00]"), Leaf(2, "false"), Leaf(3, "false")]);
        Assert.Same(frame, IedBrowserGooseInspection.Select(Expected(), [frame]));
        Assert.Same(frame, IedBrowserGooseInspection.Select(
            Expected("BCUGEF650/LLN0.GOOSE1"), [frame]));
        var slots = IedBrowserGooseInspection.OrderedWireEntries(frame);
        Assert.NotNull(slots);
        Assert.Equal(4, slots.Count);
        Assert.Equal("false", slots[3].Value);
        Assert.All(slots, item => Assert.Equal("", item.FunctionalConstraint));
        // No assigned IEC variable identity: these are explicitly wire-only.
        Assert.All(slots, item => Assert.Equal("", item.SignalReference));
    }

    [Fact]
    public void NeverSelectWrongPublisherAmbiguousFrameOrConflictingConfiguration()
    {
        var right = Frame(leaves: [Leaf(0, "0")]);
        Assert.Null(IedBrowserGooseInspection.Select(Expected(),
            [right with { GoCbRef = "BCUGEF650/LLN0$GO$BCU3" }]));
        Assert.Null(IedBrowserGooseInspection.Select(Expected(),
            [right with { ModelIedName = "OTHER" }]));
        Assert.Null(IedBrowserGooseInspection.Select(Expected(),
            [right with { AppIdText = "0x002C" }]));
        Assert.Null(IedBrowserGooseInspection.Select(Expected(),
            [right with { ConfigurationRevisionText = "2" }]));
        Assert.Null(IedBrowserGooseInspection.Select(
            Expected("BCUGEF650/LLN0.GOOSE1"),
            [right with { DataSetReference = "BCUGEF650/LLN0$GOOSE2" }]));
        Assert.Null(IedBrowserGooseInspection.Select(Expected(), [right, right]));
        Assert.Null(IedBrowserGooseInspection.Select(
            Expected() with { GoCbRef = "" }, [right]));
    }

    [Fact]
    public void UnboundWireSlotsRequireCompleteOrderedRealPdu()
    {
        var one = Leaf(0, "Close [10]");
        var two = Leaf(1, "Good");
        var valid = Frame(leaves: [one, two]);
        Assert.Equal(2, IedBrowserGooseInspection.OrderedWireEntries(valid)?.Count);
        Assert.Null(IedBrowserGooseInspection.OrderedWireEntries(
            valid with { WireDataSetEntryCount = 3 }));
        Assert.Null(IedBrowserGooseInspection.OrderedWireEntries(
            valid with { Leaves = [two, two] }));
        Assert.Null(IedBrowserGooseInspection.OrderedWireEntries(
            valid with { Leaves = [one, two with { Value = "<missing in frame>" }] }));
        Assert.Null(IedBrowserGooseInspection.OrderedWireEntries(
            valid with { Leaves = [] }));
    }

    private static LiveIedControlBlockModel Sgcb(
        string status = "AttributeInventoryOnly",
        string domain = "BCUGEF650", string fc = "SP")
        => new()
        {
            Name = "SGCB", Kind = "SettingGroupControl",
            Domain = domain, LogicalNode = "LLN0",
            Reference = domain + "/LLN0." + fc + ".SGCB",
            FunctionalConstraint = fc, DiscoveryStatus = status,
            Attributes = ["NumOfSG", "ActSG", "LActTm", "EditSG", "CnfEdit"]
        };

    private static LiveIedModelDiscoveryDocument Model(
        params LiveIedControlBlockModel[] blocks)
        => new()
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "BCUGEF650"
            }],
            SettingGroupControls = blocks
        };

    [Fact]
    public void SGCBStatusIsHydratedFromExactLiveInventoryWithoutWritingEditGroup()
    {
        var ledger = new IedBrowserLiveValues();
        var token = ledger.BeginAssociation();
        var targets = IedBrowserReadPlan.BuildInitialPriority(Model(Sgcb()), ledger, 32);
        Assert.Equal(3, targets.Count);
        Assert.Equal(["ActSG", "LActTm", "NumOfSG"],
            targets.Select(target => target.Reference.Split('.')[^1]));
        Assert.All(targets, target => Assert.Equal("SP", target.FunctionalConstraint));
        Assert.DoesNotContain(targets, target =>
            target.Reference.Contains("EditSG") || target.Reference.Contains("CnfEdit"));
        ledger.PublishForAssociation(token, "BCUGEF650/LLN0.SGCB.ActSG",
            "SP", "1", "", "", "MMS Read");
        Assert.Equal(2, IedBrowserReadPlan.BuildInitialPriority(
            Model(Sgcb()), ledger, 32).Count);
        Assert.Single(IedBrowserReadPlan.BuildInitialPriority(Model(Sgcb()), ledger, 1));
    }

    [Fact]
    public void SGCBFailsClosedForSclOnlyUnknownFcOrWrongDomain()
    {
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        Assert.Empty(IedBrowserReadPlan.BuildInitialPriority(Model(Sgcb("SCL")), ledger, 32));
        var wrong = Sgcb(domain: "OTHER");
        Assert.Empty(IedBrowserReadPlan.BuildInitialPriority(Model(wrong), ledger, 32));
        var invalid = Sgcb(fc: "CF");
        Assert.Empty(IedBrowserReadPlan.BuildInitialPriority(Model(invalid), ledger, 32));
        ledger.MarkOffline();
        Assert.Empty(IedBrowserReadPlan.BuildInitialPriority(Model(Sgcb()), ledger, 32));
    }

    [Fact]
    public void TriggerAndOptionalFlagsAreChildrenOfReportControlSection()
    {
        var source = Find("Models/IedBrowserPresentation.cs");
        var rcb = source.IndexOf("case RcbExportRow rcb:", StringComparison.Ordinal);
        var start = source.IndexOf("Section(\"Control Block attributes\");",
            rcb, StringComparison.Ordinal);
        var end = source.IndexOf("Section(\"Information received in last Report\");",
            start, StringComparison.Ordinal);
        Assert.True(rcb >= 0 && start > rcb && end > start);
        var section = source[start..end];
        Assert.Contains("new IedBrowserDetailRow(\"FlagGroup\", \"Trigger options\"", section);
        Assert.Contains("new IedBrowserDetailRow(\"FlagGroup\", \"Optional fields\"", section);
        Assert.DoesNotContain("Section(\"Trigger options\")", section);
        Assert.DoesNotContain("Section(\"Optional fields\")", section);
        var projected = new IedBrowserDetailProjection();
        projected.Reset([
            new IedBrowserDetailRow("Section", "Control Block attributes"),
            new IedBrowserDetailRow("Property", "Enabled / RptEna", "true"),
            new IedBrowserDetailRow("FlagGroup", "Trigger options"),
            new IedBrowserDetailRow("FlagGroup", "Optional fields"),
            new IedBrowserDetailRow("Section", "Information received in last Report"),
            new IedBrowserDetailRow("Property", "Last Report status", "Received")]);
        var parent = projected.Rows[0];
        Assert.Equal(3, parent.GetChildren().Count);
        projected.Toggle(parent);
        Assert.DoesNotContain(projected.Rows, x => x.Kind == "FlagGroup");
        Assert.Contains(projected.Rows, x => x.Name == "Last Report status");
    }

    [Fact]
    public void PassiveSubscribeAndRcbGlyphReuseExistingRuntimeAndAreAssociationFenced()
    {
        var browser = Find("MainWindow.IedBrowser.cs");
        var goose = Find("MainWindow.IedGooseQuickStart.cs");
        Assert.Contains("OpenIedGooseSubscriber(device);", browser);
        Assert.Contains("StartGooseSubscriber_Click(this, new RoutedEventArgs());", goose);
        Assert.Contains("sourceDevice.LiveValues.Association != checkAssociation", browser);
        Assert.Contains("ReferenceEquals(sourceDevice.LiveDiscoveryModel, checkModel)", browser);
        Assert.Contains("_iedBrowserCallerRcbSignature", browser);
        Assert.DoesNotContain("_iedBrowserSelectedDoReference", browser);
        Assert.Contains("IedBrowserReadPlan.Build(model, currentPage, readAll)", browser);
        Assert.Contains("GetActiveBrowserRcbReferences(device.DeviceId)", browser);
        var xaml = Find("MainWindow.xaml");
        Assert.Contains("IedBrowserGooseSubscribeButton", xaml);
        Assert.Contains("Margin=\"9,0,0,0\"", xaml);
    }

    private static string Find(string path)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root != null)
        {
            var file = Path.Combine(root.FullName, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(file)) return File.ReadAllText(file);
            root = root.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
