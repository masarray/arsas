using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArIED61850Tester.Services.IoTesting;

public static class IoFatProjectPackageService
{
    public const string PackageExtension = ".arsas";
    public const string LegacyPackageExtension = ".arsas-iofat";

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

        workspace.SaveNow();
        var snapshotBytes = await File.ReadAllBytesAsync(workspace.SnapshotPath, cancellationToken).ConfigureAwait(false);
        var sourceBytes = await File.ReadAllBytesAsync(workspace.SourceWorkbookPath, cancellationToken).ConfigureAwait(false);
        VerifyHash(sourceBytes, workspace.Project.SourceWorkbookSha256, "local source workbook");

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
        var reportEntry = "report/IO-FAT-Report.pdf";
        var manifest = new PackageManifest(
            IoTestWorkspacePersistence.PackageVersion,
            "io-fat",
            generatedAt.ToUniversalTime(),
            workspace.Project.ProjectId,
            workspace.Project.ProjectName,
            workspace.Project.SchemaVersion,
            "project.snapshot.json",
            HashBytes(snapshotBytes),
            $"source/{SafeFileName(workspace.Project.SourceWorkbookName, "source.xlsx")}",
            workspace.Project.SourceWorkbookSha256,
            reportEntry,
            HashBytes(pdfBytes),
            evidenceFiles);

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
                await WriteEntryAsync(archive, manifest.SourceWorkbookEntry, sourceBytes, cancellationToken).ConfigureAwait(false);
                await WriteEntryAsync(archive, manifest.ReportEntry, pdfBytes, cancellationToken).ConfigureAwait(false);
                await WriteEntryAsync(
                    archive,
                    "README.txt",
                    Encoding.UTF8.GetBytes(BuildReadme()),
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

    private static string NormalizeDestination(string destinationPath)
    {
        var fullPath = Path.GetFullPath(destinationPath);
        if (fullPath.EndsWith(PackageExtension, StringComparison.OrdinalIgnoreCase))
            return fullPath;
        if (fullPath.EndsWith(LegacyPackageExtension, StringComparison.OrdinalIgnoreCase))
            return fullPath[..^LegacyPackageExtension.Length] + PackageExtension;
        return fullPath + PackageExtension;
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

    private static string SafeFileName(string? value, string fallback)
    {
        var name = Path.GetFileName(string.IsNullOrWhiteSpace(value) ? fallback : value.Trim());
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return sanitized.Length == 0 ? fallback : sanitized;
    }

    private static void VerifyHash(byte[] bytes, string expected, string label)
    {
        var actual = HashBytes(bytes);
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The {label} SHA-256 does not match the project identity.");
    }

    private static string HashBytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string HashFile(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string BuildReadme() =>
        "ARSAS IO FAT portable project\r\n\r\n" +
        "Project file extension: .arsas\r\n" +
        "To continue testing: open this file from FAT / IO List Testing > Open ARSAS Project.\r\n" +
        "To review or print evidence: extract report/IO-FAT-Report.pdf.\r\n" +
        "The PDF is generated directly by the built-in native ARSAS PDF engine ported from ARIEC60870.\r\n" +
        "The package also contains the source workbook, project snapshot, and verified evidence journals.\r\n";

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
        List<PackageEvidence> EvidenceFiles);

    private sealed record PackageEvidence(
        string Entry,
        string Sha256,
        long RecordCount,
        string LastHash);
}
