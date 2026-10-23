using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

internal sealed class DynamicReportOptionalFieldsProbeCommissioningResult
{
    public bool IsSuccess { get; init; }
    public bool IsBlocked { get; init; }
    public bool CleanupSucceeded { get; init; }
    public string Summary { get; init; } = string.Empty;
    public ArMms.MmsDynamicReportIedIdentity? Identity { get; init; }
    public ArMms.MmsDynamicReportQualificationProfile? InputProfile { get; init; }
    public ArMms.MmsDynamicRcbOptionalFieldsProbeResult? Probe { get; init; }
    public string RcbReference { get; init; } = string.Empty;
    public string ProfilePath { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();
}

/// <summary>
/// P1 proof-field isolation gate after physical P0 TrgOps PASS. It proves only
/// reason-for-inclusion + data-set-name OptFlds on exactly one forced-live free
/// URCB, then restores the captured original OptFlds immediately. It never
/// touches TrgOps, DatSet, Resv, RptEna, GI, DataSet services, report routing,
/// or persisted qualification state.
/// </summary>
internal sealed class DynamicReportOptionalFieldsProbeCommissioningService
{
    private static readonly TimeSpan AuxiliaryAssociationTimeout = TimeSpan.FromSeconds(10);
    private const string ProbeOptionalFields = "reason-for-inclusion data-set-name";

    private readonly DynamicReportQualificationProfileStore _profileStore;

    public DynamicReportOptionalFieldsProbeCommissioningService(
        DynamicReportQualificationProfileStore? profileStore = null)
    {
        _profileStore = profileStore ?? new DynamicReportQualificationProfileStore();
    }

    public async Task<DynamicReportOptionalFieldsProbeCommissioningResult> RunAsync(
        Iec61850MonitorDevice device,
        IReadOnlyList<SignalDefinition> fullModelSignals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fullModelSignals);

        var evidence = new List<string>();
        ArMms.MmsDynamicReportIedIdentity identity;
        try
        {
            identity = DynamicReportQualificationIdentity.Build(device, fullModelSignals);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Blocked("P1 identity preflight failed: " + ex.Message, evidence);
        }

        evidence.Add($"P1 identity stableKey={identity.StableIdentityKey}; fingerprint={identity.ModelFingerprint}; profileRevision={TextOrDash(identity.ProfileRevision)}");

        var loaded = await _profileStore.LoadAsync(identity, cancellationToken).ConfigureAwait(false);
        evidence.Add($"P1 persisted profile: exists={loaded.Exists}; valid={loaded.IsValid}; reason={loaded.Reason}");
        if (!loaded.IsValid || loaded.Profile is null)
        {
            return Blocked(
                "P1 requires an identity-compatible persisted G2.3 profile before any URCB field write.",
                evidence,
                identity,
                loaded.FilePath);
        }

        var profile = loaded.Profile;
        if (profile.State < ArMms.MmsDynamicReportQualificationState.EnvelopeQualified || profile.AcceptedEnvelope is null)
        {
            return Blocked(
                $"P1 requires at least EnvelopeQualified evidence; current profile state is {profile.State}.",
                evidence,
                identity,
                loaded.FilePath,
                profile);
        }

        var preferredLogicalDevice = profile.AcceptedEnvelope.ExactProvenMemberReferences
            .Select(reference => DomainOf(reference))
            .FirstOrDefault(domain => !string.IsNullOrWhiteSpace(domain)) ?? string.Empty;
        evidence.Add($"P1 profile gate: state={profile.State}; preferredLD={TextOrDash(preferredLogicalDevice)}; requestedOptFlds={ProbeOptionalFields}; expectedCanonicalRaw=061800");

