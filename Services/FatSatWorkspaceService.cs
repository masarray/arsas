using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public sealed record FatSatAuditPackageResult(
    string OutputPath,
    string PackageSha256,
    FatSatWorkspaceSummary Summary,
    IReadOnlyDictionary<string, string> EntrySha256);

public sealed class FatSatWorkspaceService
{
    private const long MaximumEvidenceFileBytes = 256L * 1024L * 1024L;
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public FatSatWorkspaceDocument CreateDefault()
        => new()
        {
            Scope = "Bounded IEC 61850 FAT/SAT execution with evidence-backed outcomes. Formal conformance and universal interoperability remain outside scope unless separately proven.",
            TestCases =
            [
                CreateCase("001", "Identity", "Confirm IED identity and project binding", "Record the selected IED name, endpoint, SCL source, firmware identity when available, and test boundary.", "The tested asset is unambiguously identified and bound to this workspace."),
                CreateCase("010", "MMS", "Discover IEC 61850 server model", "Connect through MMS and discover Logical Devices, Logical Nodes, Data Objects, Data Attributes, DataSets, and control blocks.", "The model is discovered without unresolved fatal errors and the intended IED is represented."),
                CreateCase("020", "Reporting", "Validate report acquisition and recovery", "Enable the selected report path, observe updates, interrupt the association, reconnect, and verify recovery or documented fallback.", "Selected values update with traceable source and recover after reconnect without cross-IED contamination."),
                CreateCase("030", "GOOSE", "Validate subscribed GOOSE state and timing", "Observe the selected GOOSE control block, stNum/sqNum progression, TAL state, duplicates, and sequence anomalies.", "Stream identity is correct and observed state/timing findings match the expected test condition."),
                CreateCase("040", "Sampled Values", "Capture bounded SV evidence", "Capture the selected IEC 61850-9-2 stream for the required window and attach the ARSAS SV Evidence Bundle.", "The evidence bundle binds stream identity, payload shape, counter continuity, provenance, and explicit semantic boundary."),
                CreateCase("050", "Control", "Validate guarded control workflow", "Confirm ctlModel, command selection, interlock/synchrocheck policy, command termination, and process feedback using an authorized isolated test setup.", "The command result and feedback are consistent, time-bounded, and evidence-backed; unsafe or unauthorized operation is not performed."),
                CreateCase("060", "Files", "Validate IEC 61850 file transfer", "List the configured remote path, transfer a representative record, verify completion, and attach the received artifact or checksum.", "The file is transferred completely with stable identity and recorded integrity evidence."),
                CreateCase("070", "SCL", "Compare configured, generated, and live context", "Review the imported SCL source, generated bounded export, and available live model evidence for material differences.", "Material differences are recorded, explained, and accepted or left as explicit deviations."),
                CreateCase("090", "Closeout", "Review deviations and acceptance boundary", "Review FAIL, REVIEW, BLOCKED, and open deviations with the operator and witness before package export.", "No unresolved outcome is hidden; acceptance status and remaining field actions are explicit.")
            ]
        };

    public FatSatWorkspaceSummary Summarize(FatSatWorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new FatSatWorkspaceSummary(
            document.TestCases.Count,
            document.TestCases.Count(item => item.Result == FatSatTestResult.NotRun),
            document.TestCases.Count(item => item.Result == FatSatTestResult.Pass),
            document.TestCases.Count(item => item.Result == FatSatTestResult.Fail),
            document.TestCases.Count(item => item.Result == FatSatTestResult.Review),
            document.TestCases.Count(item => item.Result == FatSatTestResult.Blocked),
            document.TestCases.Count(item => item.Result == FatSatTestResult.NotApplicable),
            document.TestCases.Sum(item => item.Evidence.Count));
    }

