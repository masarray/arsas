using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

internal sealed record DynamicReportNativeFieldCapabilityPersistenceResult
{
    public DynamicReportSpontaneousDataChangeCommissioningResult? DataChangeResult { get; init; }
    public ArMms.MmsDynamicReportQualificationProfile? UpdatedProfile { get; init; }
    public ArMms.MmsDynamicReportNativeFieldCapabilityEvidence? CapabilityEvidence { get; init; }
    public bool ProfilePersisted { get; init; }
    public bool CapabilityWitnessPersisted { get; init; }
    public string ProfilePath { get; init; } = string.Empty;
    public string WitnessPath { get; init; } = string.Empty;
    public string Failure { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;

    public bool IsSuccess =>
        DataChangeResult?.IsSuccess == true &&
        UpdatedProfile?.State == ArMms.MmsDynamicReportQualificationState.InformationReportProven &&
        UpdatedProfile.InformationReportProof?.Kind == ArMms.MmsDynamicInformationReportKind.DataChange &&
        CapabilityEvidence?.IsSuccess == true &&
        ProfilePersisted &&
        CapabilityWitnessPersisted &&
        string.IsNullOrWhiteSpace(Failure);
}

/// <summary>
/// P1.7 persistence bridge around the already field-hardened G2.5 spontaneous dchg
/// transaction. G2.5 remains the sole MMS mutation/report/cleanup implementation.
/// Only after its complete PASS do we replace the retained GI-classified report proof with
/// a native DataChange proof and atomically persist a separate cleanup-bound capability
/// witness for this exact IED identity.
///
/// ProductionEligible is intentionally untouched. A profile save without the matching
/// sidecar remains fail-closed because normal runtime requires both through ARIEC policy.
/// </summary>
internal sealed class DynamicReportNativeFieldCapabilityPersistenceService
{
    private readonly DynamicReportQualificationProfileStore _profileStore;
    private readonly DynamicReportNativeFieldCapabilityWitnessStore _witnessStore;

    public DynamicReportNativeFieldCapabilityPersistenceService(
        DynamicReportQualificationProfileStore? profileStore = null,
        DynamicReportNativeFieldCapabilityWitnessStore? witnessStore = null)
    {
        _profileStore = profileStore ?? new DynamicReportQualificationProfileStore();
        _witnessStore = witnessStore ?? new DynamicReportNativeFieldCapabilityWitnessStore();
    }

    public async Task<DynamicReportNativeFieldCapabilityPersistenceResult> RunAsync(
        Iec61850MonitorDevice device,
        IReadOnlyList<SignalDefinition> fullModelSignals,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fullModelSignals);

        progress?.Report(
            "G2.7 P1.7: starting native per-IED dchg capability proof. Cause exactly one already-approved safe status/process change only after the G2.5 READY marker appears…");

        var dataChange = await new DynamicReportSpontaneousDataChangeCommissioningService(_profileStore)
            .RunAsync(device, fullModelSignals, progress, cancellationToken)
            .ConfigureAwait(false);

        if (!dataChange.IsSuccess || dataChange.Identity is null || dataChange.InputProfile is null)
        {
            return new DynamicReportNativeFieldCapabilityPersistenceResult
            {
                DataChangeResult = dataChange,
                ProfilePath = dataChange.ProfilePath,
                Failure = dataChange.Summary,
                Summary = "G2.7 P1.7 native field-capability bootstrap stopped fail-closed because the complete G2.5 spontaneous dchg + cleanup gate did not PASS. No native capability witness was persisted."
            };
        }

        if (dataChange.IncludedMemberReferences.Count == 0 ||
            dataChange.MemberReferences.Count == 0 ||
            string.IsNullOrWhiteSpace(dataChange.RcbReference) ||
            string.IsNullOrWhiteSpace(dataChange.DataSetReference))
        {
            return new DynamicReportNativeFieldCapabilityPersistenceResult
            {
                DataChangeResult = dataChange,
                ProfilePath = dataChange.ProfilePath,
                Failure = "G2.5 PASS did not retain a complete RCB/DataSet/member/included-member evidence set.",
                Summary = "G2.7 P1.7 refused to persist incomplete native capability evidence."
            };
        }

        var observedAt = dataChange.ReportReceivedAtUtc ?? DateTimeOffset.UtcNow;
        var evidenceRoot = "arsas-g27-native-" + Guid.NewGuid().ToString("N");
        var activationEvidenceId = evidenceRoot + "-activation";
        var reportEvidenceId = evidenceRoot + "-dchg-report";

