using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P0DReadEvidenceBundleTests
{
    [Fact]
    public void OptInBundle_IsBounded_SelfIdentifying_AndDoesNotInventMmsProof()
    {
        const string csv = "CanonicalReference,FC,LedgerState\n" +
                           "\"TESTLD/LLN0.Mod.origin.orIdent\",\"ST\",\"OBSERVED\"\n" +
                           "\"TESTLD/LLN0.NamPlt.swRev\",\"DC\",\"DEFERRED\"\n";
        var packet = IedBrowserReadEvidenceBundle.Build(
            csv, "1.6.40+appsha", "1.6.40+enginesha", 4,
            new DateTimeOffset(2026, 10, 19, 12, 30, 0, TimeSpan.Zero));

        using var archive = new ZipArchive(new MemoryStream(packet), ZipArchiveMode.Read);
        Assert.Equal(2, archive.Entries.Count);
        var data = archive.GetEntry("read-evidence.csv");
        Assert.NotNull(data);
        using (var reader = new StreamReader(data!.Open(), Encoding.UTF8))
            Assert.Equal(csv, reader.ReadToEnd());

        var manifest = archive.GetEntry("manifest.json");
        Assert.NotNull(manifest);
        using var stream = manifest!.Open();
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal(2, root.GetProperty("SelectedTargetRows").GetInt32());
        Assert.Equal(4, root.GetProperty("Association").GetInt64());
        Assert.Equal("1.6.40+appsha", root.GetProperty("AppVersion").GetString());
        Assert.Equal("1.6.40+enginesha", root.GetProperty("EngineVersion").GetString());
        Assert.Equal("NOT_CAPTURED", root.GetProperty("WireInvokeStatus").GetString());
        Assert.Equal("NOT_CAPTURED", root.GetProperty("WireGvaStatus").GetString());
        Assert.Equal("UNVERIFIED", root.GetProperty("PhysicalAcceptance").GetString());
        Assert.Null(archive.GetEntry("credentials.json"));
        Assert.Null(archive.GetEntry("network.pcap"));
    }

    [Fact]
    public void OversizedOrNullEvidenceCannotProduceDiagnosticBundle()
    {
        Assert.Throws<ArgumentNullException>(() =>
            IedBrowserReadEvidenceBundle.Build(null!, "", "", 0, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            IedBrowserReadEvidenceBundle.Build(new string('x', 4_194_305),
                "", "", 0, DateTimeOffset.UtcNow));
    }
}
