using System.Security.Cryptography;
using System.Text;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record FatSclWorkspaceSource(
    string SourceFileName,
    string SourceSha256,
    SclIedWorkspace Workspace);

public sealed record FatSclSourceArtifact(
    string SourceFileName,
    string SourceSha256,
    string IedName,
    string AccessPointName);

public sealed record FatSclImportResult(
    FatVerificationProject Project,
    IReadOnlyList<FatSclSourceArtifact> Sources,
    string SourceFingerprint);

/// <summary>
/// Aggregates one or many engine-created SCL workspaces into a FAT v2 project.
/// The caller owns file selection and SclWorkspaceService.OpenAsync; this layer only consumes
/// ARIEC workspaces and never parses XML itself.
/// </summary>
public static class FatSclWorkspaceImportService
{
    public static FatSclImportResult Import(IEnumerable<FatSclWorkspaceSource> workspaceSources)
    {
        ArgumentNullException.ThrowIfNull(workspaceSources);
        var supplied = workspaceSources.ToArray();
        if (supplied.Length == 0)
            throw new InvalidDataException("Select at least one SCL workspace for FAT import.");

        foreach (var source in supplied)
            ValidateSource(source);

        var selected = new List<FatSclWorkspaceSource>();
        foreach (var identityGroup in supplied.GroupBy(
                     source => WorkspaceIdentity(source.Workspace),
                     StringComparer.OrdinalIgnoreCase))
        {
            var distinctHashes = identityGroup
                .Select(source => NormalizeHash(source.SourceSha256))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (distinctHashes.Length > 1)
            {
                throw new InvalidDataException(
                    $"Conflicting SCL sources define the same IED/AccessPoint '{identityGroup.Key}'. " +
                    "FAT import will not silently merge competing engineering authorities.");
            }

            // The same file may be selected twice or supplied through two equivalent paths.
            // Exact content duplicates are harmless and collapse to one workspace identity.
            selected.Add(identityGroup
                .OrderBy(source => source.SourceFileName, StringComparer.OrdinalIgnoreCase)
                .First());
        }

        var rows = selected
            .SelectMany(source => FatDataSetSignalProjectionService.Project(source.Workspace))
            .OrderBy(signal => signal.IedName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(signal => signal.AccessPointName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(signal => signal.DataSetReference, StringComparer.OrdinalIgnoreCase)
            .ThenBy(signal => signal.DataSetMemberIndex)
            .ToList();

        var duplicateSignal = rows
            .GroupBy(signal => signal.SignalId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSignal is not null)
            throw new InvalidDataException($"Duplicate FAT signal identity '{duplicateSignal.Key}' was produced during SCL aggregation.");

        var sources = selected
            .Select(source => new FatSclSourceArtifact(
                source.SourceFileName.Trim(),
                NormalizeHash(source.SourceSha256),
                source.Workspace.IedName.Trim(),
                source.Workspace.AccessPointName.Trim()))
            .OrderBy(source => source.SourceSha256, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.IedName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.AccessPointName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.SourceFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new FatSclImportResult(
            new FatVerificationProject { Signals = rows },
            sources,
            BuildFingerprint(sources));
    }

    private static void ValidateSource(FatSclWorkspaceSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(source.Workspace);
        ArgumentNullException.ThrowIfNull(source.Workspace.DesignModel);
        if (string.IsNullOrWhiteSpace(source.SourceFileName))
            throw new InvalidDataException("SCL source file name is required for FAT provenance.");
        if (string.IsNullOrWhiteSpace(source.Workspace.IedName) ||
            string.IsNullOrWhiteSpace(source.Workspace.AccessPointName))
        {
            throw new InvalidDataException("Every FAT SCL workspace requires explicit IED and AccessPoint identity.");
        }

        var hash = NormalizeHash(source.SourceSha256);
        if (hash.Length != 64 || hash.Any(ch => !Uri.IsHexDigit(ch)))
            throw new InvalidDataException($"SCL source '{source.SourceFileName}' does not have a valid SHA-256 provenance hash.");
    }

    private static string BuildFingerprint(IEnumerable<FatSclSourceArtifact> sources)
    {
        // File ordering must not affect project identity. File names are deliberately omitted
        // from the hash so identical bytes selected from a renamed path remain the same source.
        var canonical = string.Join("\n", sources
            .Select(source => string.Join("|", new[]
            {
                source.SourceSha256.ToLowerInvariant(),
                source.IedName.Trim().ToLowerInvariant(),
                source.AccessPointName.Trim().ToLowerInvariant()
            }))
            .OrderBy(value => value, StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string WorkspaceIdentity(SclIedWorkspace workspace)
        => $"{workspace.IedName.Trim()}|{workspace.AccessPointName.Trim()}";

    private static string NormalizeHash(string value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();
}
