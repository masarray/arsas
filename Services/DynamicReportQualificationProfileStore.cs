using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

internal sealed class DynamicReportQualificationProfileLoadResult
{
    public bool Exists { get; init; }
    public bool IsValid { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public ArMms.MmsDynamicReportQualificationProfile? Profile { get; init; }
    public ArMms.MmsDynamicReportProfileCompatibility? Compatibility { get; init; }
}

internal sealed class DynamicReportQualificationProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false
    };

    private readonly string _rootDirectory;

    public DynamicReportQualificationProfileStore(string? rootDirectory = null)
    {
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ARSAS",
                "dynamic-report-qualification")
            : Path.GetFullPath(rootDirectory);
    }

    public string GetProfilePath(ArMms.MmsDynamicReportIedIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (string.IsNullOrWhiteSpace(identity.StableIdentityKey))
            throw new ArgumentException("StableIdentityKey is required to locate a qualification profile.", nameof(identity));

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity.StableIdentityKey.Trim().ToUpperInvariant()));
        var fileName = Convert.ToHexString(digest).ToLowerInvariant() + ".json";
        return Path.Combine(_rootDirectory, fileName);
    }

    public async Task SaveAsync(
        ArMms.MmsDynamicReportQualificationProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.SchemaVersion != ArMms.MmsDynamicReportQualificationProfile.CurrentSchemaVersion)
            throw new InvalidOperationException($"Cannot persist unsupported dynamic qualification profile schema {profile.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(profile.Identity.StableIdentityKey) ||
            string.IsNullOrWhiteSpace(profile.Identity.ModelFingerprint))
        {
            throw new InvalidOperationException("Cannot persist dynamic qualification evidence without a complete identity/fingerprint.");
        }

        Directory.CreateDirectory(_rootDirectory);
        var path = GetProfilePath(profile.Identity);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            await using (var stream = new FileStream(
                temp,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch
            {
                // Best-effort cleanup only. The target profile is never considered written
                // until the atomic move above succeeds.
            }
        }
    }

    public async Task<DynamicReportQualificationProfileLoadResult> LoadAsync(
        ArMms.MmsDynamicReportIedIdentity currentIdentity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentIdentity);
        var path = GetProfilePath(currentIdentity);
        if (!File.Exists(path))
        {
            return new DynamicReportQualificationProfileLoadResult
            {
                Exists = false,
                IsValid = false,
                FilePath = path,
                Reason = "No persisted dynamic reporting qualification profile exists for this stable IED identity."
            };
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                useAsync: true);
            var profile = await JsonSerializer.DeserializeAsync<ArMms.MmsDynamicReportQualificationProfile>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (profile is null)
            {
                return Invalid(path, "Persisted dynamic qualification profile decoded as null.");
            }
            if (profile.SchemaVersion != ArMms.MmsDynamicReportQualificationProfile.CurrentSchemaVersion)
            {
                return Invalid(
                    path,
                    $"Persisted dynamic qualification profile schema {profile.SchemaVersion} is unsupported; requalification is required.");
            }

            var compatibility = ArMms.MmsDynamicReportQualificationProfilePolicy.CheckIdentityCompatibility(
                profile,
                currentIdentity);
            if (!compatibility.IsCompatible)
            {
                return new DynamicReportQualificationProfileLoadResult
                {
                    Exists = true,
                    IsValid = false,
                    FilePath = path,
                    Profile = profile,
                    Compatibility = compatibility,
                    Reason = compatibility.Reason
                };
            }

            return new DynamicReportQualificationProfileLoadResult
            {
                Exists = true,
                IsValid = true,
                FilePath = path,
                Profile = profile,
                Compatibility = compatibility,
                Reason = $"Identity-compatible dynamic qualification profile loaded in state {profile.State}."
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Invalid(
                path,
                $"Persisted dynamic qualification profile is unreadable and will not be trusted: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static DynamicReportQualificationProfileLoadResult Invalid(string path, string reason)
        => new()
        {
            Exists = true,
            IsValid = false,
            FilePath = path,
            Reason = reason
        };
}
