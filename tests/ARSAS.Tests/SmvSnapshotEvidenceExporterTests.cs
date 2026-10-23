using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SmvSnapshotEvidenceExporterTests
{
    private static readonly DateTimeOffset FixedGeneratedAt =
        new(2026, 7, 28, 4, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExportBundle_ContainsSevenAuditableEntriesAndRawSamples()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(temporaryDirectory, "evidence.zip");
            var result = await SmvSnapshotEvidenceExporter.ExportAsync(
                path,
                CreateSnapshot(),
                CreateContext(),
                CreatePngBytes());

            Assert.Equal(7, result.Entries.Count);
            Assert.Equal(64, result.BundleSha256.Length);

            using var archive = ZipFile.OpenRead(path);
            var names = archive.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            Assert.Equal(
                new[]
                {
                    "README.txt",
                    "SHA256SUMS.txt",
                    "diagnostics.txt",
                    "manifest.json",
                    "provenance.json",
                    "samples.csv",
                    "waveform.png"
                },
                names);

            var samples = ReadEntryText(archive, "samples.csv");
            Assert.Contains("sampleIndex,cyclePosition", samples, StringComparison.Ordinal);
            Assert.Contains("0,0,100,-100", samples, StringComparison.Ordinal);
            Assert.Contains("3,0.0375,400,-400", samples, StringComparison.Ordinal);

            using var manifest = JsonDocument.Parse(ReadEntryText(archive, "manifest.json"));
            Assert.Equal(SmvSnapshotEvidenceExporter.SchemaVersion, manifest.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal("PASS", manifest.RootElement.GetProperty("verdict").GetString());
            Assert.Equal("0x4000", manifest.RootElement.GetProperty("observedStream").GetProperty("appId").GetString());
            Assert.Equal(2, manifest.RootElement.GetProperty("payload").GetProperty("plottedLaneCount").GetInt32());

            ValidateChecksums(archive);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RestartVerdict_IsPersistedAsReview()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(temporaryDirectory, "restart.zip");
            await SmvSnapshotEvidenceExporter.ExportAsync(
                path,
                CreateSnapshot() with { RestartTransitions = 1 },
                CreateContext(),
                CreatePngBytes());

            using var archive = ZipFile.OpenRead(path);
            using var manifest = JsonDocument.Parse(ReadEntryText(archive, "manifest.json"));
            Assert.Equal("REVIEW", manifest.RootElement.GetProperty("verdict").GetString());
            Assert.Equal(1, manifest.RootElement.GetProperty("continuity").GetProperty("restartTransitions").GetInt32());
            Assert.Contains("Verdict: REVIEW", ReadEntryText(archive, "diagnostics.txt"), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task FixedEvidence_IsByteDeterministic()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var firstPath = Path.Combine(temporaryDirectory, "first.zip");
            var secondPath = Path.Combine(temporaryDirectory, "second.zip");
            var snapshot = CreateSnapshot();
            var context = CreateContext();
            var png = CreatePngBytes();

            var first = await SmvSnapshotEvidenceExporter.ExportAsync(firstPath, snapshot, context, png);
            var second = await SmvSnapshotEvidenceExporter.ExportAsync(secondPath, snapshot, context, png);

            Assert.Equal(first.BundleSha256, second.BundleSha256);
            Assert.Equal(await File.ReadAllBytesAsync(firstPath), await File.ReadAllBytesAsync(secondPath));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void SuggestedFileName_IsPortableAndTraceable()
    {
        var name = SmvSnapshotEvidenceExporter.BuildSuggestedFileName(
            CreateContext() with { SelectedStreamId = "MU/01:*?" },
            CreateSnapshot());

        Assert.StartsWith("ARSAS-SV-Evidence-", name, StringComparison.Ordinal);
        Assert.EndsWith("-20260728-043000Z.zip", name, StringComparison.Ordinal);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain(':', name);
        Assert.DoesNotContain('*', name);
        Assert.DoesNotContain('?', name);
    }

    private static SmvSnapshotResult CreateSnapshot()
        => new()
        {
            StartedAt = FixedGeneratedAt.AddMilliseconds(-40),
            CompletedAt = FixedGeneratedAt,
            AppId = 0x4000,
            SourceMac = "00-11-22-33-44-55",
            DestinationMac = "01-0C-CD-04-00-01",
            VlanText = "100",
            StreamId = "MU01",
            DataSetReference = "IED1LD0/LLN0$Dataset01",
            ConfigurationRevision = 7,
            SampleSynchronization = 2,
            DeclaredSampleRate = 4000,
            DeclaredSampleMode = 1,
            NominalFrequencyHz = 50,
            SamplesPerCycle = 80,
            CycleCount = 2,
            TargetSamples = 160,
            CapturedSamples = 160,
            CapturedFrames = 80,
            ParsedAsdus = 160,
            FirstSampleCount = 0,
            LastSampleCount = 159,
            ContinuousTransitions = 159,
            TimebaseReason = "Declared 4000 samples/s at explicit 50 Hz.",
            PayloadShape = "16 bytes · 2 raw 32-bit words",
            Channels = new[]
            {
                new SmvSnapshotChannel
                {
                    ChannelIndex = 1,
                    PayloadWordIndex = 0,
                    Label = "Raw word 1",
                    Interpretation = "Signed INT32 representation; channel/unit semantics unresolved",
                    Samples = new double[] { 100, 200, 300, 400 },
                    Minimum = 100,
                    Maximum = 400
                },
                new SmvSnapshotChannel
                {
                    ChannelIndex = 2,
                    PayloadWordIndex = 1,
                    Label = "Raw word 2",
                    Interpretation = "Signed INT32 representation; channel/unit semantics unresolved",
                    Samples = new double[] { -100, -200, -300, -400 },
                    Minimum = -400,
                    Maximum = -100
                }
            },
            Diagnostics = new[] { "Synthetic deterministic test evidence." }
        };

    private static SmvSnapshotEvidenceContext CreateContext()
        => new()
        {
            GeneratedAtUtc = FixedGeneratedAt,
            DeviceName = "Test IED",
            EndpointText = "192.0.2.10:102",
            AdapterDisplayText = "Test mirror adapter",
            ControlReference = "IED1LD0/LLN0$MSVCB01",
            SelectedStreamId = "MU01",
            SelectedDataSetReference = "IED1LD0/LLN0$Dataset01",
            SelectedAppId = "0x4000",
            SelectedDestinationMac = "01-0C-CD-04-00-01",
            ExplicitNominalFrequencyHz = 50,
            Provenance = new SmvSnapshotEvidenceProvenance
            {
                ApplicationVersion = "1.6.20",
                ApplicationInformationalVersion = "1.6.20+test",
                EngineRepository = "masarray/ARIEC61850",
                EngineRef = "main",
                EngineCommit = "0f8453182957900bc6d91287fb8177c8d9762188",
                EnginePullRequest = 45
            }
        };

    private static byte[] CreatePngBytes()
        => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00 };

    private static string ReadEntryText(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"Missing ZIP entry: {name}");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static void ValidateChecksums(ZipArchive archive)
    {
        var checksumLines = ReadEntryText(archive, "SHA256SUMS.txt")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(6, checksumLines.Length);

        foreach (var line in checksumLines)
        {
            var separator = line.IndexOf("  ", StringComparison.Ordinal);
            Assert.True(separator > 0, $"Invalid checksum line: {line}");
            var expected = line[..separator];
            var name = line[(separator + 2)..];
            var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"Missing checksummed entry: {name}");
            using var stream = entry.Open();
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            Assert.Equal(expected, actual);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ARSAS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}