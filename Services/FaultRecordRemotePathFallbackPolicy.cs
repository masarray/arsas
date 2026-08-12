using AR.Iec61850.FaultRecords;

namespace ArIED61850Tester.Services;

public sealed record FaultRecordPathCandidate(string Label, Iec61850FaultRecordSet Record);

/// <summary>
/// Produces bounded, evidence-driven Siemens file-store path variants. MMS FileName is
/// case-sensitive on deployed SIPROTEC relays even when FileDirectory browsing returns
/// a normalized/lowercase companion extension. No variant is attempted after bytes move.
/// </summary>
public static class FaultRecordRemotePathFallbackPolicy
{
    private const string FileNotFoundSignature = "8B 01 07";

    public static bool ShouldTryCompatibilityPaths(Iec61850FaultRecordDownloadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return !result.IsSuccess &&
               result.BytesTransferred == 0 &&
               result.Message.Contains("FileOpen", StringComparison.OrdinalIgnoreCase) &&
               result.Message.Contains(FileNotFoundSignature, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<FaultRecordPathCandidate> BuildCandidates(Iec61850FaultRecordSet record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var candidates = new List<FaultRecordPathCandidate>();
        Add("uppercase companion extensions", uppercaseExtension: true, bareFileName: false);
        Add("bare FileName from discovered entry", uppercaseExtension: false, bareFileName: true);
        Add("bare FileName with uppercase companion extensions", uppercaseExtension: true, bareFileName: true);
        return candidates;

        void Add(string label, bool uppercaseExtension, bool bareFileName)
        {
            var files = record.Files.Select(file => CloneFile(file, uppercaseExtension, bareFileName)).ToArray();
            if (files.Select(file => file.RemotePath).SequenceEqual(record.Files.Select(file => file.RemotePath), StringComparer.Ordinal))
                return;
            if (candidates.Any(candidate => candidate.Record.Files.Select(file => file.RemotePath).SequenceEqual(files.Select(file => file.RemotePath), StringComparer.Ordinal)))
                return;

            candidates.Add(new FaultRecordPathCandidate(label, new Iec61850FaultRecordSet
            {
                RecordId = record.RecordId,
                RemoteDirectory = bareFileName ? string.Empty : record.RemoteDirectory,
                BaseName = record.BaseName,
                Files = files,
                IsComplete = record.IsComplete,
                Completeness = record.Completeness,
                KnownSizeBytes = record.KnownSizeBytes,
                HasUnknownSize = record.HasUnknownSize,
                LastModifiedUtc = record.LastModifiedUtc
            }));
        }
    }

    private static Iec61850FaultRecordFile CloneFile(
        Iec61850FaultRecordFile file,
        bool uppercaseExtension,
        bool bareFileName)
    {
        var sourceName = string.IsNullOrWhiteSpace(file.Name)
            ? GetFileName(file.RemotePath)
            : file.Name;
        var extension = Path.GetExtension(sourceName);
        var name = uppercaseExtension && extension.Length > 0
            ? sourceName[..^extension.Length] + extension.ToUpperInvariant()
            : sourceName;
        var directory = bareFileName ? string.Empty : GetDirectoryName(file.RemotePath);
        var remotePath = string.IsNullOrWhiteSpace(directory) ? name : $"{directory}/{name}";

        return new Iec61850FaultRecordFile
        {
            Name = name,
            RemotePath = remotePath,
            RemoteDirectory = directory,
            BaseName = Path.GetFileNameWithoutExtension(name),
            Extension = Path.GetExtension(name),
            Kind = file.Kind,
            SizeBytes = file.SizeBytes,
            LastModifiedRaw = file.LastModifiedRaw.ToArray(),
            LastModifiedUtc = file.LastModifiedUtc
        };
    }

    private static string GetFileName(string path)
    {
        var normalized = (path ?? string.Empty).Replace('\\', '/');
        var index = normalized.LastIndexOf('/');
        return index < 0 ? normalized : normalized[(index + 1)..];
    }

    private static string GetDirectoryName(string path)
    {
        var normalized = (path ?? string.Empty).Replace('\\', '/');
        var index = normalized.LastIndexOf('/');
        return index <= 0 ? string.Empty : normalized[..index];
    }
}
