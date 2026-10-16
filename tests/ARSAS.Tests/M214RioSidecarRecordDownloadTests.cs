using AR.Iec61850.FaultRecords;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M214RioSidecarRecordDownloadTests
{
    private static MmsFileDirectoryEntry File(string name, uint size = 5)
        => new() { Name = name, Path = name, SizeBytes = size };

    [Fact]
    public void ExactEngineRioSidecar_IsOneBrowserRecordAndDownloadMember()
    {
        var raw = new[]
        {
            File("line_1.CFG"),
            File("line_1.DAT"),
            File("line_1.RIO"),
            File("line_2.RIO")
        };
        var catalog = new IedBrowserFileCatalog(
            Iec61850FaultRecordCatalogBuilder.Build(raw), raw);
        var bundled = Assert.Single(catalog.DisplayRows, row => row.IsBundle);
        Assert.Equal("line_1", bundled.Name);
        Assert.Equal(3, bundled.BundleMemberCount);
        Assert.Equal(15L, bundled.SizeBytes);
        Assert.True(catalog.TryGetRecord(bundled.RemotePath, out var record, out _));
        Assert.Contains(record.Files, f => f.Kind == Iec61850FaultRecordFileKind.RioSidecar
            && f.Name == "line_1.RIO");
        Assert.DoesNotContain(catalog.DisplayRows, row =>
            row.Name is "line_1.RIO" or "line_1.CFG" or "line_1.DAT");
        var orphan = Assert.Single(catalog.DisplayRows, row => row.Name == "line_2.RIO");
        Assert.False(orphan.IsBundle);
        Assert.False(catalog.TryGetRecord(orphan.RemotePath, out _, out _));
    }

    [Fact]
    public void LocalRecordIntegrity_RequiresRioBeforeOpeningDownloadedComtrade()
    {
        var raw = new[] { File("line_1.cfg"), File("line_1.dat"), File("line_1.rio") };
        var record = Assert.Single(Iec61850FaultRecordCatalogBuilder.Build(raw).Records);
        var root = Path.Combine(Path.GetTempPath(), "arsas-m214-rio-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "line_1");
        Directory.CreateDirectory(folder);
        try
        {
            foreach (var file in record.Files)
                System.IO.File.WriteAllBytes(Path.Combine(folder, file.Name), new byte[checked((int)file.SizeBytes!.Value)]);
            Assert.True(IedBrowserLocalRecordEvidence.TryFindComplete(record, root, out var path));
            Assert.Equal(folder, path);
            var fetched = record.Files.Select(file => new Iec61850FaultRecordDownloadedFile
            {
                RemotePath = file.RemotePath,
                LocalPath = Path.Combine(folder, file.Name),
                BytesTransferred = 5
            }).ToArray();
            var complete = new Iec61850FaultRecordDownloadResult
            {
                IsSuccess = true, RecordId = record.RecordId,
                DestinationDirectory = folder, Files = fetched
            };
            Assert.True(IedBrowserLocalRecordEvidence.ValidateFresh(record, complete, out _));
            System.IO.File.Delete(Path.Combine(folder, "line_1.rio"));
            Assert.False(IedBrowserLocalRecordEvidence.TryFindComplete(record, root, out _));
            Assert.False(IedBrowserLocalRecordEvidence.ValidateFresh(record, complete, out _));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void IncompleteCfgAndRio_CannotObtainDownloadAuthority()
    {
        var raw = new[] { File("line_3.cfg"), File("line_3.rio") };
        var catalog = new IedBrowserFileCatalog(
            Iec61850FaultRecordCatalogBuilder.Build(raw), raw);
        var row = Assert.Single(catalog.DisplayRows);
        Assert.Equal(2, row.BundleMemberCount);
        Assert.False(catalog.TryGetRecord(row.RemotePath, out _, out _));
    }
}
