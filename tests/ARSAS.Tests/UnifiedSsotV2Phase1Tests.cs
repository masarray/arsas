using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Foundation contracts for multiple workspaces observing one canonical value
/// owner and one immutable engineering schema. No physical IED is required.
/// </summary>
public sealed class UnifiedSsotV2Phase1Tests
{
    [Fact]
    public async Task ModelSchema_IsOneFlightAcrossBrowserAndReadBindings()
    {
        var one = new LiveIedModelDiscoveryDocument();
        var another = new LiveIedModelDiscoveryDocument();
        var builds = Enumerable.Range(0, 48)
            .Select(_ => SharedIedSchemaIndex.PrepareAsync(one))
            .ToArray();

        Assert.All(builds, build => Assert.Same(builds[0], build));
        var index = await builds[0];
        Assert.Same(one, index.SourceModel);
        Assert.True(SharedIedSchemaIndex.TryGetPrepared(one, out var prepared));
        Assert.Same(index, prepared);
        Assert.Same(index, SharedIedSchemaIndex.GetForBinding(one));
        Assert.NotSame(index, await SharedIedSchemaIndex.PrepareAsync(another));
        SharedIedSchemaIndex.ForgetFaulted(one);
        Assert.Same(index, await SharedIedSchemaIndex.PrepareAsync(one));
    }

    [Fact]
    public void CanonicalDirtyKeys_AreIndependentForTwoWorkspaceCursors()
    {
        var store = new IedBrowserLiveValues();
        var association = store.BeginAssociation();
        var initial = store.ReadChanges(0);
        Assert.True(initial.RequiresFullRefresh);
        Assert.Contains(initial.Changes, c =>
            c.Kind == CanonicalValueChangeKind.AssociationInvalidated);
        var cursor = store.Version;

        const string point = "SG7LD/MMXU1.A.phsA.cVal.mag.f";
        Assert.True(store.PublishForAssociation(association,
            point, "mx", "123.4", "Good", "12:01", "Report"));
        Assert.True(store.PublishForAssociation(association,
            "SG7LD/XCBR1.Pos.stVal", "ST", "true", "Good", "12:01", "Report"));
        Assert.True(store.PublishForAssociation(association,
            "SG7LD/LLN0.Mod.stVal", "ST", "1", "", "", "MMS Read"));

        // No drain: the same publication is independently readable by Browser
        // and Live Monitor without copying a second authoritative value store.
        var browser = store.ReadChanges(cursor, 2);
        var explorer = store.ReadChanges(cursor, 2);
        Assert.False(browser.RequiresFullRefresh);
        Assert.Equal(browser, explorer);
        Assert.Equal(2, browser.Changes.Count);
        Assert.Equal(cursor + 2, browser.ThroughRevision);
        Assert.Equal(store.Version, browser.LatestRevision);
        Assert.Equal(point, browser.Changes[0].Reference);
        Assert.Equal("MX", browser.Changes[0].FunctionalConstraint);
        Assert.Equal(CanonicalValueChangeKind.Report, browser.Changes[0].Kind);

        var continuation = store.ReadChanges(browser.ThroughRevision, 2);
        Assert.Single(continuation.Changes);
        Assert.Equal(CanonicalValueChangeKind.Read, continuation.Changes[0].Kind);
        Assert.True(store.TryGet(point, "MX", out var evidence));
        Assert.Equal("123.4", evidence.Value);
        Assert.Equal(association, evidence.Association);
    }

    [Fact]
    public void ChangeJournal_RejectsStaleAssociationAndSignalsReset()
    {
        var store = new IedBrowserLiveValues();
        var first = store.BeginAssociation();
        var cursor = store.Version;
        Assert.True(store.PublishForAssociation(first, "LD/XCBR1.Pos.stVal",
            "ST", "true", "", "", "Report"));
        store.MarkOffline();
        var delta = store.ReadChanges(cursor);
        Assert.True(delta.RequiresFullRefresh);
        Assert.Equal(CanonicalValueChangeKind.AssociationInvalidated,
            delta.Changes[^1].Kind);
        Assert.False(store.PublishForAssociation(first, "LD/XCBR1.Pos.stVal",
            "ST", "false", "", "", "Report"));
        var next = store.BeginAssociation();
        Assert.True(next > first);
        Assert.False(store.TryGet("LD/XCBR1.Pos.stVal", "ST", out _));
        Assert.True(store.ReadChanges(cursor).RequiresFullRefresh);
    }

    [Fact]
    public void BoundedJournal_DetectsMissedRevisionsRatherThanHidingChanges()
    {
        var store = new IedBrowserLiveValues();
        var association = store.BeginAssociation();
        var veryOldCursor = store.Version;
        for (var i = 0; i < 4300; i++)
            Assert.True(store.PublishForAssociation(association,
                "LD/MMXU1.A.phsA.cVal.mag.f", "MX", i.ToString(), "", "", "Report"));
        var result = store.ReadChanges(veryOldCursor, 32);
        Assert.True(result.RequiresFullRefresh);
        Assert.Empty(result.Changes);
        Assert.Equal(store.Version, result.ThroughRevision);
        Assert.Empty(store.ReadChanges(store.Version).Changes);
    }

    [Fact]
    public void ReadAndBrowser_UseSharedSchemaFactoryAndCanonicalMutationJournal()
    {
        var binding = Read("Models/IedBrowserReadBindingIndex.cs");
        var browser = Read("MainWindow.IedBrowser.Performance.cs");
        var ledger = Read("Models/IedBrowserLiveValues.cs");
        Assert.Contains("SharedIedSchemaIndex.GetForBinding(model)", binding);
        Assert.Contains("SharedIedSchemaIndex.PrepareAsync(model)", browser);
        Assert.DoesNotContain("new IedBrowserSchemaIndex(model)", binding);
        Assert.Contains("RecordChange(reference, fc, isReport", ledger);
        Assert.Contains("CanonicalValueChangeKind.ReportCompanion", ledger);
        Assert.Contains("CanonicalValueChangeKind.AssociationInvalidated", ledger);
    }

    private static string Read(string filename)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory);
             dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, filename);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(filename);
    }
}
