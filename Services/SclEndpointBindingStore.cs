using System.Net;
using System.Text.Json;

namespace ArIED61850Tester.Services;

/// <summary>
/// Only a completed SCL-assisted MMS association can create a remembered binding.
/// Scope is exact immutable source SHA + IEDName + AccessPoint; the legacy IED-wide
/// endpoint history is never used for switching between AccessPoints.
/// </summary>
internal static class SclEndpointBindingStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    internal static string DefaultPath => Path.Combine(
        UserPreferenceStore.DefaultFolder, "scl-successful-endpoints.json");

    internal sealed class Binding
    {
        public string SourceSha256 { get; set; } = string.Empty;
        public string IedName { get; set; } = string.Empty;
        public string AccessPointName { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; } = 102;
        public DateTime LastVerifiedUtc { get; set; }
    }

    public static bool TryGet(
        string sourceSha256, string iedName, string accessPointName,
        out string ipAddress, out int port)
        => TryGetAtPath(DefaultPath, sourceSha256, iedName, accessPointName, out ipAddress, out port);

    internal static bool TryGetAtPath(
        string path, string sourceSha256, string iedName, string accessPointName,
        out string ipAddress, out int port)
    {
        ipAddress = string.Empty;
        port = 102;
        if (!ValidIdentity(sourceSha256, iedName, accessPointName))
            return false;
        lock (Gate)
        {
            var matches = Load(path).Where(binding => Match(binding, sourceSha256, iedName, accessPointName) &&
                ValidAddress(binding.IpAddress, binding.Port)).ToArray();
            if (matches.Length == 0) return false;
            var best = matches.OrderByDescending(binding => binding.LastVerifiedUtc).First();
            ipAddress = best.IpAddress;
            port = best.Port;
            return true;
        }
    }

    public static void RecordVerified(
        string sourceSha256, string iedName, string accessPointName,
        string ipAddress, int port)
        => RecordVerifiedAtPath(DefaultPath, sourceSha256, iedName, accessPointName, ipAddress, port);

    internal static void RecordVerifiedAtPath(
        string path, string sourceSha256, string iedName, string accessPointName,
        string ipAddress, int port)
    {
        if (!ValidIdentity(sourceSha256, iedName, accessPointName) || !ValidAddress(ipAddress, port))
            throw new ArgumentException("An exact SCL source, IED, AP and valid MMS IP:port are required.");

        lock (Gate)
        {
            var list = Load(path);
            list.RemoveAll(binding => Match(binding, sourceSha256, iedName, accessPointName));
            list.Add(new Binding
            {
                SourceSha256 = sourceSha256.Trim(),
                IedName = iedName.Trim(),
                AccessPointName = accessPointName.Trim(),
                IpAddress = ipAddress.Trim(),
                Port = port,
                LastVerifiedUtc = DateTime.UtcNow
            });
            var updated = list
                .Where(binding => ValidIdentity(binding.SourceSha256, binding.IedName, binding.AccessPointName) &&
                    ValidAddress(binding.IpAddress, binding.Port))
                .OrderByDescending(binding => binding.LastVerifiedUtc)
                .Take(100)
                .ToArray();

            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(directory);
            var tmp = Path.Combine(directory, $".scl-endpoints-{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(tmp, JsonSerializer.Serialize(updated, JsonOptions));
                File.Move(tmp, path, overwrite: true); // same-volume atomic replacement
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }
    }

    private static List<Binding> Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<List<Binding>>(File.ReadAllText(path), JsonOptions) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(); // No remembered authority is safer than a corrupt binding.
        }
    }

    private static bool Match(Binding candidate, string sourceSha, string ied, string ap) =>
        string.Equals(candidate.SourceSha256, sourceSha, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(candidate.IedName, ied, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(candidate.AccessPointName, ap, StringComparison.OrdinalIgnoreCase);

    private static bool ValidIdentity(string sourceSha, string ied, string ap) =>
        sourceSha is { Length: 64 } &&
        sourceSha.All(Uri.IsHexDigit) &&
        !string.IsNullOrWhiteSpace(ied) &&
        !string.IsNullOrWhiteSpace(ap);

    private static bool ValidAddress(string ip, int port) =>
        port is >= 1 and <= 65535 && IPAddress.TryParse(ip, out var address) &&
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
        !IPAddress.Any.Equals(address) && !IPAddress.Broadcast.Equals(address);
}