    public async Task SaveAsync(
        string path,
        FatSatWorkspaceDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Validate(document);
        document.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory);
        var temporaryPath = fullPath + ".partial";
        try
        {
            await File.WriteAllBytesAsync(
                temporaryPath,
                JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions),
                cancellationToken);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            throw;
        }
    }

    public async Task<FatSatWorkspaceDocument> OpenAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(
            Path.GetFullPath(path),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            useAsync: true);
        var document = await JsonSerializer.DeserializeAsync<FatSatWorkspaceDocument>(stream, JsonOptions, cancellationToken)
                       ?? throw new InvalidDataException("FAT/SAT workspace is empty or malformed.");
        Validate(document);
        return document;
    }

    public async Task<FatSatEvidenceReference> CreateEvidenceReferenceAsync(
        string path,
        string description = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
            throw new FileNotFoundException("Evidence file was not found.", fullPath);
        if (file.Length <= 0)
            throw new InvalidDataException("Empty evidence files cannot be attached.");
        if (file.Length > MaximumEvidenceFileBytes)
            throw new InvalidDataException($"Evidence file exceeds the {MaximumEvidenceFileBytes / (1024 * 1024)} MB package limit.");

        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return new FatSatEvidenceReference
        {
            DisplayName = file.Name,
            SourcePath = fullPath,
            Sha256 = Convert.ToHexString(hash).ToLowerInvariant(),
            SizeBytes = file.Length,
            MediaType = ResolveMediaType(file.Extension),
            AttachedAtUtc = DateTimeOffset.UtcNow,
            Description = description?.Trim() ?? string.Empty
        };
    }

    public async Task<FatSatAuditPackageResult> ExportAuditPackageAsync(
        string outputPath,
        FatSatWorkspaceDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        Validate(document);
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory);
        var temporaryPath = fullPath + ".partial";
        if (File.Exists(temporaryPath))
            File.Delete(temporaryPath);

        var summary = Summarize(document);
        var packageDocument = CloneForPackage(document);
        var entries = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["workspace.json"] = JsonSerializer.SerializeToUtf8Bytes(packageDocument, JsonOptions),
            ["report.md"] = Utf8NoBom.GetBytes(BuildMarkdownReport(packageDocument, summary))
        };

        foreach (var testCase in document.TestCases)
        {
            foreach (var evidence in testCase.Evidence)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = Path.GetFullPath(evidence.SourcePath);
                if (!File.Exists(source))
                    throw new FileNotFoundException($"Evidence file '{evidence.DisplayName}' is missing.", source);
                var bytes = await File.ReadAllBytesAsync(source, cancellationToken);
                if (bytes.LongLength != evidence.SizeBytes)
                    throw new InvalidDataException($"Evidence size changed after attachment: {evidence.DisplayName}.");
                var actualHash = ComputeSha256(bytes);
                if (!actualHash.Equals(evidence.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Evidence hash changed after attachment: {evidence.DisplayName}.");
                entries[BuildEvidenceEntryPath(testCase, evidence)] = bytes;
            }
        }

        var hashes = entries.ToDictionary(pair => pair.Key, pair => ComputeSha256(pair.Value), StringComparer.Ordinal);
        entries["SHA256SUMS.txt"] = Utf8NoBom.GetBytes(BuildChecksums(hashes));

        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, useAsync: true))
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                foreach (var pair in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = archive.CreateEntry(pair.Key, CompressionLevel.Optimal);
                    entry.LastWriteTime = document.UpdatedAtUtc;
                    await using var entryStream = entry.Open();
                    await entryStream.WriteAsync(pair.Value, cancellationToken);
                }
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
            var packageHash = ComputeSha256(await File.ReadAllBytesAsync(fullPath, cancellationToken));
            return new FatSatAuditPackageResult(fullPath, packageHash, summary, hashes);
        }
        catch
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            throw;
        }
    }

    public void Validate(FatSatWorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != FatSatWorkspaceDocument.CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported FAT/SAT schema version {document.SchemaVersion}. Expected {FatSatWorkspaceDocument.CurrentSchemaVersion}.");
        if (document.WorkspaceId == Guid.Empty)
            throw new InvalidDataException("Workspace identity is missing.");
        if (document.TestCases.Count > 1000)
            throw new InvalidDataException("Workspace exceeds the 1,000 test-case safety limit.");
        var duplicate = document.TestCases.GroupBy(item => item.TestCaseId).FirstOrDefault(group => group.Key == Guid.Empty || group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException("Test-case identities must be non-empty and unique.");
        foreach (var testCase in document.TestCases)
        {
            if (string.IsNullOrWhiteSpace(testCase.Title))
                throw new InvalidDataException("Every FAT/SAT test case requires a title.");
            if (testCase.Evidence.Count > 100)
                throw new InvalidDataException($"Test case '{testCase.Title}' exceeds the 100-evidence-file safety limit.");
            var duplicateEvidence = testCase.Evidence.GroupBy(item => item.EvidenceId).FirstOrDefault(group => group.Key == Guid.Empty || group.Count() > 1);
            if (duplicateEvidence is not null)
                throw new InvalidDataException($"Test case '{testCase.Title}' contains duplicate evidence identities.");
        }
    }

    private static FatSatTestCase CreateCase(string sequence, string area, string title, string procedure, string expected)
        => new()
        {
            Sequence = sequence,
            Area = area,
            Title = title,
            Procedure = procedure,
            ExpectedResult = expected
        };

    private static FatSatWorkspaceDocument CloneForPackage(FatSatWorkspaceDocument source)
    {
        var clone = JsonSerializer.Deserialize<FatSatWorkspaceDocument>(JsonSerializer.Serialize(source, JsonOptions), JsonOptions)
                    ?? throw new InvalidOperationException("FAT/SAT workspace could not be cloned for packaging.");
        foreach (var testCase in clone.TestCases)
        {
            foreach (var evidence in testCase.Evidence)
                evidence.SourcePath = BuildEvidenceEntryPath(testCase, evidence);
        }
        return clone;
    }

    private static string BuildEvidenceEntryPath(FatSatTestCase testCase, FatSatEvidenceReference evidence)
    {
        var sequence = SanitizeFileName(string.IsNullOrWhiteSpace(testCase.Sequence) ? "test" : testCase.Sequence);
        var name = SanitizeFileName(string.IsNullOrWhiteSpace(evidence.DisplayName) ? "evidence.bin" : evidence.DisplayName);
        return $"evidence/{sequence}-{testCase.TestCaseId:N}/{evidence.EvidenceId:N}-{name}";
    }

    private static string BuildMarkdownReport(FatSatWorkspaceDocument document, FatSatWorkspaceSummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# ARSAS FAT/SAT Test & Evidence Report");
        builder.AppendLine();
        builder.AppendLine($"- Project: {Readable(document.ProjectName)}");
        builder.AppendLine($"- Site: {Readable(document.SiteName)}");
        builder.AppendLine($"- Bay/System: {Readable(document.BayOrSystem)}");
        builder.AppendLine($"- IED: {Readable(document.IedIdentity)}");
        builder.AppendLine($"- Operator: {Readable(document.OperatorName)}");
        builder.AppendLine($"- Witness: {Readable(document.WitnessName)}");
        builder.AppendLine($"- Updated UTC: {document.UpdatedAtUtc:O}");
        builder.AppendLine($"- ARSAS: {Readable(document.ApplicationVersion)} / {Readable(document.ApplicationCommit)}");
        builder.AppendLine($"- Engine: {Readable(document.EngineRepository)} @ {Readable(document.EngineCommit)}");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine($"Total {summary.Total}; PASS {summary.Passed}; FAIL {summary.Failed}; REVIEW {summary.Review}; BLOCKED {summary.Blocked}; NOT RUN {summary.NotRun}; N/A {summary.NotApplicable}; evidence {summary.EvidenceFiles}.");
        builder.AppendLine();
        builder.AppendLine($"Package disposition: {(summary.IsComplete ? "COMPLETE / NO BLOCKING OUTCOME" : summary.HasBlockingOutcome ? "REVIEW REQUIRED" : "INCOMPLETE")}");
        builder.AppendLine();
        builder.AppendLine("## Scope and boundary");
        builder.AppendLine();
        builder.AppendLine(Readable(document.Scope));
        builder.AppendLine();
        builder.AppendLine("## Test cases");
        builder.AppendLine();
        foreach (var testCase in document.TestCases.OrderBy(item => item.Sequence, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"### {Readable(testCase.Sequence)} · {Readable(testCase.Area)} · {Readable(testCase.Title)}");
            builder.AppendLine();
            builder.AppendLine($"- Result: **{testCase.Result.ToString().ToUpperInvariant()}**");
            builder.AppendLine($"- Expected: {Readable(testCase.ExpectedResult)}");
            builder.AppendLine($"- Actual: {Readable(testCase.ActualResult)}");
            builder.AppendLine($"- Operator note: {Readable(testCase.OperatorNote)}");
            builder.AppendLine($"- Deviation: {Readable(testCase.ExceptionOrDeviation)}");
            builder.AppendLine($"- Executed by/UTC: {Readable(testCase.ExecutedBy)} / {(testCase.ExecutedAtUtc.HasValue ? testCase.ExecutedAtUtc.Value.ToString("O") : "-")}");
            if (testCase.Evidence.Count == 0)
            {
                builder.AppendLine("- Evidence: none attached");
            }
            else
            {
                builder.AppendLine("- Evidence:");
                foreach (var evidence in testCase.Evidence)
                    builder.AppendLine($"  - `{BuildEvidenceEntryPath(testCase, evidence)}` · SHA-256 `{evidence.Sha256}` · {evidence.SizeBytes} bytes");
            }
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static string BuildChecksums(IReadOnlyDictionary<string, string> hashes)
        => string.Join("\n", hashes.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Value}  {pair.Key}")) + "\n";

    private static string ComputeSha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string Readable(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim().Replace("\r", " ").Replace("\n", " ");

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(result) ? "item" : result;
    }

    private static string ResolveMediaType(string extension)
        => extension.ToLowerInvariant() switch
        {
            ".zip" => "application/zip",
            ".json" => "application/json",
            ".csv" => "text/csv",
            ".txt" or ".log" or ".md" => "text/plain",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".pcap" or ".pcapng" => "application/vnd.tcpdump.pcap",
            ".cfg" or ".dat" or ".cff" => "application/octet-stream",
            _ => "application/octet-stream"
        };
}
