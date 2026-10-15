using System.IO;
using AR.Iec61850.FaultRecords;
using AR.Iec61850.Mms;

namespace ArIED61850Tester.Models;

/// <summary>
/// Immutable FileDirectory→complete-record authority. A path is selectable
/// only when one unique engine-discovered record owns it; case-folding is
/// allowed only for MMS FileDirectory interoperability, never for ambiguous
/// collisions. File metadata is not evidence of a completed local download.
/// </summary>
public sealed class IedBrowserFileCatalog
{
    private readonly Dictionary<string, Iec61850FaultRecordSet?> _recordsByRemotePath =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<IedBrowserRemoteFile> Files { get; }

    /// <summary>
    /// One display row per engine-recognized COMTRADE record, never one row
    /// per CFG/DAT/HDR/INF. Unclassified MMS entries remain visible without
    /// gaining download permission. This is a projection, not another catalog.
    /// </summary>
    public IReadOnlyList<IedBrowserRemoteFile> DisplayRows { get; }

    public IedBrowserFileCatalog(Iec61850FaultRecordCatalog catalog,
        IEnumerable<MmsFileDirectoryEntry>? rootEntries = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var displayed = new Dictionary<string, IedBrowserRemoteFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in catalog.Records)
        foreach (var file in record.Files)
        {
            if (string.IsNullOrWhiteSpace(file.RemotePath)) continue;
            if (_recordsByRemotePath.TryGetValue(file.RemotePath, out var prior))
            {
                if (!ReferenceEquals(prior, record))
                    _recordsByRemotePath[file.RemotePath] = null; // ambiguous: fail closed
                continue;
            }
            _recordsByRemotePath[file.RemotePath] = record;
            displayed.Add(file.RemotePath, new IedBrowserRemoteFile(
                file.Name, file.RemotePath, file.SizeBytes, file.LastModifiedUtc));
        }
        // The fault-record catalog is intentionally filtered to COMTRADE and
        // supported packages. Browse *all* actual MMS root entries as ordinary
        // inventory, but do not grant download eligibility to these additions.
        // The existing record association alone authorizes CFG/DAT transfers.
        if (rootEntries is not null)
            foreach (var entry in rootEntries)
            {
                if (string.IsNullOrWhiteSpace(entry.Path) ||
                    displayed.ContainsKey(entry.Path))
                    continue;
                displayed.Add(entry.Path, new IedBrowserRemoteFile(
                    string.IsNullOrWhiteSpace(entry.Name) ? entry.Path : entry.Name,
                    entry.Path, entry.SizeBytes, null, entry.IsLikelyDirectory));
            }
        Files = displayed.Values
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.RemotePath, StringComparer.Ordinal)
            .ToArray();

        var memberPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bundles = new List<IedBrowserRemoteFile>(catalog.Records.Count);
        foreach (var record in catalog.Records)
        {
            // Only engine-declared members may be collapsed into one bundle.
            // Keep the CFG as the stable representative for the existing
            // TryGetRecord/download integrity and ambiguity fences.
            var configuration = record.Files.FirstOrDefault(file =>
                file.Kind == Iec61850FaultRecordFileKind.Configuration);
            var representative = configuration ?? record.Files.FirstOrDefault();
            if (representative is null ||
                string.IsNullOrWhiteSpace(representative.RemotePath))
                continue;
            foreach (var file in record.Files)
                if (!string.IsNullOrWhiteSpace(file.RemotePath))
                    memberPaths.Add(file.RemotePath);
            bundles.Add(new IedBrowserRemoteFile(
                record.BaseName, representative.RemotePath,
                record.HasUnknownSize ? null : record.KnownSizeBytes,
                record.LastModifiedUtc, IsBundle: true,
                BundleMemberCount: record.Files.Count));
        }
        DisplayRows = bundles.Concat(Files.Where(file =>
                !memberPaths.Contains(file.RemotePath)))
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.RemotePath, StringComparer.Ordinal)
            .ToArray();
    }

    public bool TryGetRecord(string? remotePath, out Iec61850FaultRecordSet record,
        out string reason)
    {
        record = null!;
        if (string.IsNullOrWhiteSpace(remotePath) ||
            !_recordsByRemotePath.TryGetValue(remotePath, out var selected))
        {
            reason = "The selected file is not present in the verified FileDirectory catalog.";
            return false;
        }
        if (selected is null)
        {
            reason = "Ambiguous remote file identity: multiple records claim this path.";
            return false;
        }
        if (!selected.IsComplete || selected.Files.Count == 0)
        {
            reason = $"Record {selected.BaseName} is incomplete: {selected.Completeness}.";
            return false;
        }
        // The engine also marks CFF/ZIP/vendor packages as complete; that is
        // download eligibility, NOT proof the current native CFG/DAT viewer can
        // interpret them. Require both concrete complementary files here.
        if (!selected.Files.Any(file => file.Kind == Iec61850FaultRecordFileKind.Configuration) ||
            !selected.Files.Any(file => file.Kind == Iec61850FaultRecordFileKind.Data))
        {
            reason = "The native COMTRADE viewer requires a complete CFG + DAT pair.";
            return false;
        }
        record = selected;
        reason = "";
        return true;
    }
}

