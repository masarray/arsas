using System.Security.Cryptography;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record FatSclWorkspaceLaunchResult(
    FatVerificationProject Project,
    IReadOnlyList<IoFatWorkspaceSource> SourceFiles,
    string SourceSetSha256,
    string EngineeringFingerprint,
    string WorkspaceDirectory);

/// <summary>
/// Opens one or many immutable SCL sources through the ARIEC workspace authority and creates
/// a FAT v2 project containing exactly the static DataSet membership rows projected by P1.
/// ARSAS does not parse or rewrite XML in this path.
/// </summary>
public sealed class FatSclWorkspaceBootstrapService
{
    private readonly SclWorkspaceService _sclWorkspaceService;

    public FatSclWorkspaceBootstrapService(SclWorkspaceService? sclWorkspaceService = null)
    {
        _sclWorkspaceService = sclWorkspaceService ?? new SclWorkspaceService();
    }

    public async Task<FatSclWorkspaceLaunchResult> OpenAsync(
        IReadOnlyCollection<string> sclPaths,
        string localProjectsRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sclPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(localProjectsRoot);
        if (sclPaths.Count == 0)
            throw new InvalidDataException("Select at least one SCL file for FAT v2.");

        var normalizedPaths = sclPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedPaths.Length == 0)
            throw new InvalidDataException("Select at least one SCL file for FAT v2.");

        var described = await IoFatSourceWorkspaceService.DescribeAsync(
            normalizedPaths
                .Select(path => new IoFatSourceInput(path, IoFatSourceKinds.Scl))
                .ToArray(),
            cancellationToken).ConfigureAwait(false);

        // Selecting the exact same source twice (for example through two equivalent paths)
        // must not duplicate the immutable source bundle.
        var uniqueSources = described
            .GroupBy(item => item.Source.SourceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.OriginalPath, StringComparer.OrdinalIgnoreCase).First())
            .OrderBy(item => item.Source.SourceId, StringComparer.Ordinal)
            .ToArray();

        var workspaceSources = new List<FatSclWorkspaceSource>();
        foreach (var source in uniqueSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = await _sclWorkspaceService.OpenAsync(
                source.OriginalPath,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!document.SourceSha256.Equals(source.Source.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"ARIEC SCL provenance for '{source.Source.FileName}' does not match the immutable FAT source SHA-256.");
            }

            foreach (var workspace in document.Ieds)
            {
                workspaceSources.Add(new FatSclWorkspaceSource(
                    source.Source.FileName,
                    source.Source.Sha256,
                    workspace));
            }
        }

        if (workspaceSources.Count == 0)
            throw new InvalidDataException("The selected SCL source set contains no IED/AccessPoint workspace for FAT.");

        var import = FatSclWorkspaceImportService.Import(workspaceSources);
        if (import.Project.Signals.Count == 0)
        {
            throw new InvalidDataException(
                "The selected SCL source set contains no static DataSet membership. FAT v2 will not invent rows outside the engineering authority.");
        }

        var sourceSetSha256 = IoFatSourceIdentity.ComputeSetFingerprint(uniqueSources.Select(item => item.Source));
        if (string.IsNullOrWhiteSpace(sourceSetSha256))
            throw new InvalidDataException("The SCL source-set identity could not be established.");

        var workspaceDirectory = Path.Combine(
            Path.GetFullPath(localProjectsRoot),
            "fat-v2",
            sourceSetSha256[..24]);
        var stagedSources = await StageImmutableSourcesAsync(
            uniqueSources,
            Path.Combine(workspaceDirectory, "source"),
            cancellationToken).ConfigureAwait(false);

        return new FatSclWorkspaceLaunchResult(
            import.Project,
            stagedSources,
            sourceSetSha256,
            import.SourceFingerprint,
            workspaceDirectory);
    }

    private static async Task<IReadOnlyList<IoFatWorkspaceSource>> StageImmutableSourcesAsync(
        IReadOnlyCollection<IoFatDescribedSource> described,
        string sourceDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(sourceDirectory);
        var staged = new List<IoFatWorkspaceSource>(described.Count);
        foreach (var item in described.OrderBy(item => item.Source.SourceId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packageEntry = IoFatSourceIdentity.BuildPackageEntry(item.Source);
            var localPath = Path.Combine(sourceDirectory, Path.GetFileName(packageEntry));
            var bytes = await File.ReadAllBytesAsync(item.OriginalPath, cancellationToken).ConfigureAwait(false);
            VerifyHash(bytes, item.Source.Sha256, item.Source.FileName);

            if (File.Exists(localPath))
            {
                var existing = await File.ReadAllBytesAsync(localPath, cancellationToken).ConfigureAwait(false);
                VerifyHash(existing, item.Source.Sha256, item.Source.FileName);
            }
            else
            {
                var temporary = localPath + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
                    File.Move(temporary, localPath, false);
                }
                finally
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
            }

            staged.Add(new IoFatWorkspaceSource(item.Source, localPath, packageEntry));
        }

        return staged;
    }

    private static void VerifyHash(byte[] bytes, string expectedSha256, string fileName)
    {
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"SCL source '{fileName}' changed while the FAT v2 workspace was being created.");
        }
    }
}
