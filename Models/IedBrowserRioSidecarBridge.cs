using System.IO;
using AR.Iec61850.FaultRecords;
using AR.Iec61850.Mms;

namespace ArIED61850Tester.Models;

/// <summary>
/// Read-only bridge for an optional RIO sidecar listed in the SAME live MMS
/// root FileDirectory as an ARIEC-proven, complete CFG/DAT record.
/// The pinned engine currently knows the COMTRADE primary files but not RIO.
/// This adapter never invents an MMS path or extends an incomplete record,
/// and the original engine owns the single transactional download of all
/// augmented record.Files. Replace this compatibility seam when the reviewed
/// engine-side RioSidecar contract is eligible for the historical pin.
/// </summary>
public static class IedBrowserRioSidecarBridge
{
    public static Iec61850FaultRecordCatalog Attach(
        Iec61850FaultRecordCatalog catalog,
        IReadOnlyList<MmsFileDirectoryEntry> rootEntries)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(rootEntries);
        if (rootEntries.Count == 0 || catalog.Records.Count == 0)
            return catalog;

        // No basename heuristic over SCL, report metadata, or directory scans:
        // the supplement must be an actual exact-path root FileDirectory file.
        var candidates = rootEntries
            .Where(e => !e.IsLikelyDirectory && ValidRioIdentity(e))
            .ToArray();
        if (candidates.Length == 0)
            return catalog;

        // Index once: O(root entries + records). Large fault directories
        // must not repeat a linear candidate scan for every record.
        var rootByIdentity = candidates
            .GroupBy(entry => Identity(entry.Path))
            .ToDictionary(group => group.Key, group => group.Take(2).ToArray());
        var recordCounts = catalog.Records
            .GroupBy(record => (NormalizeDirectory(record.RemoteDirectory),
                record.BaseName))
            .ToDictionary(group => group.Key, group => group.Count());

        var changed = false;
        var records = new Iec61850FaultRecordSet[catalog.Records.Count];
        for (var i = 0; i < records.Length; i++)
        {
            var record = catalog.Records[i];
            records[i] = record;
            if (!record.IsComplete || !record.Files.Any(f =>
                    f.Kind == Iec61850FaultRecordFileKind.Configuration) ||
                !record.Files.Any(f => f.Kind == Iec61850FaultRecordFileKind.Data) ||
                record.Files.Any(f => f.Extension.Equals(".rio", StringComparison.OrdinalIgnoreCase)))
                continue;

            // Case-folded aliases and duplicate directory evidence are
            // ambiguous. Never choose an arbitrary first MMS file.
            var identity = (NormalizeDirectory(record.RemoteDirectory), record.BaseName);
            if (!rootByIdentity.TryGetValue(identity, out var matching) ||
                matching.Length != 1 || recordCounts[identity] != 1)
                continue;
            var entry = matching[0];
            if (record.Files.Any(f => string.Equals(f.RemotePath, entry.Path,
                    StringComparison.OrdinalIgnoreCase)))
                continue;

            var name = FileName(entry.Path);
            // The old pinned engine has no RioSidecar enum. Information is
            // only its ancillary transfer-order slot; the concrete .RIO path
            // and extension remain unchanged, never marketed as .INF.
            var sidecar = new Iec61850FaultRecordFile
            {
                Name = name, BaseName = record.BaseName,
                RemotePath = entry.Path,
                RemoteDirectory = record.RemoteDirectory,
                Extension = ".rio",
                Kind = Iec61850FaultRecordFileKind.Information,
                SizeBytes = entry.SizeBytes,
                LastModifiedRaw = entry.LastModifiedRaw.ToArray(),
                LastModifiedUtc = null // no asserted time without engine time decoder
            };
            records[i] = new Iec61850FaultRecordSet
            {
                RecordId = record.RecordId, RemoteDirectory = record.RemoteDirectory,
                BaseName = record.BaseName, IsComplete = record.IsComplete,
                Completeness = record.Completeness,
                Files = record.Files.Append(sidecar).ToArray(),
                KnownSizeBytes = checked(record.KnownSizeBytes + (long)(entry.SizeBytes ?? 0)),
                HasUnknownSize = record.HasUnknownSize || entry.SizeBytes is not > 0,
                LastModifiedUtc = record.LastModifiedUtc
            };
            changed = true;
        }
        return changed ? new Iec61850FaultRecordCatalog
        {
            Records = records,
            DirectoriesVisited = catalog.DirectoriesVisited,
            Diagnostics = catalog.Diagnostics
        } : catalog;
    }

    private static bool ValidRioIdentity(MmsFileDirectoryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Path) ||
            entry.Path.Any(char.IsControl) ||
            !Path.GetExtension(FileName(entry.Path)).Equals(".rio",
                StringComparison.OrdinalIgnoreCase))
            return false;
        var segments = entry.Path.Replace('\\', '/').Split('/');
        if (segments.Any(s => s is "." or ".."))
            return false;
        // A mismatched server-supplied Name is not a safe file identity.
        return (string.IsNullOrWhiteSpace(entry.Name) ||
                string.Equals(entry.Name, FileName(entry.Path), StringComparison.Ordinal)) &&
               !string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(FileName(entry.Path)));
    }

    private static (string Directory, string BaseName) Identity(string path)
    {
        var normalized = path.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        return (slash < 0 ? "" : NormalizeDirectory(normalized[..slash]),
            Path.GetFileNameWithoutExtension(slash < 0 ? normalized : normalized[(slash + 1)..]));
    }

    private static string FileName(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized[(normalized.LastIndexOf('/') + 1)..];
    }

    private static string NormalizeDirectory(string path)
        => (path ?? "").Replace('\\', '/').Trim('/');
}
