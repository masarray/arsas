using System.Windows;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P0BrowserRcbEnabledTruthTests
{
    private const string Root = "BCUGEF650/LLN0.RP.URCB_A";
    private const string Other = "BCUGEF650/LLN0.RP.URCB_B";

    private static LiveIedModelDiscoveryDocument Model(params string[] references) => new()
    {
        ReportControls = references.Select(reference => new LiveIedReportControlModel
        {
            Reference = reference,
            Name = reference[(reference.LastIndexOf('.') + 1)..]
        }).ToArray()
    };

    private static RcbExportRow Rcb(string reference = Root, bool buffered = false) => new()
    {
        Reference = reference,
        Name = reference[(reference.LastIndexOf('.') + 1)..],
        Buffered = buffered,
        Availability = MmsRcbOperationalAvailability.UsedByCaller,
        StatusText = "ARSAS active"
    };

    [Fact]
    public void OnlyExactCurrentAssociationTypedMmsReadTrueEnablesTheIcon()
    {
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        var targets = IedBrowserRcbEnabledPresentation.VerifiedTargets(Model(Root, Other));
        var row = Rcb();

        Assert.False(IedBrowserRcbEnabledPresentation.TryVerifiedTrue(
            ledger, row, targets, true, out _));
        Assert.True(ledger.PublishForAssociation(association,
            Root + ".RptEna", "RP", "true", "", "", "MMS Read",
            typedBoolean: true));
        Assert.True(IedBrowserRcbEnabledPresentation.TryVerifiedTrue(
            ledger, row, targets, true, out var tooltip));
        Assert.Contains("RptEna: true", tooltip);
        Assert.Contains("verified MMS Read", tooltip);
        // A formatted string without original decoded Boolean-kind proof
        // cannot light the icon even when its text and source seem valid.
        var untyped = new IedBrowserLiveValues();
        var untypedAssociation = untyped.BeginAssociation();
        Assert.True(untyped.PublishForAssociation(untypedAssociation,
            Root + ".RptEna", "RP", "true", "", "", "MMS Read"));
        Assert.False(IedBrowserRcbEnabledPresentation.TryVerifiedTrue(
            untyped, row, targets, true, out _));
        // Once Stop/activation changes the RCB state, an older successful
        // read is only history; no new read-back means no live enabled icon.
        Assert.False(IedBrowserRcbEnabledPresentation.TryVerifiedTrue(
            ledger, row, targets, true, out _,
            minObservedUtc: DateTime.UtcNow.AddSeconds(1)));

        Assert.False(IedBrowserRcbEnabledPresentation.TryVerifiedTrue(
            ledger, Rcb(Other), targets, true, out _));
        Assert.False(IedBrowserRcbEnabledPresentation.TryVerifiedTrue(
            ledger, row, targets, false, out _));
        Assert.False(IedBrowserRcbEnabledPresentation.TryVerifiedTrue(
            ledger, Rcb(Root, buffered: true), targets, true, out _));
    }

    [Theory]
    [InlineData("true", "MMS Read")]
    [InlineData("false", "MMS Read")]
    [InlineData("1", "MMS Read")]
    [InlineData("true", "MMS Report")]
    [InlineData("true", "Snapshot")]
    [InlineData("[true]", "MMS Read")]
    public void FalseUntypedOrNonReadEvidenceHasNoIcon(string value, string source)
    {
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        var published = ledger.PublishForAssociation(association,
            Root + ".RptEna", "RP", value, "", "", source);
        // A rendered pseudo-array is rejected by the canonical scalar
        // publication fence itself; it never becomes live Boolean evidence.
        Assert.Equal(value != "[true]", published);

        Assert.False(IedBrowserRcbEnabledPresentation.TryVerifiedTrue(
            ledger, Rcb(), IedBrowserRcbEnabledPresentation.VerifiedTargets(Model(Root)),
            true, out var tooltip));
        Assert.Empty(tooltip);
    }

    [Fact]
    public void ReconnectStopAndModelAmbiguityFailClosed()
    {
        var ledger = new IedBrowserLiveValues();
        var old = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(old,
            Root + ".RptEna", "RP", "true", "", "", "MMS Read",
            typedBoolean: true));
        var targets = IedBrowserRcbEnabledPresentation.VerifiedTargets(Model(Root));
        Assert.True(IedBrowserRcbEnabledPresentation.TryVerifiedTrue(
            ledger, Rcb(), targets, true, out _));

        ledger.MarkOffline();
        Assert.False(IedBrowserRcbEnabledPresentation.TryVerifiedTrue(
            ledger, Rcb(), targets, true, out _));
        ledger.BeginAssociation();
        Assert.False(ledger.PublishForAssociation(old,
            Root + ".RptEna", "RP", "true", "", "", "MMS Read",
            typedBoolean: true));
        Assert.False(IedBrowserRcbEnabledPresentation.TryVerifiedTrue(
            ledger, Rcb(), targets, true, out _));

        var ambiguous = IedBrowserRcbEnabledPresentation.VerifiedTargets(Model(Root, Root));
        Assert.Empty(ambiguous);
        Assert.Empty(IedBrowserRcbEnabledPresentation.VerifiedTargets(null));
    }

    [Fact]
    public void VerifiedRcbTargets_CacheIsModelScopedAndCannotBeMutatedByConsumer()
    {
        var one = Model(Root, Other);
        var first = IedBrowserRcbEnabledPresentation.VerifiedTargets(one);
        for (var tick = 0; tick < 100; tick++)
            Assert.Same(first, IedBrowserRcbEnabledPresentation.VerifiedTargets(one));

        var replacement = Model(Root, Other);
        var second = IedBrowserRcbEnabledPresentation.VerifiedTargets(replacement);
        Assert.NotSame(first, second);
        Assert.Equal(first.Keys.Order(), second.Keys.Order());
        Assert.Equal(2, first.Count);
        Assert.False(first is Dictionary<string, IedBrowserReadTarget>);

        // Duplicated vendor RCB references are still not granted enablement evidence.
        var duplicates = Model(Root, Root);
        Assert.Empty(IedBrowserRcbEnabledPresentation.VerifiedTargets(duplicates));
    }

    [Fact]
    public void VirtualTreeStartsWithExactlySixCollapsedCategories()
    {
        var rows = IedBrowserPresentation.Roots(null, [Rcb()]);
        var tree = new IedBrowserTreeProjection();
        tree.Reset(rows);
        Assert.Equal(new[] { "Reports", "DataSets", "Data Model", "Files",
            "Setting Groups", "GOOSE" }, tree.Rows.Select(x => x.Title));
        Assert.All(tree.Rows, x => Assert.False(x.IsExpanded));

        var reports = Assert.Single(tree.Rows.Where(x => x.Key == "category/reports"));
        tree.Expand(reports);
        Assert.True(reports.IsExpanded);
        var expanded = tree.ExpandedKeys();
        tree.Reset(IedBrowserPresentation.Roots(null, [Rcb()]), expanded);
        Assert.True(tree.FindVisible("category/reports")?.IsExpanded == true);
        tree.Reset(IedBrowserPresentation.Roots(null, [Rcb()]));
        Assert.Equal(6, tree.Rows.Count);
        Assert.All(tree.Rows, x => Assert.False(x.IsExpanded));
    }

    [Fact]
    public void ExplicitBrowserReadForwardsDecodedBooleanEvidenceIntoAssociationLedger()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MainWindow.IedBrowser.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root!.FullName, "MainWindow.IedBrowser.cs"));
        // Regression guard: a read result with TypedBoolean=true must not be
        // reduced to a display string before the RptEna presentation selector.
        Assert.Matches(
            @"LiveValues\.PublishForAssociation\([\s\S]{0,420}typedBoolean: result\.TypedBoolean",
            source);
    }

    [Fact]
    public void XamlUsesOriginalSingleVectorInInlineFixedRcbNameSlot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MainWindow.xaml")))
            root = root.Parent;
        Assert.NotNull(root);
        var xaml = File.ReadAllText(Path.Combine(root!.FullName, "MainWindow.xaml"));
        var start = xaml.IndexOf("<DataTemplate x:Key=\"BrowserRcbObjectRow\">",
            StringComparison.Ordinal);
        var end = xaml.IndexOf("</DataTemplate>", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var rcb = xaml[start..end];
        Assert.Contains("<ColumnDefinition Width=\"200\"/>", rcb);
        Assert.Contains("<ColumnDefinition Width=\"24\"/>", rcb);
        Assert.Contains("Grid.Column=\"2\" Data=\"{StaticResource BrowserRcbEnabledIconGeometry}\"", rcb);
        Assert.Contains("Visibility=\"{Binding RcbEnabledVisibility}\"", rcb);
        Assert.DoesNotContain("RcbUsageGlyph", xaml);
        Assert.Contains("<Geometry x:Key=\"BrowserRcbEnabledIconGeometry\">M380-320h200", xaml);
    }
}
