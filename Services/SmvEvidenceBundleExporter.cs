using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArIED61850Tester.Services;

public sealed record SmvEvidenceBundleRequest
{
    public required string OutputPath { get; init; }
    public required SmvSnapshotResult Snapshot { get; init; }
    public required byte[] WaveformPng { get; init; }
    public required SmvSnapshotSelectionIdentity Selection { get; init; }
    public required string ApplicationVersion { get; init; }
    public required string ApplicationCommit { get; init; }
    public required string EngineRepository { get; init; }
    public required string EngineReference { get; init; }
    public required string EngineCommit { get; init; }
    public DateTimeOffset ExportedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record SmvEvidenceBundleResult
{
    public required string OutputPath { get; init; }
    public required string BundleSha256 { get; init; }
    public required IReadOnlyDictionary<string, string> EntrySha256 { get; init; }
}

public sealed class SmvEvidenceBundleExporter
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<SmvEvidenceBundleResult> ExportAsync(
        SmvEvidenceBundleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Snapshot);
        ArgumentNullException.ThrowIfNull(request.Selection);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        if (request.WaveformPng.Length == 0)
            throw new ArgumentException("Waveform PNG is empty.", nameof(request));

        var outputPath = Path.GetFullPath(request.OutputPath);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var entries = BuildEntries(request);
        var hashes = entries.ToDictionary(
            pair => pair.Key,
            pair => ComputeSha256(pair.Value),
            StringComparer.Ordinal);
        entries["SHA256SUMS.txt"] = Utf8NoBom.GetBytes(BuildChecksums(hashes));

        var temporaryPath = outputPath + ".partial";
        if (File.Exists(temporaryPath))
            File.Delete(temporaryPath);

        try
        {
            await using (var file = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                64 * 1024,
                useAsync: true))
            {
                using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true);
                foreach (var pair in entries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = archive.CreateEntry(pair.Key, CompressionLevel.Optimal);
                    entry.LastWriteTime = request.ExportedAtUtc;
                    await using var stream = entry.Open();
                    await stream.WriteAsync(pair.Value, cancellationToken);
                }
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
            var bundleBytes = await File.ReadAllBytesAsync(outputPath, cancellationToken);
            return new SmvEvidenceBundleResult
            {
                OutputPath = outputPath,
                BundleSha256 = ComputeSha256(bundleBytes),
                EntrySha256 = hashes
            };
        }
        catch
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            throw;
        }
    }

    internal static Dictionary<string, byte[]> BuildEntries(SmvEvidenceBundleRequest request)
    {
        var snapshot = request.Snapshot;
        var manifest = new
        {
            schema = "arsas.sv-evidence-bundle.v1",
            exportedAtUtc = request.ExportedAtUtc,
            verdict = snapshot.IsCleanProof ? "PASS" : "REVIEW",
            boundary = "Bounded passive IEC 61850-9-2 reception evidence; not calibrated measurement or formal conformance evidence.",
            application = new
            {
                name = "ARSAS",
                version = request.ApplicationVersion,
                commit = request.ApplicationCommit
            },
            engine = new
            {
                repository = request.EngineRepository,
                reference = request.EngineReference,
                commit = request.EngineCommit
            },
            selection = request.Selection,
            stream = new
            {
                appId = $"0x{snapshot.AppId:X4}",
                snapshot.SourceMac,
                snapshot.DestinationMac,
                vlan = snapshot.VlanText,
                svId = snapshot.StreamId,
                dataSetReference = snapshot.DataSetReference,
                snapshot.ConfigurationRevision,
                snapshot.SampleSynchronization
            },
            capture = new
            {
                snapshot.NominalFrequencyHz,
                snapshot.SamplesPerCycle,
                snapshot.CycleCount,
                snapshot.TargetSamples,
                snapshot.CapturedSamples,
                snapshot.CapturedFrames,
                snapshot.ParsedAsdus,
                snapshot.FirstSampleCount,
                snapshot.LastSampleCount,
                snapshot.ContinuousTransitions,
                snapshot.NormalWraps,
                snapshot.GapTransitions,
                snapshot.MissingSamples,
                snapshot.DuplicateTransitions,
                snapshot.OutOfOrderTransitions,
                snapshot.RestartTransitions,
                durationMilliseconds = snapshot.CaptureDuration.TotalMilliseconds,
                snapshot.TimebaseReason,
                snapshot.PayloadShape
            },
            channels = snapshot.Channels.Select(channel => new
            {
                channel.ChannelIndex,
                channel.PayloadWordIndex,
                channel.Label,
                channel.Interpretation,
                sampleCount = channel.Samples.Count,
                channel.Minimum,
                channel.Maximum,
                channel.PeakToPeak
            })
        };

        return new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["waveform.png"] = request.WaveformPng,
            ["samples.csv"] = Utf8NoBom.GetBytes(BuildCsv(snapshot)),
            ["manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions),
            ["diagnostics.txt"] = Utf8NoBom.GetBytes(BuildDiagnostics(snapshot))
        };
    }

    internal static string BuildCsv(SmvSnapshotResult snapshot)
    {
        var builder = new StringBuilder();
        builder.Append("sample_index,smp_cnt");
        foreach (var channel in snapshot.Channels)
            builder.Append(',').Append(EscapeCsv(channel.Label));
        builder.AppendLine();

        var sampleCount = snapshot.Channels.Count == 0
            ? 0
            : snapshot.Channels.Min(channel => channel.Samples.Count);
        var wrap = Math.Max(1, snapshot.SamplesPerCycle * snapshot.CycleCount);
        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            builder.Append(sampleIndex.ToString(CultureInfo.InvariantCulture));
            var smpCnt = (snapshot.FirstSampleCount + sampleIndex) % wrap;
            builder.Append(',').Append(smpCnt.ToString(CultureInfo.InvariantCulture));
            foreach (var channel in snapshot.Channels)
            {
                builder.Append(',').Append(channel.Samples[sampleIndex].ToString("R", CultureInfo.InvariantCulture));
            }
            builder.AppendLine();
        }

        return builder.ToString();
    }

    internal static string BuildDiagnostics(SmvSnapshotResult snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Verdict: {(snapshot.IsCleanProof ? "PASS" : "REVIEW")}");
        builder.AppendLine(SmvSnapshotSafetyAssessment.BuildContinuityEvidence(snapshot));
        builder.AppendLine($"Timebase: {snapshot.TimebaseReason}");
        builder.AppendLine($"Payload: {snapshot.PayloadShape}");
        builder.AppendLine("Boundary: raw lanes remain semantically unresolved until ordered SCL mapping and reviewed scaling evidence are bound.");
        if (snapshot.Diagnostics.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Parser and continuity diagnostics:");
            foreach (var diagnostic in snapshot.Diagnostics.Distinct(StringComparer.Ordinal))
                builder.AppendLine($"- {diagnostic}");
        }
        return builder.ToString();
    }

    private static string BuildChecksums(IReadOnlyDictionary<string, string> hashes)
        => string.Join("\n", hashes.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Value}  {pair.Key}")) + "\n";

    private static string ComputeSha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string EscapeCsv(string value)
    {
        var text = value ?? string.Empty;
        if (!text.Contains(',') && !text.Contains('"') && !text.Contains('\n') && !text.Contains('\r'))
            return text;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
