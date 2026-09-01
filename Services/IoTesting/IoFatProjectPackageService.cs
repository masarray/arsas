using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public static class IoFatProjectPackageService
{
    public const string PackageExtension = ".arsas";
    public const string LegacyPackageExtension = ".arsas-iofat";
    private const long MaximumPackageBytes = 500L * 1024 * 1024;
    private const int MaximumEntries = 10_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string OpenDialogFilter =>
        $"ARSAS project (*{PackageExtension})|*{PackageExtension}|" +
        $"Legacy IO FAT package (*{LegacyPackageExtension})|*{LegacyPackageExtension}|" +
        "All files (*.*)|*.*";

    public static bool IsSupportedPackagePath(string? path)
        => !string.IsNullOrWhiteSpace(path) &&
           (path.EndsWith(PackageExtension, StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(LegacyPackageExtension, StringComparison.OrdinalIgnoreCase));

    public static async Task ValidateAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        var info = new FileInfo(packagePath);
        if (!info.Exists)
            throw new FileNotFoundException("The ARSAS project was not found.", packagePath);
        if (info.Length > MaximumPackageBytes)
            throw new InvalidDataException("The ARSAS project exceeds the 500 MB safety limit.");

        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count > MaximumEntries)
            throw new InvalidDataException("The ARSAS project contains too many entries.");

        var manifestEntry = RequiredEntry(archive, "manifest.json");
        var manifestBytes = await ReadEntryAsync(
            manifestEntry,
            5 * 1024 * 1024,
            cancellationToken).ConfigureAwait(false);
        using var manifest = JsonDocument.Parse(manifestBytes);
        var root = manifest.RootElement;

        if (root.TryGetProperty("packageKind", out var kind) && kind.ValueKind == JsonValueKind.String &&
            !string.Equals(kind.GetString(), "io-fat", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"This .arsas project has package kind '{kind.GetString()}' and cannot be opened in the IO List Testing workspace.");
        }

        await VerifySourceBundleAsync(archive, root, cancellationToken).ConfigureAwait(false);
        await VerifyOptionalManifestEntryAsync(
            archive,
            root,
            "reportEntry",
            "reportSha256",
            "native PDF report",
            cancellationToken).ConfigureAwait(false);
        await VerifyOptionalManifestEntryAsync(
            archive,
            root,
            "resultWorkbookEntry",
            "resultWorkbookSha256",
            "Excel result workbook",
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string> ExportAsync(
        IoTestWorkspacePersistence workspace,
        IoTestSessionController session,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (session.IsSessionActive)
        {
            throw new InvalidOperationException(
                "Stop the active FAT session before exporting an ARSAS project so every evidence journal is sealed.");
        }
        if (workspace.SourceFiles.Count == 0)
            throw new InvalidDataException("The FAT workspace contains no source files to package.");

        workspace.SaveNow();
        var snapshotBytes = await File.ReadAllBytesAsync(workspace.SnapshotPath, cancellationToken).ConfigureAwait(false);
        var sourcePayloads = new List<(IoFatWorkspaceSource Source, byte[] Bytes)>();
        foreach (var source in workspace.SourceFiles)
        {
            sourcePayloads.Add((
                source,
                await IoFatSourceWorkspaceService.ReadVerifiedAsync(source, cancellationToken).ConfigureAwait(false)));
        }
        var packageSources = IoFatSourceWorkspaceService.ToPackageSources(workspace.SourceFiles).ToList();
        var sourceSetSha256 = IoFatSourceIdentity.ComputeSetFingerprint(workspace.SourceFiles.Select(source => source.Source));
        var workbook = workspace.SourceFiles.FirstOrDefault(source =>
            source.Source.Kind.Equals(IoFatSourceKinds.Workbook, StringComparison.OrdinalIgnoreCase));

        var evidenceFiles = new List<PackageEvidence>();
        if (Directory.Exists(workspace.EvidenceProjectDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(
                         workspace.EvidenceProjectDirectory,
                         "*.evidence.jsonl",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var verification = IoTestEvidenceJournal.Verify(path);
                if (!verification.IsValid)
                {
                    throw new InvalidDataException(
                        $"Evidence '{Path.GetFileName(path)}' failed verification: {verification.Error}");
                }

                evidenceFiles.Add(new PackageEvidence(
                    $"evidence/{Path.GetFileName(path)}",
                    HashFile(path),
                    verification.RecordCount,
                    verification.LastHash));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var generatedAt = DateTimeOffset.Now;
        var pdfBytes = IoFatPdfReportService.Generate(workspace.Project, generatedAt);

        string resultWorkbookEntry = string.Empty;
        string resultWorkbookSha256 = string.Empty;
        byte[]? resultWorkbookBytes = null;
        if (workbook != null)
        {
            var resultWorkbookTemporary = Path.Combine(
                Path.GetTempPath(),
                "ARSAS",
                "IO FAT Export",
                Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                await IoFatExcelResultExportService.ExportAsync(
                    workbook.LocalPath,
                    resultWorkbookTemporary,
                    workspace.Project,
                    cancellationToken).ConfigureAwait(false);
                resultWorkbookBytes = await File.ReadAllBytesAsync(
                    resultWorkbookTemporary,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (File.Exists(resultWorkbookTemporary))
                    File.Delete(resultWorkbookTemporary);
            }

            resultWorkbookEntry = "report/IO-FAT-Results.xlsx";
            resultWorkbookSha256 = HashBytes(resultWorkbookBytes);
        }

        const string reportEntry = "report/IO-FAT-Report.pdf";
        var manifest = new PackageManifest(
            IoTestWorkspacePersistence.PackageVersion,
            "io-fat",
            generatedAt.ToUniversalTime(),
            workspace.Project.ProjectId,
            workspace.Project.ProjectName,
            workspace.Project.SchemaVersion,
            "project.snapshot.json",
            HashBytes(snapshotBytes),
            workbook?.PackageEntry ?? string.Empty,
            workbook?.Source.Sha256 ?? string.Empty,
            reportEntry,
            HashBytes(pdfBytes),
            resultWorkbookEntry,
            resultWorkbookSha256,
            evidenceFiles)
        {
            SourceSetSha256 = sourceSetSha256,
            SourceFiles = packageSources
        };

        var fullDestination = NormalizeDestination(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        var temporary = fullDestination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                await WriteEntryAsync(
                    archive,
                    "manifest.json",
                    JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions),
                    cancellationToken).ConfigureAwait(false);
                await WriteEntryAsync(archive, manifest.SnapshotEntry, snapshotBytes, cancellationToken).ConfigureAwait(false);
                foreach (var source in sourcePayloads)
                    await WriteEntryAsync(archive, source.Source.PackageEntry, source.Bytes, cancellationToken).ConfigureAwait(false);
                await WriteEntryAsync(archive, manifest.ReportEntry, pdfBytes, cancellationToken).ConfigureAwait(false);
                if (resultWorkbookBytes != null)
                    await WriteEntryAsync(archive, manifest.ResultWorkbookEntry, resultWorkbookBytes, cancellationToken).ConfigureAwait(false);
                await WriteEntryAsync(
                    archive,
                    "README.txt",
                    Encoding.UTF8.GetBytes(BuildReadme(workbook != null)),
                    cancellationToken).ConfigureAwait(false);

                foreach (var evidence in evidenceFiles)
                {
                    var sourcePath = Path.Combine(
                        workspace.EvidenceProjectDirectory,
                        Path.GetFileName(evidence.Entry));
                    await WriteEntryAsync(
                        archive,
                        evidence.Entry,
                        await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false),
                        cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(temporary, fullDestination, true);
            return fullDestination;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static async Task VerifySourceBundleAsync(
        ZipArchive archive,
        JsonElement manifest,
        CancellationToken cancellationToken)
    {
        if (manifest.TryGetProperty("sourceFiles", out var sourceFiles) && sourceFiles.ValueKind == JsonValueKind.Array && sourceFiles.GetArrayLength() > 0)
        {
            var sources = sourceFiles.Deserialize<List<IoFatPackageSource>>(JsonOptions)
                ?? throw new InvalidDataException("The ARSAS project source bundle manifest is invalid.");
            if (sources.Count == 0)
                throw new InvalidDataException("The ARSAS project source bundle is empty.");

            foreach (var source in sources)
            {
                var descriptor = IoFatSourceIdentity.Normalize(source.Descriptor);
                var entry = RequiredEntry(archive, source.Entry);
                var bytes = await ReadEntryAsync(entry, 100 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
                VerifyHash(bytes, descriptor.Sha256, $"FAT source '{descriptor.FileName}'");
                if (descriptor.Length > 0 && descriptor.Length != bytes.LongLength)
                    throw new InvalidDataException($"FAT source '{descriptor.FileName}' length does not match the project manifest.");
            }

            if (manifest.TryGetProperty("sourceSetSha256", out var sourceSet) && sourceSet.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(sourceSet.GetString()))
            {
                var actual = IoFatSourceIdentity.ComputeSetFingerprint(sources.Select(source => source.Descriptor));
                if (!actual.Equals(sourceSet.GetString(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The ARSAS project FAT source-set fingerprint is invalid.");
            }
            return;
        }

        // Legacy package: singular source workbook is still accepted and fully verified.
        if (!manifest.TryGetProperty("sourceWorkbookEntry", out var legacyEntry) || legacyEntry.ValueKind != JsonValueKind.String ||
            !manifest.TryGetProperty("sourceWorkbookSha256", out var legacyHash) || legacyHash.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(legacyEntry.GetString()) || string.IsNullOrWhiteSpace(legacyHash.GetString()))
        {
            throw new InvalidDataException("The ARSAS project contains no FAT source bundle.");
        }
        var entry = RequiredEntry(archive, legacyEntry.GetString()!);
        var bytesLegacy = await ReadEntryAsync(entry, 100 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
        VerifyHash(bytesLegacy, legacyHash.GetString()!, "legacy source workbook");
    }

    private static async Task VerifyOptionalManifestEntryAsync(
        ZipArchive archive,
        JsonElement manifest,
        string entryProperty,
        string hashProperty,
        string label,
        CancellationToken cancellationToken)
    {
        var hasEntry = manifest.TryGetProperty(entryProperty, out var entryValue) && entryValue.ValueKind == JsonValueKind.String;
        var hasHash = manifest.TryGetProperty(hashProperty, out var hashValue) && hashValue.ValueKind == JsonValueKind.String;
        if (!hasEntry && !hasHash)
            return;
        if (!hasEntry || !hasHash)
            throw new InvalidDataException($"The ARSAS project {label} manifest is incomplete.");

        var entryName = entryValue.GetString();
        var expectedHash = hashValue.GetString();
        if (string.IsNullOrWhiteSpace(entryName) && string.IsNullOrWhiteSpace(expectedHash))
            return;
        if (string.IsNullOrWhiteSpace(entryName) || string.IsNullOrWhiteSpace(expectedHash))
            throw new InvalidDataException($"The ARSAS project {label} manifest is incomplete.");
        var entry = RequiredEntry(archive, entryName);
        var bytes = await ReadEntryAsync(entry, 100 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
        VerifyHash(bytes, expectedHash, label);
    }

    private static string NormalizeDestination(string destinationPath)
    {
        var fullPath = Path.GetFullPath(destinationPath);
        if (fullPath.EndsWith(PackageExtension, StringComparison.OrdinalIgnoreCase))
            return fullPath;
        if (fullPath.EndsWith(LegacyPackageExtension, StringComparison.OrdinalIgnoreCase))
            return fullPath[..^LegacyPackageExtension.Length] + PackageExtension;
        return fullPath + PackageExtension;
    }

    private static ZipArchiveEntry RequiredEntry(ZipArchive archive, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(name))
            throw new InvalidDataException("The ARSAS project contains an unsafe entry path.");
        return archive.GetEntry(name.Replace('\\', '/'))
            ?? throw new InvalidDataException($"The ARSAS project entry '{name}' is missing.");
    }

    private static async Task<byte[]> ReadEntryAsync(
        ZipArchiveEntry entry,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (entry.Length > maximumBytes)
            throw new InvalidDataException($"ARSAS project entry '{entry.FullName}' exceeds its safety limit.");
        await using var source = entry.Open();
        using var memory = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
        await source.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        if (memory.Length > maximumBytes)
            throw new InvalidDataException($"ARSAS project entry '{entry.FullName}' exceeds its safety limit.");
        return memory.ToArray();
    }

    private static async Task WriteEntryAsync(
        ZipArchive archive,
        string entryName,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName.Replace('\\', '/'), CompressionLevel.Optimal);
        await using var destination = entry.Open();
        await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static void VerifyHash(byte[] bytes, string expected, string label)
    {
        var actual = HashBytes(bytes);
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The {label} SHA-256 does not match the project manifest.");
    }

    private static string HashBytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string HashFile(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string BuildReadme(bool hasWorkbook) =>
        "ARSAS IO FAT portable project\r\n\r\n" +
        "Project file extension: .arsas\r\n" +
        "To continue testing: open this file from FAT / IO List Testing > Open ARSAS Project.\r\n" +
        "To review or print evidence: extract report/IO-FAT-Report.pdf.\r\n" +
        (hasWorkbook
            ? "To review results in Excel: extract report/IO-FAT-Results.xlsx.\r\n"
            : "This project is SCL-backed, so no workbook-derived Excel result copy is generated.\r\n") +
        "The PDF is generated directly by the built-in native ARSAS PDF engine ported from ARIEC60870.\r\n" +
        "The package contains the complete FAT source bundle, project snapshot, and verified evidence journals.\r\n";

    private sealed record PackageManifest(
        string PackageVersion,
        string PackageKind,
        DateTimeOffset CreatedAtUtc,
        string ProjectId,
        string ProjectName,
        string SchemaVersion,
        string SnapshotEntry,
        string SnapshotSha256,
        string SourceWorkbookEntry,
        string SourceWorkbookSha256,
        string ReportEntry,
        string ReportSha256,
        string ResultWorkbookEntry,
        string ResultWorkbookSha256,
        List<PackageEvidence> EvidenceFiles)
    {
        public string SourceSetSha256 { get; init; } = string.Empty;
        public List<IoFatPackageSource> SourceFiles { get; init; } = new();
    }

    private sealed record PackageEvidence(
        string Entry,
        string Sha256,
        long RecordCount,
        string LastHash);
}
