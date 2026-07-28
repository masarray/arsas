using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArIED61850Tester.Services;

public sealed record SmvSnapshotEvidenceProvenance
{
    public string ApplicationName { get; init; } = "ARSAS";
    public string ApplicationVersion { get; init; } = string.Empty;
    public string ApplicationInformationalVersion { get; init; } = string.Empty;
    public string ApplicationRepository { get; init; } = "masarray/arsas";
    public string EngineRepository { get; init; } = string.Empty;
    public string EngineRef { get; init; } = string.Empty;
    public string EngineCommit { get; init; } = string.Empty;
    public int? EnginePullRequest { get; init; }

    public static SmvSnapshotEvidenceProvenance LoadCurrent(string? baseDirectory = null)
    {
        var assembly = typeof(SmvSnapshotEvidenceProvenance).Assembly;
        var version = assembly.GetName().Version?.ToString(3) ?? string.Empty;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? version;

        var provenance = new SmvSnapshotEvidenceProvenance
        {
            ApplicationVersion = version,
            ApplicationInformationalVersion = informationalVersion
        };

        var root = string.IsNullOrWhiteSpace(baseDirectory) ? AppContext.BaseDirectory : baseDirectory;
        var candidates = new[]
        {
            Path.Combine(root, "engines", "ARIEC61850.lock.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "engines", "ARIEC61850.lock.json")
        };

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate))
                continue;

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(candidate, Encoding.UTF8));
                var rootElement = document.RootElement;
                return provenance with
                {
                    EngineRepository = ReadString(rootElement, "repository"),
                    EngineRef = ReadString(rootElement, "ref"),
                    EngineCommit = ReadString(rootElement, "commit"),
                    EnginePullRequest = ReadInt32(rootElement, "sourcePullRequest") ??
                                        ReadInt32(rootElement, "pairedPullRequest")
                };
            }
            catch (JsonException)
            {
                // Export remains available, but the manifest will clearly show missing engine provenance.
            }
        }

        return provenance;
    }

    private static string ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int? ReadInt32(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;
}

public sealed record SmvSnapshotEvidenceContext
{
    public required DateTimeOffset GeneratedAtUtc { get; init; }
    public required string DeviceName { get; init; }
    public required string EndpointText { get; init; }
    public required string AdapterDisplayText { get; init; }
    public required string ControlReference { get; init; }
    public required string SelectedStreamId { get; init; }
    public required string SelectedDataSetReference { get; init; }
    public required string SelectedAppId { get; init; }
    public required string SelectedDestinationMac { get; init; }
    public required double ExplicitNominalFrequencyHz { get; init; }
    public required SmvSnapshotEvidenceProvenance Provenance { get; init; }
}

public sealed record SmvSnapshotEvidenceBundleResult
{
    public required string BundlePath { get; init; }
    public required string BundleSha256 { get; init; }
    public required IReadOnlyList<string> Entries { get; init; }
}

