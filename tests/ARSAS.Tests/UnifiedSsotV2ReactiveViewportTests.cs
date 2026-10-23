using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// No network or WPF dispatcher required. Both consumer cursors see the same
/// atomic canonical evidence, while unrelated page row objects are untouched.
/// </summary>
public sealed class UnifiedSsotV2ReactiveViewportTests
{
    private const string Current = "SG7LD/MMXU1.A.phsA.cVal.mag.f";
    private const string SameDoQ = "SG7LD/MMXU1.A.phsA.q";
    private const string OtherDo = "SG7LD/MMXU1.Hz.mag.f";
    private const string OtherLn = "SG7LD/XCBR1.Pos.stVal";

    private static IedBrowserDetailRow Row(string reference, string fc = "MX")
        => new("Data", reference, "-") { ValueReference = reference, FunctionalConstraint = fc };

    [Fact]
    public void OnlyChangedDataObjectScopeUpdatesItsCurrentPageRows()
    {
        var a = Row(Current);
        var q = Row(SameDoQ);
        var hz = Row(OtherDo);
        var status = Row(OtherLn, "ST");
        var index = new CanonicalBrowserViewportIndex([a, q, hz, status]);

        var store = new IedBrowserLiveValues();
        var association = store.BeginAssociation();
        var before = store.Version;
        Assert.True(store.PublishForAssociation(association, Current, "MX",
            "123.4", "Good", "12:30", "Report"));
        var batch = store.ReadChanges(before);
        Assert.True(index.TryFindDirtyRows(batch, store.Version, out var dirty));
        Assert.Equal(2, dirty.Count);
        Assert.Contains(a, dirty);
        Assert.Contains(q, dirty); // typed q/t siblings within the same IEC DO
        Assert.DoesNotContain(hz, dirty);
        Assert.DoesNotContain(status, dirty);
        Assert.True(store.TryGet(Current, "MX", out var canonical));
        Assert.Equal("123.4", canonical.Value);
    }

    [Fact]
    public void OffscreenReportChanges_DoNotTouchUnrelatedDetailRows()
    {
        var rendered = Row(Current);
        var index = new CanonicalBrowserViewportIndex([rendered]);
        var store = new IedBrowserLiveValues();
        var association = store.BeginAssociation();
        var before = store.Version;

        Assert.True(store.PublishForAssociation(association, OtherLn, "ST",
            "true", "Good", "", "Report"));
        Assert.True(index.TryFindDirtyRows(store.ReadChanges(before), store.Version,
            out var dirty));
        Assert.Empty(dirty); // existing Browser loop skips all presentation setters
        Assert.Equal("-", rendered.Value);
        Assert.True(store.TryGet(OtherLn, "ST", out var canonical));
        Assert.Equal("true", canonical.Value);
    }

    [Fact]
    public void ExplicitVectorAndAggregateDependencies_RefreshSelectedRow()
    {
        var composite = new IedBrowserDetailRow("Data", "A vector")
        {
            ValueReference = "SG7LD/MMXU1.A",
            PhasePlans = [new IedBrowserPolarPlan(Current,
                "SG7LD/MMXU1.A.phsA.cVal.ang.f", "MX")],
            ScalarComponents =
            [
                new IedBrowserScalarComponent("stVal",
                    "SG7LD/XCBR1.Pos.stVal", "ST")
            ]
        };
        var index = new CanonicalBrowserViewportIndex([composite]);
        var store = new IedBrowserLiveValues();
        var association = store.BeginAssociation();
        var cursor = store.Version;

        Assert.True(store.PublishForAssociation(association, OtherLn, "ST",
            "1", "", "", "Report"));
        Assert.True(index.TryFindDirtyRows(store.ReadChanges(cursor), store.Version,
            out var dirty));
        Assert.Single(dirty);
        Assert.Contains(composite, dirty);
    }

    [Fact]
    public void AssociationResetAndConfigurationMetadata_AlwaysForceFullProjection()
    {
        var row = Row(Current);
        var index = new CanonicalBrowserViewportIndex([row]);
        var store = new IedBrowserLiveValues();
        var assoc = store.BeginAssociation();
        var cursor = store.Version;

        Assert.True(store.PublishForAssociation(assoc,
            "SG7LD/MMXU1.A.phsA.units.SIUnit", "CF",
            "A", "", "", "MMS Read"));
        Assert.False(index.TryFindDirtyRows(store.ReadChanges(cursor),
            store.Version, out _));
        cursor = store.Version;

        store.MarkOffline();
        Assert.False(index.TryFindDirtyRows(store.ReadChanges(cursor),
            store.Version, out _));
        store.BeginAssociation();
        Assert.False(index.TryFindDirtyRows(store.ReadChanges(cursor),
            store.Version, out _));
    }

    [Fact]
    public void MissingCursorPageOrJournalOverflow_FallsBackToOriginalProjection()
    {
        var row = Row(Current);
        var index = new CanonicalBrowserViewportIndex([row]);
        var store = new IedBrowserLiveValues();
        var association = store.BeginAssociation();
        var old = store.Version;
        for (var i = 0; i < 4200; ++i)
            Assert.True(store.PublishForAssociation(association, OtherDo,
                "MX", i.ToString(), "", "", "Report"));
        Assert.False(index.TryFindDirtyRows(store.ReadChanges(old),
            store.Version, out _));
        Assert.False(index.TryFindDirtyRows(store.ReadChanges(store.Version),
            store.Version - 1, out _));
        // Unbound opaque rows cannot be proven irrelevant.
        var opaque = new CanonicalBrowserViewportIndex(
            [new IedBrowserDetailRow("Data", "Vendor computed field")]);
        Assert.False(opaque.TryFindDirtyRows(store.ReadChanges(store.Version),
            store.Version, out _));
    }

    [Fact]
    public void WpfIntegration_RemainsPageScopedAndNeverWritesCanonicalOrCommand()
    {
        var browser = Read("MainWindow.IedBrowser.cs");
        var adapter = Read("MainWindow.IedBrowser.ReactiveSsot.cs");
        Assert.Contains("SelectIedBrowserReactiveRows(device, version)", browser);
        Assert.Contains("reactiveRows is not null && !reactiveRows.Contains(row)", browser);
        Assert.Contains("RebuildIedBrowserViewportIndex()", browser);
        Assert.Contains("device.LiveValues.ReadChanges(", adapter);
        Assert.Contains("maxChanges: 4096", adapter);
        Assert.Contains("_iedBrowserRenderedPage?.Kind is \"RCB\" or \"GoCB\" or \"SGCB\"", adapter);
        Assert.DoesNotContain("PublishForAssociation(", adapter);
        Assert.DoesNotContain("MmsWrite", adapter);
        Assert.DoesNotContain("StartMonitoringAsync", adapter);
    }

    private static string Read(string path)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory);
             dir is not null; dir = dir.Parent)
        {
            var full = Path.Combine(dir.FullName, path);
            if (File.Exists(full)) return File.ReadAllText(full);
        }
        throw new FileNotFoundException(path);
    }
}