/// <summary>
/// Validate an already downloaded local record without touching the relay.
/// A file's existence alone is not enough: every engine-declared file must
/// be present, and every available declared size must match. A partial
/// bundle is never sent to the native viewer.
/// </summary>
public static class IedBrowserLocalRecordEvidence
{
    public static bool TryFindComplete(
        Iec61850FaultRecordSet record, string destinationRoot, out string directory)
    {
        directory = "";
        if (!record.IsComplete || record.Files.Count == 0 ||
            string.IsNullOrWhiteSpace(destinationRoot) || !Directory.Exists(destinationRoot))
            return false;
        var expectedName = Sanitize(record.BaseName);
        try
        {
            foreach (var candidate in Directory.EnumerateDirectories(destinationRoot, "*",
                         SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(candidate);
                if (!name.Equals(expectedName, StringComparison.OrdinalIgnoreCase) &&
                    !(name.StartsWith(expectedName + "-", StringComparison.OrdinalIgnoreCase) &&
                      int.TryParse(name.AsSpan(expectedName.Length + 1), out var suffix) && suffix > 0))
                    continue;

                // Paths are already bounded to one first-level directory, and
                // every filename must be the same sanitized name as the downloader.
                if (record.Files.All(file =>
                {
                    var localFile = Path.Combine(candidate, Sanitize(file.Name));
                    if (!File.Exists(localFile)) return false;
                    var declared = file.SizeBytes;
                    return declared is not > 0 || new FileInfo(localFile).Length == declared.Value;
                }))
                {
                    directory = candidate;
                    return true;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Inaccessible or untrustworthy local content is never opened.
        }
        return false;
    }

    public static bool ValidateFresh(Iec61850FaultRecordSet record,
        Iec61850FaultRecordDownloadResult result, out string reason)
    {
        reason = "";
        if (!result.IsSuccess || !record.IsComplete ||
            string.IsNullOrWhiteSpace(result.DestinationDirectory) ||
            !Directory.Exists(result.DestinationDirectory))
        {
            reason = result.IsSuccess ? "Transfer returned no complete record directory." : result.Message;
            return false;
        }
        var transferred = result.Files.ToDictionary(file => file.RemotePath,
            StringComparer.OrdinalIgnoreCase);
        foreach (var file in record.Files)
        {
            if (!transferred.TryGetValue(file.RemotePath, out var local) ||
                !File.Exists(local.LocalPath) ||
                !Path.GetFullPath(local.LocalPath).StartsWith(
                    Path.GetFullPath(result.DestinationDirectory) + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                (file.SizeBytes is > 0 && new FileInfo(local.LocalPath).Length != file.SizeBytes.Value))
            {
                reason = $"Transfer verification failed for {file.Name}: missing, unexpected path or size.";
                return false;
            }
        }
        return true;
    }

    private static string Sanitize(string value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "fault-record" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var chars = source.Select(c => c < ' ' || invalid.Contains(c) ? '_' : c).ToArray();
        var output = new string(chars).Trim().TrimEnd('.');
        return output.Length == 0 ? "fault-record" : output;
    }
}
