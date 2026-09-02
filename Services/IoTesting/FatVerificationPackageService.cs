using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArIED61850Tester.Services.IoTesting;

public static class FatVerificationPackageService
{
    public const string PackageExtension = ".arsas";
    private const long MaximumPackageBytes = 500L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private sealed record Manifest(
        string PackageVersion,
        string PackageKind,
        DateTimeOffset CreatedAtUtc,
        string ProjectId,
        string SourceSetSha256,
        string EngineeringFingerprint,
        string SnapshotEntry,
        string SnapshotSha256,
        string PdfEntry,
        string PdfSha256,
        string ExcelEntry,
        string ExcelSha256,
        List<IoFatPackageSource> SourceFiles);

    public static async Task<string> ExportAsync(
        FatSclWorkspaceLaunchResult launch,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (launch.SourceFiles.Count == 0)
            throw new InvalidDataException("FAT v2 project contains no immutable SCL source bundle.");

        FatVerificationPersistenceService.SaveNow(launch);
        var snapshotBytes = await File.ReadAllBytesAsync(
            FatVerificationPersistenceService.SnapshotPath(launch), cancellationToken).ConfigureAwait(false);
        var pdfBytes = FatVerificationReportService.GeneratePdf(launch);
        var excelBytes = FatVerificationReportService.GenerateXlsx(launch);
        var sourceFiles = IoFatSourceWorkspaceService.ToPackageSources(launch.SourceFiles).ToList();
        var manifest = new Manifest(
            "fat-v2.1",
            "fat-v2",
            DateTimeOffset.UtcNow,
            launch.Project.ProjectId,
            launch.SourceSetSha256,
            launch.EngineeringFingerprint,
            FatVerificationPersistenceService.SnapshotFileName,
            Hash(snapshotBytes),
            "report/FAT-v2-Report.pdf",
            Hash(pdfBytes),
            "report/FAT-v2-Results.xlsx",
            Hash(excelBytes),
            sourceFiles);

        var destination = Path.GetFullPath(destinationPath);
        if (!destination.EndsWith(PackageExtension, StringComparison.OrdinalIgnoreCase))
            destination += PackageExtension;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                await WriteAsync(archive, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions), cancellationToken).ConfigureAwait(false);
                await WriteAsync(archive, manifest.SnapshotEntry, snapshotBytes, cancellationToken).ConfigureAwait(false);
                foreach (var source in launch.SourceFiles)
                {
                    var bytes = await IoFatSourceWorkspaceService.ReadVerifiedAsync(source, cancellationToken).ConfigureAwait(false);
                    await WriteAsync(archive, source.PackageEntry, bytes, cancellationToken).ConfigureAwait(false);
                }
                await WriteAsync(archive, manifest.PdfEntry, pdfBytes, cancellationToken).ConfigureAwait(false);
                await WriteAsync(archive, manifest.ExcelEntry, excelBytes, cancellationToken).ConfigureAwait(false);
                await WriteAsync(archive, "README.txt", Encoding.UTF8.GetBytes(
                    "ARSAS FAT v2 portable project\r\n\r\n" +
                    "This project is driven by immutable IEC 61850 SCL source files.\r\n" +
                    "FAT rows are recreated from ARIEC static DataSet authority before saved operator disposition and Value 1 / Value 2 evidence are restored.\r\n" +
                    "Reports: report/FAT-v2-Report.pdf and report/FAT-v2-Results.xlsx\r\n"), cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, destination, true);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static async Task<FatSclWorkspaceLaunchResult> OpenAsync(
        string packagePath,
        string localProjectsRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(localProjectsRoot);
        var info = new FileInfo(packagePath);
        if (!info.Exists)
            throw new FileNotFoundException("FAT v2 ARSAS project was not found.", packagePath);
        if (info.Length > MaximumPackageBytes)
            throw new InvalidDataException("FAT v2 ARSAS project exceeds the 500 MB safety limit.");

        using var archive = ZipFile.OpenRead(packagePath);
        var manifestBytes = await ReadAsync(RequiredEntry(archive, "manifest.json"), 5 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<Manifest>(manifestBytes, JsonOptions)
            ?? throw new InvalidDataException("FAT v2 project manifest is invalid.");
        if (!manifest.PackageKind.Equals("fat-v2", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"ARSAS project kind '{manifest.PackageKind}' is not a FAT v2 project.");
        if (manifest.SourceFiles.Count == 0)
            throw new InvalidDataException("FAT v2 project contains no SCL source bundle.");

        var snapshotBytes = await ReadAsync(RequiredEntry(archive, manifest.SnapshotEntry), 50 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
        Verify(snapshotBytes, manifest.SnapshotSha256, "FAT v2 snapshot");
        Verify(await ReadAsync(RequiredEntry(archive, manifest.PdfEntry), 100 * 1024 * 1024, cancellationToken).ConfigureAwait(false), manifest.PdfSha256, "FAT v2 PDF report");
        Verify(await ReadAsync(RequiredEntry(archive, manifest.ExcelEntry), 100 * 1024 * 1024, cancellationToken).ConfigureAwait(false), manifest.ExcelSha256, "FAT v2 Excel report");

        var sourceSet = IoFatSourceIdentity.ComputeSetFingerprint(manifest.SourceFiles.Select(source => source.Descriptor));
        if (!sourceSet.Equals(manifest.SourceSetSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("FAT v2 source-set fingerprint is invalid.");

        var extractionRoot = Path.Combine(Path.GetTempPath(), "ARSAS", "FAT-v2-import", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractionRoot);
        try
        {
            var sourcePaths = new List<string>();
            foreach (var source in manifest.SourceFiles.OrderBy(source => source.Descriptor.SourceId, StringComparer.Ordinal))
            {
                var descriptor = IoFatSourceIdentity.Normalize(source.Descriptor);
                if (!descriptor.Kind.Equals(IoFatSourceKinds.Scl, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"FAT v2 package source '{descriptor.FileName}' is not an SCL source.");
                var bytes = await ReadAsync(RequiredEntry(archive, source.Entry), 100 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
                Verify(bytes, descriptor.Sha256, $"SCL source '{descriptor.FileName}'");
                if (descriptor.Length > 0 && descriptor.Length != bytes.LongLength)
                    throw new InvalidDataException($"SCL source '{descriptor.FileName}' length does not match the manifest.");

                // SourceId includes the engineering filename. Keep that basename exactly while
                // using a per-source directory to avoid collisions between equal filenames.
                var sourceDirectory = Path.Combine(extractionRoot, descriptor.SourceId);
                Directory.CreateDirectory(sourceDirectory);
                var path = Path.Combine(sourceDirectory, descriptor.FileName);
                await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
                sourcePaths.Add(path);
            }

            var workspaceDirectory = Path.Combine(
                Path.GetFullPath(localProjectsRoot),
                "fat-v2",
                manifest.SourceSetSha256[..24]);
            Directory.CreateDirectory(workspaceDirectory);
            var snapshotPath = Path.Combine(workspaceDirectory, FatVerificationPersistenceService.SnapshotFileName);
            var temporarySnapshot = snapshotPath + ".tmp-" + Guid.NewGuid().ToString("N");
            await File.WriteAllBytesAsync(temporarySnapshot, snapshotBytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporarySnapshot, snapshotPath, true);

            var launch = await new FatSclWorkspaceBootstrapService().OpenAsync(
                sourcePaths,
                localProjectsRoot,
                cancellationToken).ConfigureAwait(false);
            if (!launch.SourceSetSha256.Equals(manifest.SourceSetSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Reopened FAT v2 project source identity changed after authoritative SCL import.");
            if (!launch.EngineeringFingerprint.Equals(manifest.EngineeringFingerprint, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Reopened FAT v2 project engineering fingerprint does not match the packaged projection.");
            return launch;
        }
        finally
        {
            if (Directory.Exists(extractionRoot))
                Directory.Delete(extractionRoot, recursive: true);
        }
    }

    private static ZipArchiveEntry RequiredEntry(ZipArchive archive, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(name))
            throw new InvalidDataException("FAT v2 package contains an unsafe entry path.");
        return archive.GetEntry(name.Replace('\\', '/'))
            ?? throw new InvalidDataException($"FAT v2 package entry '{name}' is missing.");
    }

    private static async Task<byte[]> ReadAsync(ZipArchiveEntry entry, long maximumBytes, CancellationToken cancellationToken)
    {
        if (entry.Length > maximumBytes)
            throw new InvalidDataException($"FAT v2 package entry '{entry.FullName}' exceeds its safety limit.");
        await using var source = entry.Open();
        using var memory = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
        await source.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        if (memory.Length > maximumBytes)
            throw new InvalidDataException($"FAT v2 package entry '{entry.FullName}' exceeds its safety limit.");
        return memory.ToArray();
    }

    private static async Task WriteAsync(ZipArchive archive, string name, byte[] bytes, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name.Replace('\\', '/'), CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static string Hash(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void Verify(byte[] bytes, string expected, string label)
    {
        if (!Hash(bytes).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{label} SHA-256 does not match the FAT v2 project manifest.");
    }
}
