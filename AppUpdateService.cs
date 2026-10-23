using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace ArIED61850Tester;

internal sealed class AppUpdateService : IDisposable
{
    internal const string ManifestUrl = "https://masarray.github.io/arsas/latest.json";
    internal const string InstallerFileName = "ARSAS-Windows-x64-Setup.exe";
    private const string ExpectedInstallerUrl = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Setup.exe";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(12);
    private static readonly TimeSpan DeferInterval = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HttpClient _httpClient;
    private readonly string _stateDirectory;
    private readonly string _statePath;

    public AppUpdateService()
    {
        CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
        _stateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ARSAS",
            "Updater");
        _statePath = Path.Combine(_stateDirectory, "state.json");

        _httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ARSAS-Updater", CurrentVersion.ToString(3)));
        _httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true
        };
    }

    public Version CurrentVersion { get; }

    public async Task<UpdateManifest?> CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        var state = LoadState();
        var now = DateTimeOffset.UtcNow;
        if (state.LastSuccessfulCheckUtc is { } lastCheck && now - lastCheck < CheckInterval)
            return null;

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{ManifestUrl}?current={Uri.EscapeDataString(CurrentVersion.ToString(3))}&t={now.ToUnixTimeSeconds()}");
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);

        ValidateManifest(manifest);
        state.LastSuccessfulCheckUtc = now;
        SaveState(state);

        var latestVersion = ParseVersion(manifest!.Version);
        if (latestVersion <= CurrentVersion)
            return null;

        if (state.DeferredVersion.Equals(manifest.Version, StringComparison.OrdinalIgnoreCase) &&
            state.DeferredUntilUtc is { } deferredUntil && deferredUntil > now)
        {
            return null;
        }

        return manifest;
    }

    public void Defer(UpdateManifest manifest)
    {
        var state = LoadState();
        state.DeferredVersion = manifest.Version;
        state.DeferredUntilUtc = DateTimeOffset.UtcNow.Add(DeferInterval);
        SaveState(state);
    }

    public async Task<string> DownloadInstallerAsync(
        UpdateManifest manifest,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateManifest(manifest);
        var version = ParseVersion(manifest.Version).ToString(3);
        var directory = Path.Combine(_stateDirectory, "Packages", version);
        Directory.CreateDirectory(directory);

        var destination = Path.Combine(directory, InstallerFileName);
        if (File.Exists(destination) && await VerifyInstallerAsync(destination, manifest.Installer, cancellationToken).ConfigureAwait(false))
        {
            progress?.Report(new UpdateDownloadProgress(manifest.Installer.SizeBytes, manifest.Installer.SizeBytes));
            return destination;
        }

        var partial = destination + ".part";
        TryDelete(partial);

        try
        {
            using var response = await _httpClient.GetAsync(
                manifest.Installer.Url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var target = new FileStream(
                partial,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var buffer = new byte[128 * 1024];
            long total = 0;
            var lastReport = Stopwatch.StartNew();
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                total += read;
                if (total > manifest.Installer.SizeBytes)
                    throw new InvalidDataException("Downloaded installer is larger than the verified release manifest.");

                if (lastReport.ElapsedMilliseconds >= 150)
                {
                    progress?.Report(new UpdateDownloadProgress(total, manifest.Installer.SizeBytes));
                    lastReport.Restart();
                }
            }

            await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            progress?.Report(new UpdateDownloadProgress(total, manifest.Installer.SizeBytes));

            if (!await VerifyInstallerAsync(partial, manifest.Installer, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("Installer verification failed. The file was not opened.");

            File.Move(partial, destination, overwrite: true);
            return destination;
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    public void LaunchInstaller(string installerPath)
    {
        if (!File.Exists(installerPath))
            throw new FileNotFoundException("The verified installer could not be found.", installerPath);

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /NORESTART",
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? _stateDirectory,
            UseShellExecute = true
        });

        if (process is null)
            throw new InvalidOperationException("Windows did not start the ARSAS installer.");
    }

    public void OpenInstallerFolder(string? installerPath)
    {
        var directory = installerPath is not null
            ? Path.GetDirectoryName(installerPath)
            : Path.Combine(_stateDirectory, "Packages");
        if (string.IsNullOrWhiteSpace(directory))
            return;

        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = installerPath is not null && File.Exists(installerPath)
                ? $"/select,\"{installerPath}\""
                : $"\"{directory}\"",
            UseShellExecute = true
        });
    }

    private static void ValidateManifest(UpdateManifest? manifest)
    {
        if (manifest is null)
            throw new InvalidDataException("The ARSAS update manifest is empty.");
        if (!manifest.Product.Equals("ARSAS", StringComparison.Ordinal))
            throw new InvalidDataException("The update manifest is for a different product.");
        if (!manifest.Channel.Equals("stable", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only the stable ARSAS update channel is accepted.");

        _ = ParseVersion(manifest.Version);
        if (!manifest.Installer.Name.Equals(InstallerFileName, StringComparison.Ordinal))
            throw new InvalidDataException("The update manifest does not name the official ARSAS installer.");
        if (!Uri.TryCreate(manifest.Installer.Url, UriKind.Absolute, out var installerUri) ||
            installerUri.Scheme != Uri.UriSchemeHttps ||
            !manifest.Installer.Url.Equals(ExpectedInstallerUrl, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The update manifest points to an untrusted installer location.");
        }
        if (manifest.Installer.SizeBytes is < 1_000_000 or > 500_000_000)
            throw new InvalidDataException("The installer size in the update manifest is invalid.");
        if (manifest.Installer.Sha256.Length != 64 ||
            manifest.Installer.Sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("The update manifest has an invalid SHA-256 value.");
        }
    }

    private static Version ParseVersion(string value)
    {
        var normalized = value.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(normalized, out var version))
            throw new InvalidDataException($"Invalid ARSAS update version '{value}'.");
        return version;
    }

    private static async Task<bool> VerifyInstallerAsync(
        string path,
        UpdateInstallerManifest installer,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != installer.SizeBytes)
            return false;

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actual = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var expected = Convert.FromHexString(installer.Sha256);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private UpdateState LoadState()
    {
        try
        {
            if (!File.Exists(_statePath))
                return new UpdateState();
            return JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(_statePath), JsonOptions) ?? new UpdateState();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to read ARSAS updater state: {exception}");
            return new UpdateState();
        }
    }

    private void SaveState(UpdateState state)
    {
        try
        {
            Directory.CreateDirectory(_stateDirectory);
            var temporary = _statePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temporary, _statePath, overwrite: true);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to save ARSAS updater state: {exception}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // A stale partial file is harmless and will be overwritten on the next attempt.
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class UpdateState
    {
        public DateTimeOffset? LastSuccessfulCheckUtc { get; set; }
        public string DeferredVersion { get; set; } = string.Empty;
        public DateTimeOffset? DeferredUntilUtc { get; set; }
    }
}

internal sealed class UpdateManifest
{
    public int SchemaVersion { get; set; }
    public string Product { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public UpdateInstallerManifest Installer { get; set; } = new();
}

internal sealed class UpdateInstallerManifest
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

internal readonly record struct UpdateDownloadProgress(long BytesReceived, long TotalBytes)
{
    public double Percentage => TotalBytes <= 0 ? 0 : Math.Clamp(BytesReceived * 100d / TotalBytes, 0, 100);
}
