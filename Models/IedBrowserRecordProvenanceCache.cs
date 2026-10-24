using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AR.Iec61850.FaultRecords;

namespace ArIED61850Tester.Models;

/// <summary>
/// Source-fenced local COMTRADE cache. File size and basename alone are NOT
/// authority to reuse a record: different IEDs can export identical names.
/// Browser downloads are stored in a deterministic endpoint+IED namespace;
/// a committed manifest binds the complete verified bundle to MMS record
/// identities, remote modification evidence and each local SHA-256 digest.
/// Unknown remote timestamps never authorize cache reuse.
/// </summary>
public static class IedBrowserRecordProvenanceCache
{
    private const string ManifestName = ".arsas-browser-record.json";
    private const int SchemaVersion = 1;

    public static string DeviceRoot(string documentsRoot, string iedName, string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(iedName);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{iedName.Trim()}\n{host.Trim().ToLowerInvariant()}\n{port}")))[..24];
        return Path.Combine(Path.GetFullPath(documentsRoot), "IED Browser", fingerprint);
    }

    public static void RecordVerifiedTransfer(
        Iec61850FaultRecordSet record, string sourceRoot, string directory)
    {
        ArgumentNullException.ThrowIfNull(record);
        EnsureChildDirectory(sourceRoot, directory);
        if (!record.IsComplete || record.Files.Count == 0)
            throw new InvalidDataException("A complete source record is required for the local cache.");
        var manifest = new Manifest
        {
            Version = SchemaVersion,
            RecordId = record.RecordId,
            Files = record.Files.Select(file =>
            {
                var path = LocalFilePath(directory, file.Name);
                if (!File.Exists(path))
                    throw new InvalidDataException($"Missing downloaded file '{file.Name}'.");
                var length = new FileInfo(path).Length;
                if (file.SizeBytes is > 0 && length != file.SizeBytes.Value)
                    throw new InvalidDataException($"Invalid downloaded file size: {file.Name}.");
                return new Entry
                {
                    RemotePath = file.RemotePath,
                    FileName = SafeName(file.Name),
                    DeclaredSize = file.SizeBytes,
                    RemoteModifiedUtc = file.LastModifiedUtc?.ToUniversalTime().ToString("O"),
                    Bytes = length,
                    Sha256 = HashFile(path)
                };
            }).ToArray()
        };
        var location = Path.Combine(directory, ManifestName);
        var temporary = location + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(manifest));
            File.Move(temporary, location, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static bool TryFindVerified(
        Iec61850FaultRecordSet record, string sourceRoot, out string directory)
    {
        directory = "";
        if (!record.IsComplete || record.Files.Count == 0 ||
            string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot) ||
            record.Files.Any(f => f.LastModifiedUtc is null ||
                string.IsNullOrWhiteSpace(f.RemotePath)))
            return false;
        var safeBase = SafeName(record.BaseName);
        try
        {
            foreach (var candidate in Directory.EnumerateDirectories(sourceRoot))
            {
                var name = Path.GetFileName(candidate);
                if (!name.Equals(safeBase, StringComparison.OrdinalIgnoreCase) &&
                    !(name.StartsWith(safeBase + "-", StringComparison.OrdinalIgnoreCase) &&
                      int.TryParse(name.AsSpan(safeBase.Length + 1), out var suffix) && suffix > 0))
                    continue;
                if (Matches(record, sourceRoot, candidate))
                {
                    directory = candidate;
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // A broken cache never becomes authoritative.
        }
        return false;
    }

    private static bool Matches(Iec61850FaultRecordSet record, string root, string candidate)
    {
        EnsureChildDirectory(root, candidate);
        var manifestPath = Path.Combine(candidate, ManifestName);
        if (!File.Exists(manifestPath)) return false;
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath));
        if (manifest?.Version != SchemaVersion ||
            !string.Equals(manifest.RecordId, record.RecordId, StringComparison.Ordinal) ||
            manifest.Files.Length != record.Files.Count)
            return false;

        var byPath = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var entry in manifest.Files)
            if (string.IsNullOrWhiteSpace(entry.RemotePath) ||
                !byPath.TryAdd(entry.RemotePath, entry)) return false;
        foreach (var remote in record.Files)
        {
            if (!byPath.TryGetValue(remote.RemotePath, out var local) ||
                !string.Equals(local.FileName, SafeName(remote.Name), StringComparison.Ordinal) ||
                local.DeclaredSize != remote.SizeBytes ||
                !string.Equals(local.RemoteModifiedUtc,
                    remote.LastModifiedUtc?.ToUniversalTime().ToString("O"), StringComparison.Ordinal))
                return false;
            var file = LocalFilePath(candidate, remote.Name);
            if (!File.Exists(file) || new FileInfo(file).Length != local.Bytes ||
                !string.Equals(HashFile(file), local.Sha256, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static void EnsureChildDirectory(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var fullChild = Path.GetFullPath(path);
        if (!fullChild.StartsWith(fullRoot, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            !string.Equals(Path.GetDirectoryName(fullChild),
                fullRoot.TrimEnd(Path.DirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("COMTRADE cache path escapes the bound device directory.");
    }

    private static string LocalFilePath(string root, string name)
        => Path.Combine(root, SafeName(name));

    private static string SafeName(string input)
    {
        var source = string.IsNullOrWhiteSpace(input) ? "fault-record" : input.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var filename = new string(source.Select(c =>
            c < ' ' || invalid.Contains(c) || c is '/' or '\\' or ':' ? '_' : c).ToArray())
            .Trim().TrimEnd('.');
        return filename.Length == 0 ? "fault-record" : filename;
    }

    private static string HashFile(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    private sealed class Manifest
    {
        public int Version { get; init; }
        public string RecordId { get; init; } = "";
        public Entry[] Files { get; init; } = [];
    }

    private sealed class Entry
    {
        public string RemotePath { get; init; } = "";
        public string FileName { get; init; } = "";
        public uint? DeclaredSize { get; init; }
        public string? RemoteModifiedUtc { get; init; }
        public long Bytes { get; init; }
        public string Sha256 { get; init; } = "";
    }
}
