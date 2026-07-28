using System.IO.Compression;
using System.Text.Json;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SmvEvidenceBundleExporterTests
{
    [Fact]
    public async Task ExportAsync_WritesAuditableBundleWithIntegrityEvidence()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"arsas-sv-evidence-{Guid.NewGuid():N}.zip");
        try
        {
            var snapshot = CreateSnapshot();
            var exporter = new SmvEvidenceBundleExporter();
            var result = await exporter.ExportAsync(new SmvEvidenceBundleRequest
            {
                OutputPath = outputPath,
                Snapshot = snapshot,
                WaveformPng = [137, 80, 78, 71, 13, 10, 26, 10],
                Selection = SmvSnapshotSelectionIdentity.Create(
                    "IED1LD0/LLN0$MSVCB01",
                    "MU01",
                    "IED1LD0/LLN0$Dataset01",
                    "0x4000",
                    "01-0C-CD-04-00-01"),
                ApplicationVersion = "1.6.19",
                ApplicationCommit = "abc123",
                EngineRepository = "masarray/ARIEC61850",
                EngineReference = "main",
                EngineCommit = "0f8453182957900bc6d91287fb8177c8d9762188",
                ExportedAtUtc = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero)
            });

            Assert.True(File.Exists(outputPath));
            Assert.Matches("^[0-9a-f]{64}$", result.BundleSha256);

            using var archive = ZipFile.OpenRead(outputPath);
            var names = archive.Entries
                .Select(entry => entry.FullName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(
                ["SHA256SUMS.txt", "diagnostics.txt", "manifest.json", "samples.csv", "waveform.png"],
                names);

            var manifestText = await ReadEntryAsync(archive, "manifest.json");
            using var manifest = JsonDocument.Parse(manifestText);
            Assert.Equal("arsas.sv-evidence-bundle.v1", manifest.RootElement.GetProperty("schema").GetString());
            Assert.Equal("REVIEW", manifest.RootElement.GetProperty("verdict").GetString());
            Assert.Equal("1.6.19", manifest.RootElement.GetProperty("application").GetProperty("version").GetString());
            Assert.Equal(
                "0f8453182957900bc6d91287fb8177c8d9762188",
                manifest.RootElement.GetProperty("engine").GetProperty("commit").GetString());

            var csv = await ReadEntryAsync(archive, "samples.csv");
            Assert.Contains("sample_index,smp_cnt,Raw lane 1,Raw lane 2", csv, StringComparison.Ordinal);
            Assert.Contains("0,10,100,-100", csv, StringComparison.Ordinal);
            Assert.Contains("2,12,120,-120", csv, StringComparison.Ordinal);

            var diagnostics = await ReadEntryAsync(archive, "diagnostics.txt");
            Assert.Contains("Verdict: REVIEW", diagnostics, StringComparison.Ordinal);
            Assert.Contains("restart 1", diagnostics, StringComparison.Ordinal);

            var checksums = await ReadEntryAsync(archive, "SHA256SUMS.txt");
            Assert.Contains("  manifest.json", checksums, StringComparison.Ordinal);
            Assert.Contains("  samples.csv", checksums, StringComparison.Ordinal);
            Assert.Contains("  waveform.png", checksums, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    private static SmvSnapshotResult CreateSnapshot()
        => new()
        {
            StartedAt = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2026, 7, 28, 0, 0, 0, 40, TimeSpan.Zero),
            AppId = 0x4000,
            SourceMac = "00-11-22-33-44-55",
            DestinationMac = "01-0C-CD-04-00-01",
            VlanText = "100",
            StreamId = "MU01",
            DataSetReference = "IED1LD0/LLN0$Dataset01",
            ConfigurationRevision = 7,
            SampleSynchronization = 2,
            NominalFrequencyHz = 50,
            SamplesPerCycle = 80,
            CycleCount = 2,
            TargetSamples = 3,
            CapturedSamples = 3,
            CapturedFrames = 3,
            ParsedAsdus = 3,
            FirstSampleCount = 10,
            LastSampleCount = 12,
            ContinuousTransitions = 1,
            RestartTransitions = 1,
            TimebaseReason = "explicit test evidence",
            PayloadShape = "16 bytes · 2 raw 32-bit words",
            Diagnostics = ["Publisher restart observed."],
            Channels =
            [
                new SmvSnapshotChannel
                {
                    ChannelIndex = 1,
                    PayloadWordIndex = 0,
                    Label = "Raw lane 1",
                    Interpretation = "Signed INT32",
                    Samples = [100, 110, 120],
                    Minimum = 100,
                    Maximum = 120
                },
                new SmvSnapshotChannel
                {
                    ChannelIndex = 2,
                    PayloadWordIndex = 1,
                    Label = "Raw lane 2",
                    Interpretation = "Signed INT32",
                    Samples = [-100, -110, -120],
                    Minimum = -120,
                    Maximum = -100
                }
            ]
        };

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"Missing ZIP entry: {name}");
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
