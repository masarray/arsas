using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

internal sealed record DynamicReportPerIedFieldCapabilityBootstrapResult
{
    public bool IsSuccess { get; init; }
    public bool AlreadyQualified { get; init; }
    public string Stage { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string ProfilePath { get; init; } = string.Empty;
    public string WitnessPath { get; init; } = string.Empty;
}

/// <summary>
/// Explicit P1.7 per-IED capability bootstrap.
///
/// This coordinator does not invent a shortcut around qualification. For a new identity it
/// reuses the existing guarded commissioning ladder in order:
/// G2.3 exact dynamic DataSet envelope -> G2.4 transactional RCB/GI activation proof ->
/// G2.5 actual spontaneous NO-GI dchg + cleanup -> native profile/witness persistence.
///
/// The action never calls MarkProductionEligible. Normal monitoring remains polling/static
/// until the complete native witness has been persisted and a later monitor start reloads it.
/// </summary>
internal sealed class DynamicReportPerIedFieldCapabilityBootstrapService
{
    private readonly DynamicReportQualificationProfileStore _profileStore;
    private readonly DynamicReportNativeFieldCapabilityWitnessStore _witnessStore;

    public DynamicReportPerIedFieldCapabilityBootstrapService(
        DynamicReportQualificationProfileStore? profileStore = null,
        DynamicReportNativeFieldCapabilityWitnessStore? witnessStore = null)
    {
        _profileStore = profileStore ?? new DynamicReportQualificationProfileStore();
        _witnessStore = witnessStore ?? new DynamicReportNativeFieldCapabilityWitnessStore();
    }

    public async Task<DynamicReportPerIedFieldCapabilityBootstrapResult> RunAsync(
        Iec61850MonitorDevice device,
        IReadOnlyList<SignalDefinition> fullModelSignals,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fullModelSignals);
        cancellationToken.ThrowIfCancellationRequested();

        ArMms.MmsDynamicReportIedIdentity identity;
        try
        {
            identity = DynamicReportQualificationIdentity.Build(device, fullModelSignals);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Failed("identity", "P1.7 identity preflight failed: " + ex.Message);
        }

        var initial = await _profileStore.LoadAsync(identity, cancellationToken).ConfigureAwait(false);
        if (initial.IsValid && initial.Profile is not null)
        {
            var existingWitness = await _witnessStore.LoadAsync(identity, cancellationToken).ConfigureAwait(false);
            if (existingWitness.IsValid &&
                existingWitness.Evidence is not null &&
                ArMms.MmsGuardedDynamicReportNativeFieldCapabilityPolicy.TryValidate(
                    new ArMms.MmsDynamicReportGuardedRuntimePlanningContext
                    {
                        Profile = initial.Profile,
                        CurrentIdentity = identity
                    },
                    existingWitness.Evidence,
                    out var alreadyReason))
            {
                return new DynamicReportPerIedFieldCapabilityBootstrapResult
                {
                    IsSuccess = true,
                    AlreadyQualified = true,
                    Stage = "already-native-capable",
                    ProfilePath = initial.FilePath,
                    WitnessPath = existingWitness.FilePath,
                    Summary = "P1.7 native per-IED Dynamic RCB capability is already valid for this exact identity. " + alreadyReason
                };
            }

            if (initial.Profile.State == ArMms.MmsDynamicReportQualificationState.ProductionEligible)
            {
                return Failed(
                    "production-eligible",
                    "This identity already has a ProductionEligible profile. P1.7 bootstrap will not rewrite certified production evidence.",
                    initial.FilePath,
                    existingWitness.FilePath);
            }
        }

        var loaded = initial;
        if (!loaded.IsValid || loaded.Profile is null)
        {
            progress?.Report(
                $"G2.7 P1.7 [{identity.StableIdentityKey}] stage 1/3: no valid per-IED profile; running existing G2.3 exact bounded dynamic DataSet qualification…");
            var g23 = await new DynamicReportQualificationCommissioningService(_profileStore)
                .RunAsync(device, fullModelSignals, cancellationToken)
                .ConfigureAwait(false);
            if (!g23.IsSuccess)
            {
                return Failed(
                    "G2.3-envelope",
                    "P1.7 bootstrap stopped at G2.3: " + g23.Summary,
                    g23.ProfilePath);
            }

            loaded = await _profileStore.LoadAsync(identity, cancellationToken).ConfigureAwait(false);
            if (!loaded.IsValid || loaded.Profile is null)
                return Failed("G2.3-reload", "G2.3 reported success but its identity-compatible profile could not be reloaded.", g23.ProfilePath);
        }

        if (loaded.Profile.State == ArMms.MmsDynamicReportQualificationState.EnvelopeQualified)
        {
            progress?.Report(
                $"G2.7 P1.7 [{identity.StableIdentityKey}] stage 2/3: G2.3 envelope ready; running existing transactional G2.4 RCB activation + actual InformationReport proof…");
            var g24 = await new DynamicReportActivationCommissioningServiceV2(_profileStore)
                .RunAsync(device, fullModelSignals, cancellationToken)
                .ConfigureAwait(false);
            if (!g24.IsSuccess)
            {
                return Failed(
                    "G2.4-activation",
                    "P1.7 bootstrap stopped at G2.4: " + g24.Summary,
                    g24.ProfilePath);
            }

            loaded = await _profileStore.LoadAsync(identity, cancellationToken).ConfigureAwait(false);
            if (!loaded.IsValid || loaded.Profile is null)
                return Failed("G2.4-reload", "G2.4 reported success but its InformationReportProven profile could not be reloaded.", g24.ProfilePath);
        }

        if (loaded.Profile.State != ArMms.MmsDynamicReportQualificationState.InformationReportProven)
        {
            return Failed(
                "profile-state",
                $"P1.7 requires InformationReportProven before the native dchg witness, but current state is {loaded.Profile.State}.",
                loaded.FilePath);
        }

        var physicalWitnessMembers = loaded.Profile.RcbActivationProof?.MemberReferences
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .ToArray() ?? Array.Empty<string>();
        if (physicalWitnessMembers.Length == 0)
        {
            return Failed(
                "G2.5-target",
                "InformationReportProven profile has no exact activation member sequence for the physical dchg witness.",
                loaded.FilePath);
        }

        progress?.Report(
            $"G2.7 P1.7 [{identity.StableIdentityKey}] stage 3/3: InformationReportProven ready. Exact physical dchg witness members ({physicalWitnessMembers.Length}) = {string.Join(" | ", physicalWitnessMembers)}. When G2.5 says ARMED, cause exactly ONE already-approved safe process/status change affecting one of THESE members…");

        var native = await new DynamicReportNativeFieldCapabilityPersistenceService(_profileStore, _witnessStore)
            .RunAsync(device, fullModelSignals, progress, cancellationToken)
            .ConfigureAwait(false);
        if (!native.IsSuccess)
        {
            return Failed(
                "G2.5-native-dchg",
                native.Summary,
                native.ProfilePath,
                native.WitnessPath);
        }

        return new DynamicReportPerIedFieldCapabilityBootstrapResult
        {
            IsSuccess = true,
            Stage = "native-field-capability",
            Summary = native.Summary,
            ProfilePath = native.ProfilePath,
            WitnessPath = native.WitnessPath
        };
    }

    private static DynamicReportPerIedFieldCapabilityBootstrapResult Failed(
        string stage,
        string summary,
        string profilePath = "",
        string witnessPath = "")
        => new()
        {
            IsSuccess = false,
            Stage = stage,
            Summary = summary,
            ProfilePath = profilePath,
            WitnessPath = witnessPath
        };
}
