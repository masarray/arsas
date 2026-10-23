using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SmvSnapshotEvidenceExporterTestsReview
{
    [Fact]
    public async Task Manifest_SeparatesCaptureIntervalFromBundleGenerationTime()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var startedAt = new DateTimeOffset(2026, 7, 28, 5, 0, 0, TimeSpan.Zero);
            var completedAt = startedAt.AddMilliseconds(40);
            var generatedAt = completedAt.AddMinutes(5);
            var path = Path.Combine(directory, "capture-time.zip");

            await SmvSnapshotEvidenceExporter.ExportAsync(
                path,
                CreateSnapshot(startedAt, completedAt),
                CreateContext(generatedAt),
                new byte[] { 0x89, 0x50, 0x4E, 0x47 });

            using var archive = ZipFile.OpenRead(path);
            using var manifest = JsonDocument.Parse(ReadEntryText(archive, "manifest.json"));
            var root = manifest.RootElement;
            var interval = root.GetProperty("captureInterval");

            Assert.Equal(startedAt, interval.GetProperty("startedAtUtc").GetDateTimeOffset());
            Assert.Equal(completedAt, interval.GetProperty("completedAtUtc").GetDateTimeOffset());
            Assert.Equal(40, interval.GetProperty("durationMilliseconds").GetDouble());
            Assert.Equal(generatedAt, root.GetProperty("generatedAtUtc").GetDateTimeOffset());

            var readme = ReadEntryText(archive, "README.txt");
            Assert.Contains($"Capture started UTC: {startedAt:O}", readme, StringComparison.Ordinal);
            Assert.Contains($"Capture completed UTC: {completedAt:O}", readme, StringComparison.Ordinal);
            Assert.Contains($"Bundle generated UTC: {generatedAt:O}", readme, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ProvenanceLoader_ReadsSourcePullRequestFromShippedLockSchema()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var engineDirectory = Path.Combine(directory, "engines");
            Directory.CreateDirectory(engineDirectory);
            File.WriteAllText(
                Path.Combine(engineDirectory, "ARIEC61850.lock.json"),
                """
                {
                  "repository": "masarray/ARIEC61850",
                  "ref": "main",
                  "commit": "0f8453182957900bc6d91287fb8177c8d9762188",
                  "sourcePullRequest": 45
                }
                """,
                Encoding.UTF8);

            var provenance = SmvSnapshotEvidenceProvenance.LoadCurrent(directory);

            Assert.Equal("masarray/ARIEC61850", provenance.EngineRepository);
            Assert.Equal("main", provenance.EngineRef);
            Assert.Equal("0f8453182957900bc6d91287fb8177c8d9762188", provenance.EngineCommit);
            Assert.Equal(45, provenance.EnginePullRequest);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static SmvSnapshotResult CreateSnapshot(DateTimeOffset startedAt, DateTimeOffset completedAt)
        => new()
        {
            StartedAt = startedAt,
            CompletedAt = completedAt,
            AppId = 0x4000,
            DestinationMac = "01-0C-CD-04-00-01",
            StreamId = "MU01",
            NominalFrequencyHz = 50,
            SamplesPerCycle = 80,
            CycleCount = 2,
            TargetSamples = 160,
            CapturedSamples = 160,
            CapturedFrames = 80,
            ParsedAsdus = 160,
            LastSampleCount = 159,
            ContinuousTransitions = 159,
            TimebaseReason = "Deterministic review test.",
            PayloadShape = "4 bytes · 1 raw 32-bit word",
            Channels = new[]
            {
                new SmvSnapshotChannel
                {
                    ChannelIndex = 1,
                    PayloadWordIndex = 0,
                    Label = "Raw word 1",
                    Interpretation = "Signed INT32 representation; channel/unit semantics unresolved",
                    Samples = new double[] { 1, 2 },
                    Minimum = 1,
                    Maximum = 2
                }
            }
        };

    private static SmvSnapshotEvidenceContext CreateContext(DateTimeOffset generatedAt)
        => new()
        {
            GeneratedAtUtc = generatedAt,
            DeviceName = "Review IED",
            EndpointText = "192.0.2.20:102",
            AdapterDisplayText = "Captured mirror adapter",
            ControlReference = "IED1LD0/LLN0$MSVCB01",
            SelectedStreamId = "MU01",
            SelectedDataSetReference = "IED1LD0/LLN0$Dataset01",
            SelectedAppId = "0x4000",
            SelectedDestinationMac = "01-0C-CD-04-00-01",
            ExplicitNominalFrequencyHz = 50,
            Provenance = new SmvSnapshotEvidenceProvenance()
        };

    private static string ReadEntryText(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"Missing ZIP entry: {name}");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ARSAS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}