using System.IO;
using System.Text.Json;

namespace ArIED61850Tester.Services;

/// <summary>
/// Persists successful IEC 61850 endpoints and per-IED signal selections outside
/// project files. Profiles can be restored by real IEDName or by IP:port.
/// </summary>
public static class UserPreferenceStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ArIED61850Tester");

    public static string PreferencesPath => Path.Combine(DefaultFolder, "user-preferences.json");
    public static string LegacyPreferencesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ArServer",
        "user-preferences.json");


    public static IReadOnlyList<string> LoadRecentEndpoints(int maximum = 20)
    {
        maximum = Math.Clamp(maximum, 1, 100);
        var prefs = LoadPreferences();
        return prefs.SuccessfulRelays
            .Where(item => !string.IsNullOrWhiteSpace(item.IpAddress))
            .OrderByDescending(item => item.LastConnectedUtc)
            .Select(item => item.IpAddress.Trim())
            .Concat(prefs.SignalSelectionProfiles
                .Where(item => !string.IsNullOrWhiteSpace(item.IpAddress))
                .OrderByDescending(item => item.LastSavedUtc)
                .Select(item => item.IpAddress.Trim()))
            .Concat(prefs.RecentRelayIps
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maximum)
            .ToList();
    }

    public static void RecordSuccessfulEndpoint(string ipAddress, int port, string iedName)
    {
        if (string.IsNullOrWhiteSpace(ipAddress)) return;

        Directory.CreateDirectory(DefaultFolder);
        var prefs = LoadPreferences();
        var endpointKey = NormalizeEndpoint(ipAddress, port);
        var item = prefs.SuccessfulRelays.FirstOrDefault(candidate =>
            NormalizeEndpoint(candidate.IpAddress, candidate.MmsPort)
                .Equals(endpointKey, StringComparison.OrdinalIgnoreCase));
        if (item == null)
        {
            item = new SuccessfulRelayEndpoint();
            prefs.SuccessfulRelays.Add(item);
        }

        item.IpAddress = ipAddress.Trim();
        item.MmsPort = port <= 0 ? 102 : port;
        item.IedName = iedName?.Trim() ?? string.Empty;
        item.LastConnectedUtc = DateTime.UtcNow;
        item.SuccessCount = Math.Max(1, item.SuccessCount + 1);
        prefs.RecentRelayIps.Clear();
        prefs.SuccessfulRelays = prefs.SuccessfulRelays
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.IpAddress))
            .GroupBy(candidate => NormalizeEndpoint(candidate.IpAddress, candidate.MmsPort), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(candidate => candidate.LastConnectedUtc).First())
            .OrderByDescending(candidate => candidate.LastConnectedUtc)
            .Take(50)
            .ToList();

        File.WriteAllText(PreferencesPath, JsonSerializer.Serialize(prefs, Options));
    }

    public static IReadOnlyCollection<string> LoadSignalSelectionProfile(string iedName, string ipAddress, int port)
    {
        var prefs = LoadPreferences();
        var nameKey = NormalizeName(iedName);
        var endpointKey = NormalizeEndpoint(ipAddress, port);

        // Restore one best profile instead of unioning multiple historical devices.
        // Real IEDName is the strongest identity; IP:port is the fallback when the name
        // cannot be resolved or the device was moved before a new profile was saved.
        var profile = prefs.SignalSelectionProfiles
            .Select(item => new
            {
                Profile = item,
                NameMatch = !string.IsNullOrWhiteSpace(nameKey) &&
                            NormalizeName(item.IedName).Equals(nameKey, StringComparison.OrdinalIgnoreCase),
                EndpointMatch = !string.IsNullOrWhiteSpace(endpointKey) &&
                                NormalizeEndpoint(item.IpAddress, item.Port).Equals(endpointKey, StringComparison.OrdinalIgnoreCase)
            })
            .Where(item => item.NameMatch || item.EndpointMatch)
            .OrderByDescending(item => item.NameMatch && item.EndpointMatch)
            .ThenByDescending(item => item.NameMatch)
            .ThenByDescending(item => item.Profile.LastSavedUtc)
            .Select(item => item.Profile)
            .FirstOrDefault();

        if (profile == null)
            return Array.Empty<string>();

        return profile.SelectedObjectReferences
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(NormalizeReference)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void SaveSignalSelectionProfile(
        string iedName,
        string ipAddress,
        int port,
        IEnumerable<string> selectedObjectReferences)
    {
        var nameKey = NormalizeName(iedName);
        var endpointKey = NormalizeEndpoint(ipAddress, port);
        if (string.IsNullOrWhiteSpace(nameKey) && string.IsNullOrWhiteSpace(endpointKey)) return;

        Directory.CreateDirectory(DefaultFolder);
        var prefs = LoadPreferences();
        var profile = prefs.SignalSelectionProfiles
            .Select(item => new
            {
                Profile = item,
                NameMatch = !string.IsNullOrWhiteSpace(nameKey) &&
                            NormalizeName(item.IedName).Equals(nameKey, StringComparison.OrdinalIgnoreCase),
                EndpointMatch = !string.IsNullOrWhiteSpace(endpointKey) &&
                                NormalizeEndpoint(item.IpAddress, item.Port).Equals(endpointKey, StringComparison.OrdinalIgnoreCase)
            })
            .Where(item => item.NameMatch ||
                           (item.EndpointMatch &&
                            (string.IsNullOrWhiteSpace(nameKey) || string.IsNullOrWhiteSpace(item.Profile.IedName))))
            .OrderByDescending(item => item.NameMatch && item.EndpointMatch)
            .ThenByDescending(item => item.NameMatch)
            .ThenByDescending(item => item.Profile.LastSavedUtc)
            .Select(item => item.Profile)
            .FirstOrDefault();

        if (profile == null)
        {
            profile = new SignalSelectionProfile();
            prefs.SignalSelectionProfiles.Add(profile);
        }

        profile.IedName = iedName?.Trim() ?? string.Empty;
        profile.IpAddress = ipAddress?.Trim() ?? string.Empty;
        profile.Port = port <= 0 ? 102 : port;
        profile.SelectedObjectReferences = selectedObjectReferences
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(NormalizeReference)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase)
            .Take(50000)
            .ToList();
        profile.LastSavedUtc = DateTime.UtcNow;

        prefs.SignalSelectionProfiles = prefs.SignalSelectionProfiles
            .Where(item => !string.IsNullOrWhiteSpace(item.IedName) || !string.IsNullOrWhiteSpace(item.IpAddress))
            .GroupBy(item => BuildProfileKey(item.IedName, item.IpAddress, item.Port), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.LastSavedUtc).First())
            .OrderByDescending(item => item.LastSavedUtc)
            .Take(100)
            .ToList();

        var json = JsonSerializer.Serialize(prefs, Options);
        File.WriteAllText(PreferencesPath, json);
    }

    private static UserPreferences LoadPreferences()
    {
        var current = ReadPreferencesFile(PreferencesPath) ?? new UserPreferences();

        // One-way compatibility with the original ArServer preference file. Its signal
        // profile schema already used IEDName + object references, so those choices can
        // be restored immediately after upgrading to ArIED without carrying MQTT/Modbus.
        var legacy = ReadPreferencesFile(LegacyPreferencesPath);
        if (legacy != null)
        {
            current.RecentRelayIps.AddRange(legacy.RecentRelayIps);
            current.SuccessfulRelays.AddRange(legacy.SuccessfulRelays);
            current.SignalSelectionProfiles.AddRange(legacy.SignalSelectionProfiles);
            current.RecentRelayIps = current.RecentRelayIps
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(50)
                .ToList();
            current.SuccessfulRelays = current.SuccessfulRelays
                .Where(item => !string.IsNullOrWhiteSpace(item.IpAddress))
                .GroupBy(item => NormalizeEndpoint(item.IpAddress, item.MmsPort), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.LastConnectedUtc).First())
                .OrderByDescending(item => item.LastConnectedUtc)
                .Take(50)
                .ToList();
            current.SignalSelectionProfiles = current.SignalSelectionProfiles
                .Where(item => !string.IsNullOrWhiteSpace(item.IedName) || !string.IsNullOrWhiteSpace(item.IpAddress))
                .GroupBy(item => BuildProfileKey(item.IedName, item.IpAddress, item.Port), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.LastSavedUtc).First())
                .ToList();
        }

        return current;
    }

    private static UserPreferences? ReadPreferencesFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UserPreferences>(json, Options);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildProfileKey(string iedName, string ipAddress, int port)
    {
        var name = NormalizeName(iedName);
        return !string.IsNullOrWhiteSpace(name) ? "name:" + name : "endpoint:" + NormalizeEndpoint(ipAddress, port);
    }

    private static string NormalizeName(string? value) => (value ?? string.Empty).Trim();
    private static string NormalizeEndpoint(string? ipAddress, int port)
        => string.IsNullOrWhiteSpace(ipAddress) ? string.Empty : $"{ipAddress.Trim().ToLowerInvariant()}:{(port <= 0 ? 102 : port)}";
    private static string NormalizeReference(string? value)
        => (value ?? string.Empty).Trim().Replace('$', '.').Replace("..", ".").ToLowerInvariant();

    private sealed class UserPreferences
    {
        // Keep the original ArServer fields so successful endpoints migrate without
        // requiring the user to type the IP address again after upgrading.
        public List<string> RecentRelayIps { get; set; } = new();
        public List<SuccessfulRelayEndpoint> SuccessfulRelays { get; set; } = new();
        public List<SignalSelectionProfile> SignalSelectionProfiles { get; set; } = new();
    }

    private sealed class SignalSelectionProfile
    {
        public string IedName { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; } = 102;
        public List<string> SelectedObjectReferences { get; set; } = new();
        public DateTime LastSavedUtc { get; set; } = DateTime.UtcNow;
    }
    private sealed class SuccessfulRelayEndpoint
    {
        public string IpAddress { get; set; } = string.Empty;
        public int MmsPort { get; set; } = 102;
        public string IedName { get; set; } = string.Empty;
        public DateTime LastConnectedUtc { get; set; } = DateTime.UtcNow;
        public int SuccessCount { get; set; }
    }

}