        await using var auxiliary = new ArMms.MmsClientSession();
        try
        {
            await auxiliary.ConnectAsync(
                device.IpAddress,
                device.Port,
                AuxiliaryAssociationTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException)
        {
            evidence.Add($"P1 auxiliary association failed: {ex.GetType().Name}: {ex.Message}");
            return Blocked(
                "P1 auxiliary MMS association was not established. No URCB field was changed.",
                evidence,
                identity,
                loaded.FilePath,
                profile);
        }

        evidence.Add($"P1 auxiliary association ready: state={auxiliary.State}; handshake={TextOrDash(auxiliary.LastHandshakeMessage)}");

        ArMms.MmsDiscoveryResult discovery;
        try
        {
            discovery = await auxiliary.DiscoverAsync(
                probeReportAttributes: true,
                maxReportAttributeProbes: 64,
                cancellationToken: cancellationToken,
                readDataSetDirectories: false,
                maxDataSetDirectoryReads: 0).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
        {
            evidence.Add($"P1 discovery failed: {ex.GetType().Name}: {ex.Message}");
            return Failed(
                "P1 fresh discovery failed before any URCB field mutation.",
                evidence,
                identity,
                profile,
                loaded.FilePath);
        }

        evidence.Add("P1 discovery: " + discovery.Summary);

        ArMms.MmsRcbAvailabilityResult availability;
        try
        {
            availability = await auxiliary.CheckReportControlAvailabilityAsync(
                discovery.ReportInventory,
                discovery.IedDirectory,
                new ArMms.MmsRcbAvailabilityOptions
                {
                    MaxReportControls = 64,
                    ReadDataSetDirectories = false
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
        {
            evidence.Add($"P1 forced-live URCB availability failed: {ex.GetType().Name}: {ex.Message}");
            return Failed(
                "P1 could not obtain fresh DatSet/RptEna/Resv/Owner evidence. No URCB field was changed.",
                evidence,
                identity,
                profile,
                loaded.FilePath);
        }

        evidence.Add("P1 forced live availability: " + availability.Summary);
        foreach (var warning in availability.Warnings)
            evidence.Add("P1 availability warning: " + warning);

        var selectedRcb = DynamicReportActivationCommissioningServiceV2.SelectQualifiedUrcbFromFreshAvailability(
            availability,
            discovery.ReportInventory,
            preferredLogicalDevice,
            out var selectedSnapshot,
            out var selectionReason,
            out var candidateDiagnostics);
        evidence.Add("P1 URCB selection: " + selectionReason);
        foreach (var diagnostic in candidateDiagnostics)
            evidence.Add("P1 URCB candidate: " + diagnostic);

        if (selectedRcb is null || selectedSnapshot is null)
        {
            return Failed(
                "P1 found no forced-live proven-empty/free URCB. No OptFlds write was attempted.",
                evidence,
                identity,
                profile,
                loaded.FilePath);
        }

        ArMms.MmsDynamicRcbOptionalFieldsProbeResult probe;
        try
        {
            probe = await auxiliary.ProbeDynamicRcbOptionalFieldsAsync(
                selectedRcb,
                ProbeOptionalFields,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
        {
            evidence.Add($"P1 OptFlds probe exception: {ex.GetType().Name}: {ex.Message}");
            return new DynamicReportOptionalFieldsProbeCommissioningResult
            {
                IsSuccess = false,
                CleanupSucceeded = false,
                Summary = "P1 OptFlds probe ended with a protocol/transport exception; inspect the selected URCB from a fresh association before retry.",
                Identity = identity,
                InputProfile = profile,
                RcbReference = selectedRcb.Reference,
                ProfilePath = loaded.FilePath,
                EvidenceLines = evidence
            };
        }

        foreach (var step in probe.WriteSteps)
            evidence.Add($"P1 write: attribute={step.Attribute}; reference={step.Reference}; attempted={step.Attempted}; success={step.IsSuccess}; result={step.Message}");
        foreach (var line in probe.Evidence)
            evidence.Add(line);

        var associationHealthy = auxiliary.IsMmsInitiated;
        evidence.Add($"P1 result: success={probe.IsSuccess}; cleanup={probe.CleanupSucceeded}; associationHealthy={associationHealthy}; original={TextOrDash(probe.OriginalRaw)}; requested={TextOrDash(probe.RequestedRaw)}; readback={TextOrDash(probe.ReadbackRaw)}; restoreReadback={TextOrDash(probe.RestoreReadbackRaw)}");
        evidence.Add("P1 safety: no TrgOps, DatSet, Resv, RptEna, GI, DefineNamedVariableList, report monitor or profile-state mutation is performed by this action.");

        var success = probe.IsSuccess && probe.CleanupSucceeded && associationHealthy;
        return new DynamicReportOptionalFieldsProbeCommissioningResult
        {
            IsSuccess = success,
            CleanupSucceeded = probe.CleanupSucceeded,
            Summary = success
                ? "P1 PASS: one proven-free URCB accepted reason-for-inclusion + data-set-name OptFlds significant bits and original OptFlds restore was proven. G2.4 may proceed to the one-URCB transactional InformationReport proof; production dynamic reporting remains OFF."
                : probe.CleanupSucceeded
                    ? "P1 requested OptFlds was not proven, but original OptFlds restore passed. Production dynamic reporting remains OFF."
                    : "P1 did not prove original OptFlds restore. Do not retry active commissioning until a fresh read-only inspection confirms the URCB state.",
            Identity = identity,
            InputProfile = profile,
            Probe = probe,
            RcbReference = selectedRcb.Reference,
            ProfilePath = loaded.FilePath,
            EvidenceLines = evidence
        };
    }

    private static DynamicReportOptionalFieldsProbeCommissioningResult Blocked(
        string summary,
        IReadOnlyList<string> evidence,
        ArMms.MmsDynamicReportIedIdentity? identity = null,
        string profilePath = "",
        ArMms.MmsDynamicReportQualificationProfile? profile = null)
        => new()
        {
            IsBlocked = true,
            CleanupSucceeded = true,
            Summary = summary,
            Identity = identity,
            InputProfile = profile,
            ProfilePath = profilePath,
            EvidenceLines = evidence.ToArray()
        };

    private static DynamicReportOptionalFieldsProbeCommissioningResult Failed(
        string summary,
        IReadOnlyList<string> evidence,
        ArMms.MmsDynamicReportIedIdentity identity,
        ArMms.MmsDynamicReportQualificationProfile profile,
        string profilePath)
        => new()
        {
            IsSuccess = false,
            CleanupSucceeded = true,
            Summary = summary,
            Identity = identity,
            InputProfile = profile,
            ProfilePath = profilePath,
            EvidenceLines = evidence.ToArray()
        };

    private static string DomainOf(string? reference)
    {
        var text = (reference ?? string.Empty).Trim();
        var slash = text.IndexOf('/');
        return slash > 0 ? text[..slash] : string.Empty;
    }

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
