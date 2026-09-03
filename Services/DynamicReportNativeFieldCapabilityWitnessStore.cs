using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

internal sealed class DynamicReportNativeFieldCapabilityWitnessLoadResult
{
    public bool Exists { get; init; }
    public bool IsValid { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public ArMms.MmsDynamicReportNativeFieldCapabilityEvidence? Evidence { get; init; }
}

/// <summary>
/// Durable P1.7 per-IED physical capability witness store.
///
/// This sidecar is intentionally separate from the qualification profile. A native
/// DataChange InformationReportProven profile by itself cannot unlock general Dynamic RCB
/// planning: normal runtime also requires this exact identity/profile-bound dchg + cleanup
/// witness and revalidates it through the ARIEC P1.7 policy.
/// </summary>
internal sealed class DynamicReportNativeFieldCapabilityWitnessStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false
    };

    private readonly string _rootDirectory;

    public DynamicReportNativeFieldCapabilityWitnessStore(string? rootDirectory = null)
    {
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ARSAS",
                "dynamic-report-field-capability")
            : Path.GetFullPath(rootDirectory);
    }

    public string GetWitnessPath(ArMms.MmsDynamicReportIedIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (string.IsNullOrWhiteSpace(identity.StableIdentityKey))
            throw new ArgumentException("StableIdentityKey is required to locate a native field-capability witness.", nameof(identity));

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity.StableIdentityKey.Trim().ToUpperInvariant()));
        var fileName = Convert.ToHexString(digest).ToLowerInvariant() + ".json";
        return Path.Combine(_rootDirectory, fileName);
    }

    public async Task SaveAsync(
        ArMms.MmsDynamicReportNativeFieldCapabilityEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (!evidence.IsSuccess)
            throw new InvalidOperationException("Cannot persist an incomplete native dynamic field-capability witness.");
        if (string.IsNullOrWhiteSpace(evidence.StableIdentityKey) ||
            string.IsNullOrWhiteSpace(evidence.ModelFingerprint))
        {
            throw new InvalidOperationException("Cannot persist native field-capability evidence without complete identity/fingerprint binding.");
        }

        var identity = new ArMms.MmsDynamicReportIedIdentity
        {
            StableIdentityKey = evidence.StableIdentityKey,
            ModelFingerprint = evidence.ModelFingerprint,
            ProfileRevision = evidence.ProfileRevision
        };
        var path = GetWitnessPath(identity);
        Directory.CreateDirectory(_rootDirectory);
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
                await JsonSerializer.SerializeAsync(stream, evidence, JsonOptions, cancellationToken).ConfigureAwait(false);
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
                // Best-effort temp cleanup only. A witness is trusted only after the atomic move.
            }
        }
    }

    public async Task<DynamicReportNativeFieldCapabilityWitnessLoadResult> LoadAsync(
        ArMms.MmsDynamicReportIedIdentity currentIdentity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentIdentity);
        var path = GetWitnessPath(currentIdentity);
        if (!File.Exists(path))
        {
            return new DynamicReportNativeFieldCapabilityWitnessLoadResult
            {
                Exists = false,
                IsValid = false,
                FilePath = path,
                Reason = "No persisted native Dynamic RCB field-capability witness exists for this stable IED identity."
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
            var evidence = await JsonSerializer.DeserializeAsync<ArMms.MmsDynamicReportNativeFieldCapabilityEvidence>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (evidence is null)
                return Invalid(path, "Persisted native field-capability witness decoded as null.");
            if (!evidence.IsSuccess)
                return Invalid(path, "Persisted native field-capability witness is incomplete and will not be trusted.", evidence);
            if (!Same(evidence.StableIdentityKey, currentIdentity.StableIdentityKey))
                return Invalid(path, "Persisted native field-capability stable identity does not match the current IED.", evidence);
            if (!Same(evidence.ModelFingerprint, currentIdentity.ModelFingerprint))
                return Invalid(path, "Persisted native field-capability model fingerprint does not match the current IED model.", evidence);
            if (!Same(evidence.ProfileRevision, currentIdentity.ProfileRevision))
                return Invalid(path, "Persisted native field-capability profile revision does not match the current IED profile revision.", evidence);

            return new DynamicReportNativeFieldCapabilityWitnessLoadResult
            {
                Exists = true,
                IsValid = true,
                FilePath = path,
                Evidence = evidence,
                Reason = "Identity-compatible native Dynamic RCB field-capability witness loaded."
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Invalid(
                path,
                $"Persisted native field-capability witness is unreadable and will not be trusted: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static DynamicReportNativeFieldCapabilityWitnessLoadResult Invalid(
        string path,
        string reason,
        ArMms.MmsDynamicReportNativeFieldCapabilityEvidence? evidence = null)
        => new()
        {
            Exists = true,
            IsValid = false,
            FilePath = path,
            Reason = reason,
            Evidence = evidence
        };

    private static bool Same(string? left, string? right)
        => string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
}