/// <summary>
/// Produces one portable, deterministic and reviewable evidence package for a bounded SV snapshot.
/// The package deliberately preserves raw values and safety boundaries; it never invents current,
/// voltage, phase or engineering-unit semantics without trusted ordered SCL mapping.
/// </summary>
public static class SmvSnapshotEvidenceExporter
{
    public const string SchemaVersion = "arsas.sv-evidence.v1";
    private static readonly DateTimeOffset FixedZipTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task<SmvSnapshotEvidenceBundleResult> ExportAsync(
        string destinationZipPath,
        SmvSnapshotResult snapshot,
        SmvSnapshotEvidenceContext context,
        ReadOnlyMemory<byte> waveformPng,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationZipPath);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);
        if (waveformPng.IsEmpty)
            throw new ArgumentException("A rendered waveform PNG is required for the SV evidence bundle.", nameof(waveformPng));

        var fullDestinationPath = Path.GetFullPath(destinationZipPath);
        var destinationDirectory = Path.GetDirectoryName(fullDestinationPath)
            ?? throw new InvalidOperationException("The evidence bundle destination has no parent directory.");
        Directory.CreateDirectory(destinationDirectory);

        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["README.txt"] = Utf8(BuildReadme(snapshot, context)),
            ["diagnostics.txt"] = Utf8(BuildDiagnostics(snapshot)),
            ["manifest.json"] = Json(BuildManifest(snapshot, context)),
            ["provenance.json"] = Json(context.Provenance),
            ["samples.csv"] = Utf8(BuildSamplesCsv(snapshot)),
            ["waveform.png"] = waveformPng.ToArray()
        };
        files["SHA256SUMS.txt"] = Utf8(BuildChecksums(files));

        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(fullDestinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var output = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             131072,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8))
            {
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = archive.CreateEntry(file.Key, CompressionLevel.Optimal);
                    entry.LastWriteTime = FixedZipTimestamp;
                    await using var stream = entry.Open();
                    await stream.WriteAsync(file.Value, cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(temporaryPath, fullDestinationPath, overwrite: true);
            return new SmvSnapshotEvidenceBundleResult
            {
                BundlePath = fullDestinationPath,
                BundleSha256 = await ComputeFileSha256Async(fullDestinationPath, cancellationToken).ConfigureAwait(false),
                Entries = files.Keys.ToArray()
            };
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public static string BuildSuggestedFileName(
        SmvSnapshotEvidenceContext context,
        SmvSnapshotResult snapshot)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);

        var identity = FirstNonEmpty(
            context.SelectedStreamId,
            snapshot.StreamId,
            context.ControlReference,
            $"APPID-{snapshot.AppId:X4}");
        var safeIdentity = SanitizeFileName(identity, 56);
        return $"ARSAS-SV-Evidence-{safeIdentity}-{context.GeneratedAtUtc:yyyyMMdd-HHmmss}Z.zip";
    }

    private static object BuildManifest(SmvSnapshotResult snapshot, SmvSnapshotEvidenceContext context)
        => new
        {
            schemaVersion = SchemaVersion,
            generatedAtUtc = context.GeneratedAtUtc.ToUniversalTime(),
            verdict = snapshot.IsCleanProof ? "PASS" : "REVIEW",
            proofBoundary = new
            {
                proves = "Bounded reception, IEC 61850-9-2 parsing, stable seqOfData shape and observable sample-counter continuity for the selected window.",
                doesNotProve = "Calibrated current/voltage accuracy, formal conformance, universal interoperability or channel semantics without trusted ordered SCL mapping and reviewed scaling evidence."
            },
            operatorSelection = new
            {
                context.DeviceName,
                context.EndpointText,
                context.AdapterDisplayText,
                context.ControlReference,
                streamId = context.SelectedStreamId,
                dataSetReference = context.SelectedDataSetReference,
                appId = context.SelectedAppId,
                destinationMac = context.SelectedDestinationMac,
                context.ExplicitNominalFrequencyHz
            },
            observedStream = new
            {
                appId = $"0x{snapshot.AppId:X4}",
                snapshot.SourceMac,
                snapshot.DestinationMac,
                vlan = snapshot.VlanText,
                streamId = snapshot.StreamId,
                dataSetReference = snapshot.DataSetReference,
                snapshot.ConfigurationRevision,
                snapshot.SampleSynchronization,
                snapshot.DeclaredSampleRate,
                snapshot.DeclaredSampleMode
            },
            captureInterval = new
            {
                startedAtUtc = snapshot.StartedAt.ToUniversalTime(),
                completedAtUtc = snapshot.CompletedAt.ToUniversalTime(),
                durationMilliseconds = snapshot.CaptureDuration.TotalMilliseconds
            },
            timebase = new
            {
                snapshot.NominalFrequencyHz,
                snapshot.SamplesPerCycle,
                snapshot.CycleCount,
                snapshot.TargetSamples,
                snapshot.CapturedSamples,
                reason = snapshot.TimebaseReason
            },
            transport = new
            {
                snapshot.CapturedFrames,
                snapshot.ParsedAsdus,
                firstSampleCount = snapshot.FirstSampleCount,
                lastSampleCount = snapshot.LastSampleCount
            },
            continuity = new
            {
                snapshot.ContinuousTransitions,
                snapshot.NormalWraps,
                snapshot.GapTransitions,
                snapshot.MissingSamples,
                snapshot.DuplicateTransitions,
                snapshot.OutOfOrderTransitions,
                snapshot.RestartTransitions,
                snapshot.HasCounterAnomaly
            },
            payload = new
            {
                shape = snapshot.PayloadShape,
                plottedLaneCount = snapshot.Channels.Count,
                lanes = snapshot.Channels.Select(channel => new
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
            },
            diagnostics = snapshot.Diagnostics,
            files = new
            {
                rawSamples = "samples.csv",
                waveform = "waveform.png",
                diagnostics = "diagnostics.txt",
                provenance = "provenance.json",
                checksums = "SHA256SUMS.txt"
            }
        };

    private static string BuildSamplesCsv(SmvSnapshotResult snapshot)
    {
        var builder = new StringBuilder(32768);
        builder.Append("sampleIndex,cyclePosition");
        foreach (var channel in snapshot.Channels)
            builder.Append(',').Append(CsvCell($"{channel.Label} [raw INT32]"));
        builder.AppendLine();

        var rowCount = snapshot.Channels.Count == 0
            ? 0
            : snapshot.Channels.Max(channel => channel.Samples.Count);
        for (var sampleIndex = 0; sampleIndex < rowCount; sampleIndex++)
        {
            builder.Append(sampleIndex.ToString(CultureInfo.InvariantCulture));
            builder.Append(',').Append(
                (sampleIndex / (double)Math.Max(1, snapshot.SamplesPerCycle))
                .ToString("0.########", CultureInfo.InvariantCulture));

            foreach (var channel in snapshot.Channels)
            {
                builder.Append(',');
                if (sampleIndex < channel.Samples.Count)
                    builder.Append(channel.Samples[sampleIndex].ToString("R", CultureInfo.InvariantCulture));
            }
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildDiagnostics(SmvSnapshotResult snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Verdict: {(snapshot.IsCleanProof ? "PASS" : "REVIEW")}");
        builder.AppendLine($"Continuity: gaps={snapshot.GapTransitions}, missing={snapshot.MissingSamples}, duplicates={snapshot.DuplicateTransitions}, outOfOrder={snapshot.OutOfOrderTransitions}, restarts={snapshot.RestartTransitions}");
        builder.AppendLine($"Timebase: {snapshot.TimebaseReason}");
        builder.AppendLine($"Payload: {snapshot.PayloadShape}");
        builder.AppendLine();
        builder.AppendLine("Engine diagnostics:");
        if (snapshot.Diagnostics.Count == 0)
            builder.AppendLine("- none");
        else
            foreach (var diagnostic in snapshot.Diagnostics)
                builder.Append("- ").AppendLine(diagnostic);
        return builder.ToString();
    }

    private static string BuildReadme(SmvSnapshotResult snapshot, SmvSnapshotEvidenceContext context)
        => $"""
           ARSAS Sampled Values Evidence Bundle
           ====================================

           Schema: {SchemaVersion}
           Capture started UTC: {snapshot.StartedAt.ToUniversalTime():O}
           Capture completed UTC: {snapshot.CompletedAt.ToUniversalTime():O}
           Bundle generated UTC: {context.GeneratedAtUtc.ToUniversalTime():O}
           Verdict: {(snapshot.IsCleanProof ? "PASS" : "REVIEW")}

           This package records one bounded IEC 61850-9-2 observation window.
           Raw lanes are exported exactly as signed numeric payload representations.

           IMPORTANT BOUNDARY
           ------------------
           This package proves bounded reception, protocol parsing, stable payload shape and
           sample-counter observability for the selected stream. It does not by itself prove
           calibrated current/voltage accuracy, formal conformance, universal interoperability,
           or IA/IB/IC/IN/VA/VB/VC/VN semantics. Those claims require trusted ordered SCL mapping,
           reviewed scaling evidence, known injection and controlled field acceptance.

           Files
           -----
           manifest.json     Structured capture identity, verdict, counters and payload metadata.
           provenance.json   ARSAS and ARIEC61850 source provenance.
           samples.csv       Raw lane samples with invariant-culture numeric formatting.
           waveform.png      Static visual proof rendered by ARSAS.
           diagnostics.txt   Continuity and engine diagnostics.
           SHA256SUMS.txt    SHA-256 digests for every evidence payload file.
           """;

    private static string BuildChecksums(IReadOnlyDictionary<string, byte[]> files)
    {
        var builder = new StringBuilder();
        foreach (var file in files.OrderBy(item => item.Key, StringComparer.Ordinal))
            builder.Append(Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant())
                .Append("  ")
                .AppendLine(file.Key);
        return builder.ToString();
    }

    private static byte[] Json(object value)
        => JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

    private static byte[] Utf8(string value)
        => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(value.Replace("\r\n", "\n", StringComparison.Ordinal));

    private static string CsvCell(string value)
        => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string SanitizeFileName(string value, int maximumLength)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Trim()
            .Select(character => invalid.Contains(character) || char.IsControl(character) ? '-' : character)
            .ToArray());
        while (sanitized.Contains("--", StringComparison.Ordinal))n            sanitized = sanitized.Replace("--", "-", StringComparison.Ordinal);
        sanitized = sanitized.Trim(' ', '.', '-');
        if (sanitized.Length > maximumLength)
            sanitized = sanitized[..maximumLength].TrimEnd(' ', '.', '-');
        return string.IsNullOrWhiteSpace(sanitized) ? "SV-stream" : sanitized;
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "SV-stream";

    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[131072];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}