        ArMms.MmsDynamicReportQualificationProfile updatedProfile;
        try
        {
            var activationProof = new ArMms.MmsDynamicRcbActivationProof
            {
                EvidenceId = activationEvidenceId,
                ObservedAtUtc = observedAt,
                RcbReference = dataChange.RcbReference,
                DataSetReference = dataChange.DataSetReference,
                MemberReferences = dataChange.MemberReferences.ToArray(),
                FreshRcbAvailabilityVerified = true,
                DataSetReadbackVerified = true,
                RcbDataSetBindingAccepted = true,
                RptEnaAccepted = true,
                AssociationHealthyAfterActivation = true
            };

            var activationProfile = ArMms.MmsDynamicReportQualificationProfilePolicy.RecordRcbActivationProof(
                dataChange.InputProfile,
                dataChange.Identity,
                activationProof);

            var reportProof = new ArMms.MmsDynamicInformationReportProof
            {
                EvidenceId = reportEvidenceId,
                ObservedAtUtc = observedAt,
                RcbReference = dataChange.RcbReference,
                DataSetReference = dataChange.DataSetReference,
                MemberReferences = dataChange.MemberReferences.ToArray(),
                Kind = ArMms.MmsDynamicInformationReportKind.DataChange,
                ActualInformationReportReceived = true,
                ReportIdentityVerified = true,
                ExactMemberMappingVerified = true,
                AssociationHealthyAfterReport = dataChange.AssociationHealthyAfterReport,
                ReportAuthoritativePointCount = dataChange.IncludedMemberReferences.Count
            };

            updatedProfile = ArMms.MmsDynamicReportQualificationProfilePolicy.RecordInformationReportProof(
                activationProfile,
                dataChange.Identity,
                reportProof);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return new DynamicReportNativeFieldCapabilityPersistenceResult
            {
                DataChangeResult = dataChange,
                ProfilePath = dataChange.ProfilePath,
                Failure = ex.Message,
                Summary = "G2.7 P1.7 rejected the native activation/report profile transition: " + ex.Message
            };
        }

        var capability = new ArMms.MmsDynamicReportNativeFieldCapabilityEvidence
        {
            EvidenceId = evidenceRoot + "-capability",
            ObservedAtUtc = observedAt,
            StableIdentityKey = dataChange.Identity.StableIdentityKey,
            ModelFingerprint = dataChange.Identity.ModelFingerprint,
            ProfileRevision = dataChange.Identity.ProfileRevision,
            RcbReference = dataChange.RcbReference,
            DataSetReference = dataChange.DataSetReference,
            RcbActivationEvidenceId = activationEvidenceId,
            InformationReportEvidenceId = reportEvidenceId,
            IncludedMemberReferences = dataChange.IncludedMemberReferences.ToArray(),
            ActualInformationReportReceived = true,
            DataChangeReasonVerified = dataChange.SpontaneousDataChangeProven,
            GeneralInterrogationDisabled = true,
            ExactMemberMappingVerified = true,
            AssociationHealthyAfterReport = dataChange.AssociationHealthyAfterReport,
            MonitorCleanupSucceeded = dataChange.MonitorCleanupSucceeded,
            ProofFieldRestoreSucceeded = dataChange.ProofFieldRestoreSucceeded,
            FreshCleanupClosureSucceeded = dataChange.FreshCleanupClosureSucceeded
        };

        if (!ArMms.MmsGuardedDynamicReportNativeFieldCapabilityPolicy.TryValidate(
                new ArMms.MmsDynamicReportGuardedRuntimePlanningContext
                {
                    Profile = updatedProfile,
                    CurrentIdentity = dataChange.Identity
                },
                capability,
                out var validationReason))
        {
            return new DynamicReportNativeFieldCapabilityPersistenceResult
            {
                DataChangeResult = dataChange,
                UpdatedProfile = updatedProfile,
                CapabilityEvidence = capability,
                ProfilePath = dataChange.ProfilePath,
                Failure = validationReason,
                Summary = "G2.7 P1.7 engine rejected the just-collected native capability evidence before persistence: " + validationReason
            };
        }

        var profilePersisted = false;
        var witnessPersisted = false;
        var witnessPath = _witnessStore.GetWitnessPath(dataChange.Identity);
        try
        {
            // Save profile first. If sidecar save fails, runtime remains fail-closed because
            // a native DataChange profile cannot authorize P1.7 without its exact witness.
            await _profileStore.SaveAsync(updatedProfile, cancellationToken).ConfigureAwait(false);
            profilePersisted = true;
            await _witnessStore.SaveAsync(capability, cancellationToken).ConfigureAwait(false);
            witnessPersisted = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return new DynamicReportNativeFieldCapabilityPersistenceResult
            {
                DataChangeResult = dataChange,
                UpdatedProfile = updatedProfile,
                CapabilityEvidence = capability,
                ProfilePersisted = profilePersisted,
                CapabilityWitnessPersisted = witnessPersisted,
                ProfilePath = dataChange.ProfilePath,
                WitnessPath = witnessPath,
                Failure = ex.Message,
                Summary = "G2.7 P1.7 persistence did not complete. General Dynamic RCB runtime remains fail-closed until both profile and matching capability witness are durable. " + ex.Message
            };
        }

        progress?.Report(
            "G2.7 P1.7 PASS: native per-IED DataChange + cleanup capability witness persisted. Disconnect/reconnect or restart monitoring to load the new guarded Dynamic RCB runtime authorization.");

        return new DynamicReportNativeFieldCapabilityPersistenceResult
        {
            DataChangeResult = dataChange,
            UpdatedProfile = updatedProfile,
            CapabilityEvidence = capability,
            ProfilePersisted = profilePersisted,
            CapabilityWitnessPersisted = witnessPersisted,
            ProfilePath = dataChange.ProfilePath,
            WitnessPath = witnessPath,
            Summary =
                $"G2.7 P1.7 PASS for {dataChange.Identity.StableIdentityKey}: actual NO-GI dchg + exact mapping + association health + monitor/proof-field/fresh cleanup are durably bound to the current InformationReportProven profile. ProductionEligible remains OFF. Reconnect and Start Monitor to activate general Dynamic RCB planning."
        };
    }
}
