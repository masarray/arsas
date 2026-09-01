using System.Security.Cryptography;
using System.Text;

namespace ArIED61850Tester.Models.IoTesting;

public static class IoFatSourceKinds
{
    public const string Workbook = "workbook";
    public const string Scl = "scl";
}

public sealed record IoFatSourceDescriptor(
    string SourceId,
    string Kind,
    string FileName,
    string Sha256,
    long Length);

public sealed record IoFatSourceInput(
    string FilePath,
    string Kind);

public sealed record IoFatWorkspaceSource(
    IoFatSourceDescriptor Source,
    string LocalPath,
    string PackageEntry);

public static class IoFatSourceIdentity
{
    public static async Task<IoFatSourceDescriptor> DescribeAsync(
        IoFatSourceInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.FilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Kind);
        if (!File.Exists(input.FilePath))
            throw new FileNotFoundException("The FAT source file was not found.", input.FilePath);

        var kind = NormalizeKind(input.Kind);
        var fileName = Path.GetFileName(input.FilePath);
        await using var stream = new FileStream(
            input.FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var sha256 = Convert.ToHexString(hash).ToLowerInvariant();
        var sourceId = BuildSourceId(kind, fileName, sha256);
        return new IoFatSourceDescriptor(sourceId, kind, fileName, sha256, stream.Length);
    }

    public static IoFatSourceDescriptor LegacyWorkbook(string fileName, string sha256)
    {
        var normalizedName = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "source.xlsx" : fileName.Trim());
        var normalizedHash = NormalizeHash(sha256);
        return new IoFatSourceDescriptor(
            BuildSourceId(IoFatSourceKinds.Workbook, normalizedName, normalizedHash),
            IoFatSourceKinds.Workbook,
            normalizedName,
            normalizedHash,
            0);
    }

    public static string ComputeSetFingerprint(IEnumerable<IoFatSourceDescriptor> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var canonical = sources
            .Select(Normalize)
            .OrderBy(source => source.Kind, StringComparer.Ordinal)
            .ThenBy(source => source.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.Sha256, StringComparer.Ordinal)
            .ThenBy(source => source.SourceId, StringComparer.Ordinal)
            .Select(source => $"{source.Kind}\t{source.FileName.ToLowerInvariant()}\t{source.Sha256}\t{source.SourceId}")
            .ToArray();
        if (canonical.Length == 0)
            return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(string.Join("\n", canonical));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static string ProjectSourceFingerprint(IoTestProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!string.IsNullOrWhiteSpace(project.SourceSetSha256))
            return NormalizeHash(project.SourceSetSha256);
        if (project.Sources.Count > 0)
            return ComputeSetFingerprint(project.Sources);
        if (!string.IsNullOrWhiteSpace(project.SourceWorkbookSha256))
            return NormalizeHash(project.SourceWorkbookSha256);
        return string.Empty;
    }

    public static string ProjectStorageFingerprint(IoTestProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var effective = project.Sources.Count > 0
            ? project.Sources
            : !string.IsNullOrWhiteSpace(project.SourceWorkbookSha256)
                ? new List<IoFatSourceDescriptor> { LegacyWorkbook(project.SourceWorkbookName, project.SourceWorkbookSha256) }
                : new List<IoFatSourceDescriptor>();

        // Preserve the exact legacy workspace directory for one workbook so old local
        // snapshots continue to restore after P3. Multi-source/SCL workspaces use the
        // deterministic source-set fingerprint instead.
        if (effective.Count == 1 &&
            effective[0].Kind.Equals(IoFatSourceKinds.Workbook, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(project.SourceWorkbookSha256) &&
            effective[0].Sha256.Equals(project.SourceWorkbookSha256, StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeHash(project.SourceWorkbookSha256);
        }

        return ComputeSetFingerprint(effective);
    }

    public static void AttachOrValidate(IoTestProject project, IReadOnlyCollection<IoFatSourceDescriptor> sources)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
            throw new InvalidDataException("A FAT workspace must contain at least one source file.");

        var normalized = sources.Select(Normalize).ToList();
        var duplicate = normalized
            .GroupBy(source => source.SourceId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            throw new InvalidDataException($"FAT source '{duplicate.First().FileName}' was supplied more than once.");

        if (!string.IsNullOrWhiteSpace(project.SourceWorkbookSha256))
        {
            var workbook = normalized.FirstOrDefault(source =>
                source.Kind.Equals(IoFatSourceKinds.Workbook, StringComparison.OrdinalIgnoreCase));
            if (workbook != null &&
                !workbook.Sha256.Equals(project.SourceWorkbookSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The workbook source SHA-256 does not match the imported FAT plan.");
            }
        }

        var fingerprint = ComputeSetFingerprint(normalized);
        if (project.Sources.Count > 0)
        {
            var existing = ComputeSetFingerprint(project.Sources);
            if (!existing.Equals(fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The supplied FAT source set does not match the project source identity.");
        }
        if (!string.IsNullOrWhiteSpace(project.SourceSetSha256) &&
            !project.SourceSetSha256.Equals(fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The supplied FAT source set SHA-256 does not match the project snapshot.");
        }

        project.SetSources(normalized, fingerprint);
    }

    public static IoFatSourceDescriptor Normalize(IoFatSourceDescriptor source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var kind = NormalizeKind(source.Kind);
        var fileName = Path.GetFileName(source.FileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidDataException("A FAT source filename is missing.");
        var sha256 = NormalizeHash(source.Sha256);
        var sourceId = string.IsNullOrWhiteSpace(source.SourceId)
            ? BuildSourceId(kind, fileName, sha256)
            : source.SourceId.Trim().ToLowerInvariant();
        return new IoFatSourceDescriptor(sourceId, kind, fileName, sha256, Math.Max(0, source.Length));
    }

    public static string BuildPackageEntry(IoFatSourceDescriptor source)
    {
        var normalized = Normalize(source);
        var safeName = SafeFileName(normalized.FileName, "source.xml");
        return $"source/{normalized.SourceId}_{safeName}";
    }

    private static string BuildSourceId(string kind, string fileName, string sha256)
    {
        var canonical = $"{NormalizeKind(kind)}\n{Path.GetFileName(fileName).ToLowerInvariant()}\n{NormalizeHash(sha256)}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return hash[..24];
    }

    private static string NormalizeKind(string kind)
    {
        var normalized = (kind ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            IoFatSourceKinds.Workbook => IoFatSourceKinds.Workbook,
            IoFatSourceKinds.Scl => IoFatSourceKinds.Scl,
            _ => throw new InvalidDataException($"Unsupported FAT source kind '{kind}'.")
        };
    }

    private static string NormalizeHash(string sha256)
    {
        var normalized = (sha256 ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("A FAT source SHA-256 value is missing or invalid.");
        return normalized;
    }

    private static string SafeFileName(string? value, string fallback)
    {
        var name = Path.GetFileName(string.IsNullOrWhiteSpace(value) ? fallback : value.Trim());
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return sanitized.Length == 0 ? fallback : sanitized;
    }
}
