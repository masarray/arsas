using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
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
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(csv))).ToLowerInvariant(),
            root.GetProperty("ReadEvidenceCsvSha256").GetString());
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
    public void ManifestHashMatchesArchivedPayload_AndDetectsModifiedCsv()
    {
        const string csv = "CanonicalReference,FC,LedgerState\r\n" +
            "LAB/LLN0.Mod.origin.orIdent,ST,OBSERVED\r\n";
        var archiveBytes = IedBrowserReadEvidenceBundle.Build(
            csv, "app", "engine", 7, DateTimeOffset.UnixEpoch);
        using var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
        using var csvStream = archive.GetEntry("read-evidence.csv")!.Open();
        using var payload = new MemoryStream();
        csvStream.CopyTo(payload);
        var actualHash = Convert.ToHexString(SHA256.HashData(payload.ToArray())).ToLowerInvariant();
        using var manifestStream = archive.GetEntry("manifest.json")!.Open();
        using var manifest = JsonDocument.Parse(manifestStream);
        var declaredHash = manifest.RootElement.GetProperty("ReadEvidenceCsvSha256").GetString();
        Assert.Equal(declaredHash, actualHash);
        Assert.NotEqual(declaredHash,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(csv + "tampered"))).ToLowerInvariant());
        Assert.Equal(1, manifest.RootElement.GetProperty("SelectedTargetRows").GetInt32());
    }

    [Fact]
    public void ManifestCountsCsvRecords_NotEmbeddedQuotedNewlinesOrEscapedQuotes()
    {
        const string csv = "CanonicalReference,FC,Reason\r\n" +
            "\"TESTLD/LLN0.Mod.origin.orIdent\",\"ST\",\"line 1\r\nline 2 with \"\"quotes\"\"\"\r\n" +
            "\"TESTLD/LLN0.NamPlt.swRev\",\"DC\",\"Not scheduled\"\r\n";
        var bundle = IedBrowserReadEvidenceBundle.Build(
            csv, "app", "engine", 9, DateTimeOffset.UnixEpoch);
        using var zip = new ZipArchive(new MemoryStream(bundle), ZipArchiveMode.Read);
        using var manifestStream = zip.GetEntry("manifest.json")!.Open();
        using var manifest = JsonDocument.Parse(manifestStream);
        Assert.Equal(2, manifest.RootElement.GetProperty("SelectedTargetRows").GetInt32());
    }

    [Fact]
    public void MalformedCsvOrUnboundedTargetCount_FailsClosedWithoutEvidencePacket()
    {
        static void Package(string csv) => IedBrowserReadEvidenceBundle.Build(
            csv, "app", "engine", 9, DateTimeOffset.UnixEpoch);

        Assert.Throws<FormatException>(() => Package("Header,Value\n\"unterminated"));
        Assert.Throws<FormatException>(() => Package("Header,Value\n\"quoted\"invalid\n"));
        Assert.Throws<FormatException>(() => Package("Header,Value\n\nvalue,3\n"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Package("Header\n" + string.Concat(Enumerable.Repeat("target\n", 4097))));
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
