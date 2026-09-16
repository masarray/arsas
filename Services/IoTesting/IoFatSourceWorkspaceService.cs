using System.IO.Compression;
using System.Security.Cryptography;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record IoFatPackageSource(
    string Entry,
    string SourceId,
    string Kind,
    string FileName,
    string Sha256,
    long Length)
{
    public IoFatSourceDescriptor Descriptor => new(SourceId, Kind, FileName, Sha256, Length);
}

public sealed record IoFatDescribedSource(
    IoFatSourceDescriptor Source,
    string OriginalPath);

public static class IoFatSourceWorkspaceService
{
    public static async Task<IReadOnlyList<IoFatDescribedSource>> DescribeAsync(
        IReadOnlyCollection<IoFatSourceInput> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0)
            throw new InvalidDataException("A FAT workspace must contain at least one source file.");

        var described = new List<IoFatDescribedSource>(inputs.Count);
        foreach (var input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            described.Add(new IoFatDescribedSource(
                await IoFatSourceIdentity.DescribeAsync(input, cancellationToken).ConfigureAwait(false),
                input.FilePath));
        }
        return described;
    }

    public static async Task<IReadOnlyList<IoFatWorkspaceSource>> StageAsync(
        IoTestProject project,
        IReadOnlyCollection<IoFatSourceInput> inputs,
        string sourceDirectory,
        CancellationToken cancellationToken = default)
        => await StageDescribedAsync(
            project,
            await DescribeAsync(inputs, cancellationToken).ConfigureAwait(false),
            sourceDirectory,
            cancellationToken).ConfigureAwait(false);

    public static async Task<IReadOnlyList<IoFatWorkspaceSource>> StageDescribedAsync(
        IoTestProject project,
        IReadOnlyCollection<IoFatDescribedSource> described,
        string sourceDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(described);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        if (described.Count == 0)
            throw new InvalidDataException("A FAT workspace must contain at least one source file.");

        IoFatSourceIdentity.AttachOrValidate(project, described.Select(item => item.Source).ToArray());
        Directory.CreateDirectory(sourceDirectory);
        var staged = new List<IoFatWorkspaceSource>(described.Count);
        foreach (var item in described.OrderBy(item => item.Source.SourceId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packageEntry = IoFatSourceIdentity.BuildPackageEntry(item.Source);
            var localPath = Path.Combine(sourceDirectory, Path.GetFileName(packageEntry));
            await CopyVerifiedAsync(
                item.OriginalPath,
                localPath,
                item.Source.Sha256,
                item.Source.Length,
                cancellationToken).ConfigureAwait(false);
            staged.Add(new IoFatWorkspaceSource(item.Source, localPath, packageEntry));
        }
        return staged;
    }

    public static async Task<IReadOnlyList<IoFatWorkspaceSource>> ImportAsync(
        IoTestProject project,
        ZipArchive archive,
        IReadOnlyCollection<IoFatPackageSource> packageSources,
        string legacyWorkbookEntry,
        string legacyWorkbookSha256,
        string sourceDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(packageSources);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);

        var sources = packageSources.ToList();
        if (sources.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(legacyWorkbookEntry) || string.IsNullOrWhiteSpace(legacyWorkbookSha256))
                throw new InvalidDataException("The FAT project contains no source bundle.");
            var descriptor = IoFatSourceIdentity.LegacyWorkbook(project.SourceWorkbookName, legacyWorkbookSha256);
            sources.Add(new IoFatPackageSource(
                legacyWorkbookEntry,
                descriptor.SourceId,
                descriptor.Kind,
                descriptor.FileName,
                descriptor.Sha256,
                0));
        }

        var descriptors = sources.Select(source => IoFatSourceIdentity.Normalize(source.Descriptor)).ToArray();
        IoFatSourceIdentity.AttachOrValidate(project, descriptors);
        Directory.CreateDirectory(sourceDirectory);
        var imported = new List<IoFatWorkspaceSource>(sources.Count);
        foreach (var packageSource in sources.OrderBy(source => source.SourceId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = IoFatSourceIdentity.Normalize(packageSource.Descriptor);
            var entry = RequiredEntry(archive, packageSource.Entry);
            var bytes = await ReadEntryAsync(entry, 100L * 1024 * 1024, cancellationToken).ConfigureAwait(false);
            VerifyHash(bytes, descriptor.Sha256, $"source '{descriptor.FileName}'");
            if (descriptor.Length > 0 && bytes.LongLength != descriptor.Length)
                throw new InvalidDataException($"Source '{descriptor.FileName}' length does not match the project manifest.");

            var canonicalEntry = IoFatSourceIdentity.BuildPackageEntry(descriptor);
            var localPath = Path.Combine(sourceDirectory, Path.GetFileName(canonicalEntry));
            await WriteFileAtomicAsync(localPath, bytes, cancellationToken).ConfigureAwait(false);
            imported.Add(new IoFatWorkspaceSource(descriptor with { Length = bytes.LongLength }, localPath, canonicalEntry));
        }
        return imported;
    }

    public static IReadOnlyList<IoFatPackageSource> ToPackageSources(IEnumerable<IoFatWorkspaceSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return sources
            .Select(source => new IoFatPackageSource(
                source.PackageEntry,
                source.Source.SourceId,
                source.Source.Kind,
                source.Source.FileName,
                source.Source.Sha256,
                source.Source.Length))
            .OrderBy(source => source.SourceId, StringComparer.Ordinal)
            .ToArray();
    }

    public static async Task<byte[]> ReadVerifiedAsync(
        IoFatWorkspaceSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!File.Exists(source.LocalPath))
            throw new FileNotFoundException($"FAT source '{source.Source.FileName}' is missing from the local workspace.", source.LocalPath);
        var bytes = await File.ReadAllBytesAsync(source.LocalPath, cancellationToken).ConfigureAwait(false);
        VerifyHash(bytes, source.Source.Sha256, $"local source '{source.Source.FileName}'");
        return bytes;
    }

    private static async Task CopyVerifiedAsync(
        string sourcePath,
        string destination,
        string expectedSha256,
        long expectedLength,
        CancellationToken cancellationToken)
    {
        // The local FAT source path is content-addressed by SourceId/SHA-256. Re-opening the
        // same Engineering SCL must not rewrite identical bytes on every FAT-tab entry.
        // Verify the staged copy before reuse; corruption falls back to the normal source
        // read + SHA-256 verification + atomic replacement path.
        if (await IsVerifiedStagedCopyAsync(
                destination,
                expectedSha256,
                expectedLength,
                cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        VerifyHash(bytes, expectedSha256, $"source '{Path.GetFileName(sourcePath)}'");
        if (expectedLength > 0 && bytes.LongLength != expectedLength)
            throw new InvalidDataException($"The source '{Path.GetFileName(sourcePath)}' length changed after its FAT identity was described.");
        await WriteFileAtomicAsync(destination, bytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsVerifiedStagedCopyAsync(
        string path,
        string expectedSha256,
        long expectedLength,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return false;

        var info = new FileInfo(path);
        if (expectedLength > 0 && info.Length != expectedLength)
            return false;

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        return actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static ZipArchiveEntry RequiredEntry(ZipArchive archive, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(name))
            throw new InvalidDataException("The FAT project contains an unsafe source entry path.");
        return archive.GetEntry(name.Replace('\\', '/'))
            ?? throw new InvalidDataException($"The FAT project source entry '{name}' is missing.");
    }

    private static async Task<byte[]> ReadEntryAsync(
        ZipArchiveEntry entry,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (entry.Length > maximumBytes)
            throw new InvalidDataException($"FAT source entry '{entry.FullName}' exceeds its safety limit.");
        await using var source = entry.Open();
        using var memory = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
        await source.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        if (memory.Length > maximumBytes)
            throw new InvalidDataException($"FAT source entry '{entry.FullName}' exceeds its safety limit.");
        return memory.ToArray();
    }

    private static void VerifyHash(byte[] bytes, string expected, string label)
    {
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The {label} SHA-256 does not match the FAT source identity.");
    }

    private static async Task WriteFileAtomicAsync(
        string destination,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
