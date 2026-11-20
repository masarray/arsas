namespace ArIED61850Tester.Models;

/// <summary>One file proven by the existing remote COMTRADE/FileDirectory service.</summary>
public sealed record IedBrowserRemoteFile(
    string Name, string RemotePath, long? SizeBytes, DateTimeOffset? ModifiedUtc,
    bool IsDirectory = false, bool IsBundle = false, int BundleMemberCount = 0)
{
    public string SizeText => IsDirectory ? "Folder"
        : SizeBytes is not { } size ? "Size unknown"
        : size >= 1_048_576 ? $"{size / 1_048_576d:0.##} MB"
        : size >= 1_024 ? $"{size / 1_024d:0.##} KB"
        : $"{size} B";
    public string ModifiedText => ModifiedUtc?.ToLocalTime().ToString("g") ?? "";
}
