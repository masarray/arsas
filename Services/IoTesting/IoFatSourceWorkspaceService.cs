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

public static class IoFatSourceWorkspaceService
{
    public static async Task<IReadOnlyList<IoFatWorkspaceSource>> StageAsync(
        IoTestProject project,
        IReadOnlyCollection<IoFatSourceInput> inputs,
        string sourceDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        if (inputs.Count == 0)
            throw new InvalidDataException("A FAT workspace must contain at least one source file.");

        var described = new List<(IoFatSourceDescriptor Descriptor, string OriginalPath)>();
        foreach (var input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            described.Add((
                await IoFatSourceIdentity.DescribeAsync(input, cancellationToken).ConfigureAwait(false),
                input.FilePath));
        }

        IoFatSourceIdentity.AttachOrValidate(project, described.Select(item => item.Descriptor).ToArray());
        Directory.CreateDirectory(sourceDirectory);
        var staged = new List<IoFatWorkspaceSource>(described.Count);
        foreach (var item in described.OrderBy(item => item.Descriptor.SourceId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packageEntry = IoFatSourceIdentity.BuildPackageEntry(item.Descriptor);
            var localPath = Path.Combine(sourceDirectory, Path.GetFileName(packageEntry));
            await CopyVerifiedAsync(item.OriginalPath, localPath, item.Descriptor.Sha256, cancellationToken).ConfigureAwait(false);
            staged.Add(new IoFatWorkspaceSource(item.Descriptor, localPath, packageEntry));
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
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        VerifyHash(bytes, expectedSha256, $"source '{Path.GetFileName(sourcePath)}'");
        await WriteFileAtomicAsync(destination, bytes, cancellationToken).ConfigureAwait(false);
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
