using AR.Iec61850.FaultRecords;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M214RioPinnedEngineCompatibilityTests
{
    private static MmsFileDirectoryEntry File(string path, uint size = 11) =>
        new() { Name = path.Replace('\\', '/').Split('/').Last(),
                Path = path, SizeBytes = size };

    private static Iec61850FaultRecordCatalog Catalog(params MmsFileDirectoryEntry[] entries)
        => Iec61850FaultRecordCatalogBuilder.Build(entries);

    [Fact]
    public void ExactRootRio_IsSingleComtradeDisplayRow_AndOneCompleteDownloadPlan()
    {
        var root = new[] {
            File("line_1.cfg", 10), File("line_1.dat", 20),
            File("line_1.RIO", 8), File("line_2.rio", 7)
        };
        var original = Catalog(root);
        var originalRecord = Assert.Single(original.Records);
        Assert.Equal(3, originalRecord.Files.Count); // staged engine already owns exact root RIO
        var enriched = IedBrowserRioSidecarBridge.Attach(original, root);
        var index = new IedBrowserFileCatalog(enriched, root);
        var bundle = Assert.Single(index.DisplayRows, row => row.IsBundle);
        Assert.Equal("line_1", bundle.Name);
        Assert.Equal(3, bundle.BundleMemberCount);
        Assert.Equal(38L, bundle.SizeBytes);
        Assert.True(index.TryGetRecord(bundle.RemotePath, out var record, out _));
        Assert.True(record.IsComplete);
        Assert.Equal(3, record.Files.Count);
        Assert.Contains(record.Files, f => f.Extension == ".rio" &&
            f.RemotePath == "line_1.RIO" && f.Name == "line_1.RIO");
        Assert.DoesNotContain(index.DisplayRows, row => row.Name == "line_1.RIO");
        var orphan = Assert.Single(index.DisplayRows, row => row.Name == "line_2.rio");
        Assert.False(orphan.IsBundle);
        Assert.False(index.TryGetRecord(orphan.RemotePath, out _, out _));
        Assert.Equal(3, originalRecord.Files.Count); // no mutation of ARIEC source
        Assert.Same(original, enriched); // bridge must be idempotent with native RIO support
    }

    [Fact]
    public void AmbiguousOrIncorrectRootRio_FailsClosedRatherThanPickingFirst()
    {
        var cfg = File("line.cfg"); var dat = File("line.dat");
        var engine = Catalog(cfg, dat);
        var variants = new[]
        {
            new[] { cfg, dat, File("line.rio"), File("line.RIO") }, // duplicate aliases
            new[] { cfg, dat, File("other/line.rio") }, // wrong directory
            new[] { cfg, dat, File("LINE.RIO") }, // different exact basename
            new[] { cfg, dat, File("line2.rio") } // sibling
        };
        foreach (var root in variants)
        {
            var catalog = IedBrowserRioSidecarBridge.Attach(engine, root);
            var record = Assert.Single(catalog.Records);
            Assert.Equal(2, record.Files.Count);
        }
    }

    [Fact]
    public void IncompleteCfgAndRio_DoesNotGrantMmsDownloadAuthority()
    {
        var root = new[] { File("partial.cfg"), File("partial.rio") };
        var engine = Catalog(root);
        var supplemented = IedBrowserRioSidecarBridge.Attach(engine, root);
        var index = new IedBrowserFileCatalog(supplemented, root);
        var bundle = Assert.Single(index.DisplayRows, row => row.IsBundle);
        Assert.Equal(2, bundle.BundleMemberCount); // engine exposes CFG+RIO, still no DAT
        Assert.False(index.TryGetRecord(bundle.RemotePath, out _, out _));
        Assert.Contains(index.Files, row => row.Name == "partial.rio");
        Assert.False(index.TryGetRecord("partial.rio", out _, out _));
        // One incomplete bundle, no duplicate RIO row / false download authority.
        Assert.Single(index.DisplayRows);
    }

    [Fact]
    public async Task RootFirstDiscovery_ProjectsRioBeforeAnyRecursiveScan()
    {
        var root = new[] { File("fault.cfg"), File("fault.dat"), File("fault.RIO") };
        IedBrowserFileCatalog? early = null;
        var result = await IedBrowserFileDiscoveryPlanner.DiscoverAsync(
            _ => Task.FromResult<IReadOnlyList<MmsFileDirectoryEntry>>(root),
            _ => throw new InvalidOperationException("No folder, recursive scan forbidden"),
            CancellationToken.None,
            catalog => { early = catalog; return Task.CompletedTask; });
        Assert.NotNull(early);
        Assert.Equal(3, Assert.Single(early.DisplayRows).BundleMemberCount);
        Assert.Equal(3, Assert.Single(result.Files.DisplayRows).BundleMemberCount);
        Assert.Null(result.CatalogWarning);
    }

    [Fact]
    public void CacheAndFreshDownloadValidation_RequireRioAndMatchingDeclaredSize()
    {
        var rootFiles = new[] {
            File("event.cfg", 4), File("event.dat", 5), File("event.RIO", 7)
        };
        var record = Assert.Single(IedBrowserRioSidecarBridge.Attach(
            Catalog(rootFiles), rootFiles).Records);
        var root = Path.Combine(Path.GetTempPath(), "arsas-rio-compat-" +
            Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "event");
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var file in record.Files)
                System.IO.File.WriteAllBytes(Path.Combine(dir, file.Name),
                    new byte[checked((int)file.SizeBytes!.Value)]);
            Assert.True(IedBrowserLocalRecordEvidence.TryFindComplete(record, root, out _));
            var downloaded = record.Files.Select(file => new Iec61850FaultRecordDownloadedFile {
                RemotePath = file.RemotePath, LocalPath = Path.Combine(dir, file.Name),
                BytesTransferred = file.SizeBytes!.Value
            }).ToArray();
            var success = new Iec61850FaultRecordDownloadResult {
                IsSuccess = true, RecordId = record.RecordId,
                DestinationDirectory = dir, Files = downloaded
            };
            Assert.True(IedBrowserLocalRecordEvidence.ValidateFresh(record, success, out _));
            // No fabricated RIO timestamp: even a full local folder does not
            // authorize silent reuse under the prior session's IED identity.
            Assert.False(IedBrowserRecordProvenanceCache.TryFindVerified(record, root, out _));
            System.IO.File.Delete(Path.Combine(dir, "event.RIO"));
            Assert.False(IedBrowserLocalRecordEvidence.TryFindComplete(record, root, out _));
            Assert.False(IedBrowserLocalRecordEvidence.ValidateFresh(record, success, out _));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ModelOnlyRio_CannotBePromotedWithoutPhysicalRootDirectory()
    {
        var engine = Catalog(File("line.cfg"), File("line.dat"));
        var result = IedBrowserRioSidecarBridge.Attach(engine, Array.Empty<MmsFileDirectoryEntry>());
        Assert.Same(engine, result);
        Assert.Equal(2, Assert.Single(result.Records).Files.Count);
    }
}
