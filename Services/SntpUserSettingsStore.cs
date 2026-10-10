using System.Net;
using System.Text.Json;

namespace ArIED61850Tester.Services;

public sealed record SntpSavedSettings(string InterfaceId, string Ipv4Address, bool Enabled);

/// <summary>Local and independent of station/IED project preferences.</summary>
public static class SntpUserSettingsStore
{
    public static string DefaultPath => Path.Combine(UserPreferenceStore.DefaultFolder, "sntp-server.json");

    public static SntpSavedSettings? Load(string? path = null)
    {
        try
        {
            var value = JsonSerializer.Deserialize<SntpSavedSettings>(File.ReadAllText(path ?? DefaultPath));
            return value is not null && IsValid(value) ? value : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    public static bool Save(SntpSavedSettings value, string? path = null)
    {
        if (!IsValid(value)) return false;
        var target = path ?? DefaultPath;
        var tmp = target + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(tmp, JsonSerializer.Serialize(value));
            File.Move(tmp, target, overwrite: true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { } }
    }

    public static SntpNetworkBinding? Match(IReadOnlyList<SntpNetworkBinding> available, SntpSavedSettings? saved)
        => saved is null ? null : available.FirstOrDefault(binding =>
            binding.InterfaceId.Equals(saved.InterfaceId, StringComparison.OrdinalIgnoreCase) &&
            binding.LocalAddress.ToString().Equals(saved.Ipv4Address, StringComparison.OrdinalIgnoreCase));

    private static bool IsValid(SntpSavedSettings value)
        => !string.IsNullOrWhiteSpace(value.InterfaceId) &&
           IPAddress.TryParse(value.Ipv4Address, out var ip) &&
           ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
           !IPAddress.IsLoopback(ip);
}
