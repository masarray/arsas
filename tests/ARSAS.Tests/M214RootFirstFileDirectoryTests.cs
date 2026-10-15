using AR.Iec61850.FaultRecords;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M214RootFirstFileDirectoryTests
{
    private static MmsFileDirectoryEntry File(string path, uint size)
        => new() { Path = path, Name = Path.GetFileName(path), SizeBytes = size };

    [Fact]
    public async Task RootFilesAppearWithoutWastingMmsScanWhenNoDirectoriesExist()
    {
        var scans = 0;
        var published = 0;
        var result = await IedBrowserFileDiscoveryPlanner.DiscoverAsync(
            _ => Task.FromResult<IReadOnlyList<MmsFileDirectoryEntry>>(
                [File("/config.xml", 128), File("/IED.CFG", 128),
                 File("/IED.DAT", 1024)]),
            _ => { scans++; return Task.FromResult(new Iec61850FaultRecordCatalog()); },
            CancellationToken.None,
            publishRoot: catalog =>
            {
                published++;
                Assert.Equal(3, catalog.Files.Count);
                return Task.CompletedTask;
            });
        Assert.Equal(0, scans);
        Assert.Equal(1, published);
        Assert.Equal(3, result.Files.Files.Count);
        Assert.Null(result.CatalogWarning);
        Assert.True(result.Files.TryGetRecord("/IED.CFG", out _, out _));
    }

    [Fact]
    public async Task RootEvidenceSurvivesSlowOrRejectedRecursiveCatalog()
    {
        var published = 0;
        var result = await IedBrowserFileDiscoveryPlanner.DiscoverAsync(
            _ => Task.FromResult<IReadOnlyList<MmsFileDirectoryEntry>>(
                [File("/notes.txt", 10), new MmsFileDirectoryEntry
                {
                    Path = "/RECORDS/", Name = "RECORDS/", SizeBytes = 0
                }]),
            ct => Task.FromCanceled<Iec61850FaultRecordCatalog>(
                new CancellationToken(true)),
            CancellationToken.None,
            publishRoot: root =>
            {
                published++;
                Assert.Equal(2, root.Files.Count);
                return Task.CompletedTask;
            },
            scanBudget: TimeSpan.FromMilliseconds(50));
        Assert.Equal(1, published);
        Assert.NotNull(result.CatalogWarning);
        Assert.Equal(2, result.Files.Files.Count);
        Assert.False(result.Files.TryGetRecord("/notes.txt", out _, out _));
    }

    [Fact]
    public async Task FailureOfRootStillAllowsExistingFaultRecordDiscoveryFallback()
    {
        var scanned = false;
        var result = await IedBrowserFileDiscoveryPlanner.DiscoverAsync(
            _ => throw new InvalidOperationException("MMS AccessDenied"),
            _ => { scanned = true; return Task.FromResult(new Iec61850FaultRecordCatalog()); },
            CancellationToken.None);
        Assert.True(scanned);
        Assert.Contains("AccessDenied", result.RootWarning);
        Assert.Empty(result.Files.Files);
    }

    [Fact]
    public async Task UserCancellationNeverPublishesRootOrTriggersRecordScan()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var scanned = false;
        var callback = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            IedBrowserFileDiscoveryPlanner.DiscoverAsync(
                _ => Task.FromResult<IReadOnlyList<MmsFileDirectoryEntry>>(
                    [File("/foo.bin", 1)]),
                _ => { scanned = true; return Task.FromResult(new Iec61850FaultRecordCatalog()); },
                cancelled.Token,
                _ => { callback = true; return Task.CompletedTask; }));
        Assert.False(scanned);
        Assert.False(callback);
    }

    [Fact]
    public async Task RootIsPublishedBeforeDeeperMmsTraversal()
    {
        var order = new List<string>();
        await IedBrowserFileDiscoveryPlanner.DiscoverAsync(
            _ =>
            {
                order.Add("root");
                return Task.FromResult<IReadOnlyList<MmsFileDirectoryEntry>>(
                    [new MmsFileDirectoryEntry { Path = "/dir/", Name = "dir/" }]);
            },
            _ =>
            {
                order.Add("catalog");
                return Task.FromResult(new Iec61850FaultRecordCatalog());
            },
            CancellationToken.None,
            _ =>
            {
                order.Add("publish");
                return Task.CompletedTask;
            });
        Assert.Equal(new[] { "root", "publish", "catalog" }, order);
    }
}
