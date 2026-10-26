using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class IedBrowserGooseInspectionTests
{
    private static IedBrowserGooseExpectedIdentity Expected()
        => new("BCUGE", "LD/LLN0.GO.BCU", "LD/LLN0.GOOSE1", "BCUGE03",
            33, 1, "", "", null);

    private static GooseStreamSnapshot Frame(string ied = "BCUGE",
        string gocb = "LD/LLN0$GO$BCU", string dataset = "LD/LLN0$GOOSE1",
        string appid = "0x0021", string revision = "1",
        IReadOnlyList<GooseLeafValueSnapshot>? leaves = null)
        => new("wire", appid, gocb, "BCUGE03", dataset,
            "02:00:4C:4F:5B:50", "01:0C:CD:01:00:33", "VID 0 / PCP 4",
            "1", "143", "Healthy", "20000 ms", revision, ied, "SCL",
            "", "12:00:00.000", 2, 0, false, false,
            leaves ?? Array.Empty<GooseLeafValueSnapshot>(),
            "0", "4", leaves?.Count ?? 0);

    [Fact]
    public void ExactCanonicalControlIdentityAndOnePublisherRequired()
    {
        var right = Frame();
        Assert.Same(right, IedBrowserGooseInspection.Select(Expected(), [right]));
        Assert.Null(IedBrowserGooseInspection.Select(Expected(), [Frame(ied: "OTHER")]));
        Assert.Null(IedBrowserGooseInspection.Select(Expected(), [Frame(gocb: "LD/LLN0$GO$BCU3")]));
        Assert.Null(IedBrowserGooseInspection.Select(Expected(), [Frame(dataset: "LD/LLN0$OTHER")]));
        Assert.Null(IedBrowserGooseInspection.Select(Expected(), [Frame(appid: "0x0022")]));
        Assert.Null(IedBrowserGooseInspection.Select(Expected(), [Frame(revision: "2")]));
        Assert.Null(IedBrowserGooseInspection.Select(Expected(), [right, Frame()]));
    }

    [Fact]
    public void UnboundAllDataRejectsIncompleteDuplicateOrMissingRealWireSlots()
    {
        var first = new GooseLeafValueSnapshot(1, 0, "wire[1]", "", "", "",
            "", "Closed [10]", "", false, "Unbound");
        var second = new GooseLeafValueSnapshot(2, 1, "wire[2]", "", "", "",
            "", "Good", "", false, "Unbound");
        var frame = Frame(leaves: [first, second]);
        var slots = IedBrowserGooseInspection.OrderedUnboundWireEntries(frame);
        Assert.NotNull(slots);
        Assert.Equal(2, slots.Count);
        Assert.Same(first, slots[0]);
        Assert.All(slots, slot => Assert.Empty(slot.SignalReference));
        Assert.All(slots, slot => Assert.Empty(slot.FunctionalConstraint));

        Assert.Null(IedBrowserGooseInspection.OrderedUnboundWireEntries(
            frame with { WireDataSetEntryCount = 3 }));
        Assert.Null(IedBrowserGooseInspection.OrderedUnboundWireEntries(
            frame with { Leaves = [second, first] }));
        Assert.Null(IedBrowserGooseInspection.OrderedUnboundWireEntries(
            frame with { Leaves = [first, first] }));
        Assert.Null(IedBrowserGooseInspection.OrderedUnboundWireEntries(
            frame with { Leaves = [first, second with { Order = 7 }] }));
        Assert.Null(IedBrowserGooseInspection.OrderedUnboundWireEntries(
            frame with { Leaves = [first, second with { Value = "<missing in frame>" }] }));
        Assert.Null(IedBrowserGooseInspection.OrderedUnboundWireEntries(
            frame with { Leaves = [first, second with { Value = "" }] }));
        Assert.Null(IedBrowserGooseInspection.OrderedUnboundWireEntries(
            frame with { Leaves = [] }));

        // The UI displays at most 128 entries; an invalid *129th* wire
        // slot must still invalidate the entire PDU, including the first 128.
        var large = Enumerable.Range(0, 129)
            .Select(i => new GooseLeafValueSnapshot(i + 1, i,
                $"wire[{i + 1}]", "", "", "", "", "Good", "", false, "Unbound"))
            .ToArray();
        Assert.Equal(129, IedBrowserGooseInspection.OrderedUnboundWireEntries(
            Frame(leaves: large))?.Count);
        large[128] = large[128] with { DataSetIndex = 127 };
        Assert.Null(IedBrowserGooseInspection.OrderedUnboundWireEntries(
            Frame(leaves: large)));
    }

    [Fact]
    public void ExactSclGoCbMetadata_ProjectsOnlyConfiguredVlanAndTimingFields()
    {
        var expected = Expected() with
        {
            SclVlanId = 0, SclVlanPriority = 4,
            SclMinTimeMs = 0, SclMaxTimeMs = 2000
        };
        var fields = IedBrowserGooseInspection.SclNetworkProperties(expected)
            .ToDictionary(field => field.Name, field => field.Value,
                StringComparer.Ordinal);
        Assert.Equal(4, fields.Count);
        Assert.Equal("0", fields["VLAN ID"]);
        Assert.Equal("4", fields["VLAN priority"]);
        Assert.Equal("0", fields["Minimum time (ms)"]);
        Assert.Equal("2000", fields["Maximum time (ms)"]);
        Assert.DoesNotContain("Enabled", fields.Keys);
        Assert.DoesNotContain("Capture status", fields.Keys);
    }

    [Fact]
    public void MissingSclGseAddressCannotFabricateNetworkState()
    {
        Assert.Empty(IedBrowserGooseInspection.SclNetworkProperties(Expected()));
    }

    [Fact]
    public void WireDataSetValuesOnlyIfWholeOrderedMemberIdentityMatches()
    {
        var first = new LiveIedDataSetMemberModel { Index = 0,
            Reference = "LD/CSWI6.Pos.stVal", FunctionalConstraint = "ST" };
        var second = new LiveIedDataSetMemberModel { Index = 1,
            Reference = "LD/CSWI6.Pos.q", FunctionalConstraint = "ST" };
        var set = new LiveIedDataSetModel { Reference = "LD/LLN0.GOOSE1",
            Members = [first, second] };
        var a = new GooseLeafValueSnapshot(1, 0, "Pos", first.Reference,
            "ST", "DPC", "Dbpos", "intermediate-state", "", false, "SCL");
        var b = new GooseLeafValueSnapshot(2, 1, "q", second.Reference,
            "ST", "DPC", "Quality", "good", "", false, "SCL");
        var frame = Frame(leaves: [a, b]);
        Assert.Equal("good", IedBrowserGooseInspection.VerifiedMembers(frame, set)?[1].Value);
        Assert.Null(IedBrowserGooseInspection.VerifiedMembers(frame with
            { Leaves = [b, a] }, set));
        Assert.Null(IedBrowserGooseInspection.VerifiedMembers(frame with
            { WireDataSetEntryCount = 1 }, set));
        Assert.Null(IedBrowserGooseInspection.VerifiedMembers(frame with
            { DataSetReference = "LD/LLN0$DIFFERENT" }, set));
        Assert.Null(IedBrowserGooseInspection.VerifiedMembers(frame with
            { Leaves = [a, b with { Order = 1 }] }, set));
        Assert.Null(IedBrowserGooseInspection.VerifiedMembers(frame with
            { Leaves = [a, b with { FunctionalConstraint = "MX" }] }, set));
        var nonContiguous = new LiveIedDataSetModel
        {
            Reference = set.Reference,
            Members =
            [
                new LiveIedDataSetMemberModel
                {
                    Index = 1, Reference = first.Reference, FunctionalConstraint = "ST"
                },
                new LiveIedDataSetMemberModel
                {
                    Index = 2, Reference = second.Reference, FunctionalConstraint = "ST"
                }
            ]
        };
        Assert.Null(IedBrowserGooseInspection.VerifiedMembers(frame, nonContiguous));
        var ambiguous = new LiveIedDataSetModel { Reference = set.Reference,
            Members = [first, first] };
        Assert.Null(IedBrowserGooseInspection.VerifiedMembers(frame, ambiguous));
    }
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(-1, 1000, false)]
    [InlineData(0, 1000, true)]
    [InlineData(999, 1000, true)]
    [InlineData(1000, 1000, false)]
    [InlineData(1001, 1000, false)]
    public void BrowserOnlyAdmitsGooseFramesInsideReceivedTimeAllowedToLive(
        int ageMilliseconds, long ttlMilliseconds, bool current)
    {
        var now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        var sample = now.AddMilliseconds(-ageMilliseconds);
        Assert.Equal(current, IedBrowserGooseInspection.IsFrameCurrent(
            sample, ttlMilliseconds, now));
    }

    [Fact]
    public void HugeTtlAndClockSkewDoNotOverflowOrCreateFutureEvidence()
    {
        var now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        Assert.True(IedBrowserGooseInspection.IsFrameCurrent(
            now.AddDays(-1), (long)uint.MaxValue, now));
        Assert.False(IedBrowserGooseInspection.IsFrameCurrent(
            now.AddMilliseconds(1), 20000, now));
        Assert.False(IedBrowserGooseInspection.IsFrameCurrent(
            now, -1, now));
    }

    [Fact]
    public void BrowserRechecksGooseTtlOnUiTickWithoutReadingMms()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "MainWindow.IedBrowser.cs")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory!.FullName,
            "MainWindow.IedBrowser.cs"));
        Assert.Contains("IedBrowserGooseInspection.IsFrameCurrent(", source);
        Assert.Contains("GOOSE frame expired · no current live value", source);
        Assert.Contains("if (_iedBrowserRenderedPage?.Kind == \"GoCB\")", source);
        Assert.Contains("UpdateIedBrowserGooseDetails();", source);
    }


    [Fact]
    public void CopyDiagnosticsReusesOnlyExistingSubscriberFrameIndex()
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        while (folder is not null &&
               !File.Exists(Path.Combine(folder.FullName, "MainWindow.xaml.cs")))
            folder = folder.Parent;
        Assert.NotNull(folder);
        var main = File.ReadAllText(Path.Combine(folder!.FullName, "MainWindow.xaml.cs"));
        var browser = File.ReadAllText(Path.Combine(folder.FullName, "MainWindow.IedBrowser.cs"));
        Assert.Contains("CaptureIedBrowserGooseEvidence()", main);
        Assert.Contains("GOOSE BROWSER ACCEPTANCE EVIDENCE (passive, non-MMS)", browser);
        Assert.Contains("_latestGooseFrames.Values", browser);
        Assert.Contains("TTL-expired-or-invalid", browser);
        Assert.Contains("identity-or-model-binding-mismatch", browser);
        Assert.Contains("ordered-member-directory-not-verified", browser);
        Assert.Contains("var browserGooseEvidence = CaptureIedBrowserGooseEvidence();", main);
        Assert.Contains("report += browserGooseEvidence;", main);
    }


    [Fact]
    public void FullyQualifiedWireGoCbCanBeObservedWithoutMmsDataSetDirectory()
    {
        var expected = Expected() with { DataSetReference = "", GoId = "",
            AppId = null, ConfRev = null };
        var raw = Frame() with { ModelIedName = "", BindingSource = "Unbound" };
        Assert.Same(raw, IedBrowserGooseInspection.Select(expected, [raw]));
        Assert.Null(IedBrowserGooseInspection.Select(expected,
            [raw with { GoCbRef = "LD/LLN0$GO$DIFFERENT" }]));
        Assert.Null(IedBrowserGooseInspection.Select(expected, [raw, raw with
            { StreamKey = "other-publisher" }]));
        Assert.Null(IedBrowserGooseInspection.Select(
            expected with { GoCbRef = "BCU" }, [raw]));
        Assert.Null(IedBrowserGooseInspection.Select(
            expected with { DataSetReference = "LD/LLN0.OTHER" }, [raw]));
        Assert.Null(IedBrowserGooseInspection.Select(
            expected, [raw with { ModelIedName = "OTHER" }]));
    }

    [Fact]
    public void BrowserSubscribeIsInlineAndSharesOriginalNpcapStartStop()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "MainWindow.xaml")))
            root = root.Parent;
        Assert.NotNull(root);
        var xaml = File.ReadAllText(Path.Combine(root!.FullName, "MainWindow.xaml"));
        var src = File.ReadAllText(Path.Combine(root.FullName, "MainWindow.IedBrowser.cs"));
        Assert.Contains("IedBrowserGooseAdapterCombo", xaml);
        Assert.Contains("IedBrowserGooseStopButton", xaml);
        Assert.Contains("StartGooseSubscriber_Click(sender, e);", src);
        Assert.Contains("await StopGooseSubscriberAsync();", src);
        var offset = src.IndexOf("private void IedBrowserGooseSubscribe_Click(", StringComparison.Ordinal);
        var end = src.IndexOf("private async void IedBrowserGooseStop_Click(", offset, StringComparison.Ordinal);
        Assert.True(offset >= 0 && end > offset);
        Assert.DoesNotContain("OpenIedGooseSubscriber(", src[offset..end]);
        Assert.DoesNotContain("MainTabs.SelectedIndex =", src[offset..end]);
        Assert.DoesNotContain("new GooseSubscriberRuntime(", src[offset..end]);
    }


    [Fact]
    public void WireDataSetDirectoryUsesExactDomainAndReferenceNotGoId()
    {
        var live = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel { MmsDomain = "LD" }],
            DataSets = [new LiveIedDataSetModel
            {
                Domain = "LD", Reference = "LD/LLN0.GOOSE2",
                Members = [new LiveIedDataSetMemberModel
                {
                    Index = 0, Reference = "LD/CSWI6.Pos.stVal",
                    FunctionalConstraint = "ST"
                }]
            }]
        };
        var exact = IedBrowserGooseDirectory.ResolveWireDataSet(
            live, null, "LD", "LD/LLN0$GOOSE2");
        Assert.NotNull(exact);
        Assert.Same(live.DataSets[0], exact.Value.Set);
        Assert.Null(IedBrowserGooseDirectory.ResolveWireDataSet(
            live, null, "LD", "LD/LLN0.GOOSE1"));
        Assert.Null(IedBrowserGooseDirectory.ResolveWireDataSet(
            live, null, "OTHER", "LD/LLN0.GOOSE2"));
    }

    [Fact]
    public void BrowserHasUnboundWireAllDataFallbackWithoutRewritingLedger()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root != null &&
               !File.Exists(Path.Combine(root.FullName, "MainWindow.IedBrowser.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root!.FullName,
            "MainWindow.IedBrowser.cs"));
        Assert.Contains("ResolveWireDataSet(", source);
        Assert.Contains("allData[{slot + 1}] · unbound", source);
        Assert.Contains("IedBrowserGooseInspection.OrderedUnboundWireEntries(frameView)", source);
        Assert.Contains("GOOSE raw allData · unbound", source);
        Assert.Contains("Not current", source);
        Assert.Contains("Received GOOSE PDU datSet", source);
        Assert.DoesNotContain("device.LiveValues.PublishForAssociation(", source);
    }


}
