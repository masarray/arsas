using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

/// <summary>
/// P1.7 field recovery wrapper for the existing G2.3 qualification service.
///
/// The underlying G2.3 coordinator intentionally stops when an attempt loses association
/// continuity or cannot prove DeleteNamedVariableList cleanup. That stop remains authoritative.
/// This wrapper performs exactly one recovery cycle on a NEW MMS association, using the
/// ARIEC exact-residue recovery primitive.
///
/// After exact fresh-association cleanup closure, a prior cleanup-safe, association-surviving
/// multi-member milestone from the same first ladder may be retained as the bounded G2.3
/// envelope. A later failed larger milestone is never generalized as safe. If there was no
/// earlier cleanup-safe multi-member envelope, the unchanged G2.3 service may be retried once.
/// There is no retry loop and this path never promotes ProductionEligible.
/// </summary>
internal sealed class DynamicReportQualificationFreshRecoveryCommissioningService
{
    private static readonly TimeSpan AuxiliaryAssociationTimeout = TimeSpan.FromSeconds(10);
    private readonly DynamicReportQualificationProfileStore _profileStore;

    public DynamicReportQualificationFreshRecoveryCommissioningService(
        DynamicReportQualificationProfileStore? profileStore = null)
    {
        _profileStore = profileStore ?? new DynamicReportQualificationProfileStore();
    }

    public async Task<DynamicReportQualificationCommissioningResult> RunAsync(
        Iec61850MonitorDevice device,
        IReadOnlyList<SignalDefinition> fullModelSignals,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fullModelSignals);

        var first = await new DynamicReportQualificationCommissioningService(_profileStore)
            .RunAsync(device, fullModelSignals, cancellationToken)
            .ConfigureAwait(false);
        if (first.IsSuccess || first.Coordinator?.RequiresFreshAssociation != true)
            return first;

        var failedAttempt = first.Coordinator.Attempts
            .LastOrDefault(attempt => attempt.RequiresFreshAssociation);
        if (failedAttempt is null ||
            string.IsNullOrWhiteSpace(failedAttempt.DataSetReference) ||
            failedAttempt.MemberReferences.Count == 0)
        {
            return Copy(
                first,
                false,
                "G2.3 requested a fresh association but did not retain one exact failed DataSet/member attempt for safe recovery. No recovery mutation was attempted.",
                first.EvidenceLines.Append("G2.3 fresh recovery blocked: exact failed attempt evidence is missing.").ToArray());
        }

        if (!IsExactCurrentRunG23TemporaryDataSet(failedAttempt.DataSetReference))
        {
            return Copy(
                first,
                false,
                "G2.3 fresh recovery refused the failed DataSet identity because it is not an exact ARQ<8-hex> temporary qualification name created by this commissioning path.",
                first.EvidenceLines.Append("G2.3 fresh recovery blocked: temporary DataSet identity failed the ARQ current-run naming contract.").ToArray());
        }

        var triggerEvidence = first.EvidenceLines
            .Append(
                $"G2.3 recovery trigger: failedAttempt={failedAttempt.AttemptId}; dataset={failedAttempt.DataSetReference}; " +
                $"members={failedAttempt.MemberCount}; failureStage={failedAttempt.FailureStage}; " +
                $"associationSurvived={failedAttempt.AssociationSurvived}; cleanupSucceeded={failedAttempt.CleanupSucceeded}; " +
                $"dynamicMutationAttempted={failedAttempt.DynamicMutationAttempted}.")
            .ToArray();

        progress?.Report(
            $"G2.7 P1.7 G2.3 recovery: failedAttempt={failedAttempt.AttemptId}; members={failedAttempt.MemberCount}; " +
            $"failureStage={failedAttempt.FailureStage}; associationSurvived={failedAttempt.AssociationSurvived}; " +
            $"cleanupSucceeded={failedAttempt.CleanupSucceeded}. Opening a fresh association to inspect exact temporary DataSet {failedAttempt.DataSetReference}…");

        ArMms.MmsDynamicDataSetQualificationRecoveryResult recovery;
        await using (var fresh = new ArMms.MmsClientSession())
        {
            try
            {
                await fresh.ConnectAsync(
                    device.IpAddress,
                    device.Port,
                    AuxiliaryAssociationTimeout,
                    cancellationToken).ConfigureAwait(false);
                recovery = await fresh.RecoverDynamicDataSetQualificationResidueAsync(
                    failedAttempt.DataSetReference,
                    failedAttempt.MemberReferences,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException or ArgumentException)
            {
                var evidence = triggerEvidence
                    .Append($"G2.3 fresh recovery exception: {ex.GetType().Name}: {ex.Message}")
                    .ToArray();
                return Copy(
                    first,
                    false,
                    "G2.3 fresh-association recovery could not complete. No retry and no profile advancement were allowed. " + ex.Message,
                    evidence);
            }
        }

        var combined = triggerEvidence
            .Concat(recovery.EvidenceLines.Select(line => "G2.3 recovery: " + line))
            .Append(
                $"G2.3 recovery result: success={recovery.IsSuccess}; deleteAttempted={recovery.DeleteAttempted}; " +
                $"exactMembers={recovery.ExactMembersVerifiedBeforeDelete}; associationHealthy={recovery.AssociationHealthy}; " +
                $"namespaceAbsenceBefore={recovery.NamespaceAbsenceProvenBefore}; directoryAbsenceBefore={recovery.DirectoryAbsenceProvenBefore}; " +
                $"namespaceAbsenceAfter={recovery.NamespaceAbsenceProvenAfter}; directoryAbsenceAfter={recovery.DirectoryAbsenceProvenAfter}; " +
                $"summary={recovery.Summary}")
            .ToArray();

        if (!recovery.IsSuccess)
        {
            return Copy(
                first,
                false,
                "G2.3 fresh-association cleanup closure failed closed. No retry and no profile advancement were allowed. " + recovery.Summary,
                combined);
        }

        var retainedAttempt = LargestCleanupSafeMultiMemberAttempt(first.Coordinator);
        if (retainedAttempt is not null && first.Identity is not null)
        {
            try
            {
                var acceptedEnvelope = ArMms.MmsDynamicDataSetQualificationLadder.AcceptExactEnvelope(
                    first.Coordinator.Assessment,
                    retainedAttempt.AttemptId);
                var fieldEvidenceId =
                    $"arsas-g2.3-recovered-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
                var profile = ArMms.MmsDynamicReportQualificationProfilePolicy.CreateEnvelopeQualifiedProfile(
                    first.Identity,
                    acceptedEnvelope,
                    first.Coordinator.Assessment,
                    capacityEvidence: null,
                    sourceEvidenceId: fieldEvidenceId,
                    nowUtc: DateTimeOffset.UtcNow);

                await _profileStore.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
                var profilePath = _profileStore.GetProfilePath(first.Identity);
                var evidence = combined
                    .Append(
                        $"G2.3 recovered-envelope acceptance: sourceAttempt={retainedAttempt.AttemptId}; " +
                        $"members={retainedAttempt.MemberCount}; requestBytes={retainedAttempt.DefineRequestByteCount}; " +
                        $"profileState={profile.State}; path={profilePath}; laterFailedAttempt={failedAttempt.AttemptId}; " +
                        "later failed larger milestone is not generalized.")
                    .Append(
                        "G2.3 recovered-envelope safety: exact failed residue closure was proven first; EnvelopeQualified is NOT RcbActivationProven, NOT InformationReportProven, and NOT ProductionEligible.")
                    .ToArray();

                progress?.Report(
                    $"G2.7 P1.7 G2.3 recovery PASS: exact residue closure proven. Retained the largest prior cleanup-safe multi-member envelope " +
                    $"({retainedAttempt.MemberCount} member(s), attempt={retainedAttempt.AttemptId}). The later failed larger milestone is not generalized. Continuing to G2.4…");

                return new DynamicReportQualificationCommissioningResult
                {
                    IsSuccess = true,
                    IsBlocked = false,
                    Summary =
                        $"G2.3 fresh-association recovery PASS and retained the largest prior cleanup-safe multi-member envelope: " +
                        $"{retainedAttempt.MemberCount} member(s) from {retainedAttempt.AttemptId}. The later failed larger milestone is not generalized; " +
                        "G2.4/G2.5 physical report proof is still required before normal-runtime Dynamic RCB authorization.",
                    Identity = first.Identity,
                    Candidates = first.Candidates,
                    Coordinator = first.Coordinator,
                    SavedProfile = profile,
                    ProfilePath = profilePath,
                    EvidenceLines = evidence
                };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                var evidence = combined
                    .Append($"G2.3 recovered-envelope persistence failed closed: {ex.GetType().Name}: {ex.Message}")
                    .ToArray();
                return Copy(
                    first,
                    false,
                    "G2.3 exact residue recovery passed, but the prior clean envelope could not be safely persisted. No profile advancement was allowed. " + ex.Message,
                    evidence);
            }
        }

        progress?.Report(
            "G2.7 P1.7 G2.3 recovery PASS, but no earlier cleanup-safe multi-member envelope exists. Retrying the unchanged bounded G2.3 ladder exactly once on another fresh association…");

        var retry = await new DynamicReportQualificationCommissioningService(_profileStore)
            .RunAsync(device, fullModelSignals, cancellationToken)
            .ConfigureAwait(false);
        var retryEvidence = combined
            .Append("G2.3 one-retry boundary: recovery passed; no earlier cleanup-safe multi-member envelope existed; exactly one new G2.3 commissioning run was started.")
            .Concat(retry.EvidenceLines.Select(line => "G2.3 retry: " + line))
            .ToArray();

        if (!retry.IsSuccess)
        {
            var repeatedFresh = retry.Coordinator?.RequiresFreshAssociation == true;
            return Copy(
                retry,
                false,
                repeatedFresh
                    ? "G2.3 retry again lost association continuity or cleanup proof after a successful fresh cleanup closure. This is repeated physical mutation-instability evidence; automatic retry is stopped. " + retry.Summary
                    : "G2.3 one-time retry did not produce a cleanup-safe multi-member envelope. Automatic retry is stopped. " + retry.Summary,
                retryEvidence);
        }

        return Copy(
            retry,
            true,
            "G2.3 fresh-association recovery PASS followed by one clean bounded retry. " + retry.Summary,
            retryEvidence);
    }

    internal static ArMms.MmsDynamicDataSetQualificationAttemptEvidence? LargestCleanupSafeMultiMemberAttempt(
        ArMms.MmsDynamicDataSetQualificationCoordinatorResult coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        return coordinator.Attempts
            .Where(attempt => attempt.IsQualificationSuccess && attempt.MemberCount > 1)
            .OrderByDescending(attempt => attempt.MemberCount)
            .ThenByDescending(attempt => attempt.DefineRequestByteCount)
            .ThenBy(attempt => attempt.AttemptId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    internal static bool IsExactCurrentRunG23TemporaryDataSet(string? reference)
    {
        var text = (reference ?? string.Empty).Trim().Replace('$', '.');
        var slash = text.IndexOf('/');
        if (slash <= 0 || slash >= text.Length - 1)
            return false;

        const string prefix = "LLN0.ARQ";
        var item = text[(slash + 1)..];
        if (!item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var suffix = item[prefix.Length..];
        return suffix.Length == 8 && suffix.All(Uri.IsHexDigit);
    }

    private static DynamicReportQualificationCommissioningResult Copy(
        DynamicReportQualificationCommissioningResult source,
        bool success,
        string summary,
        IReadOnlyList<string> evidence)
        => new()
        {
            IsSuccess = success,
            IsBlocked = source.IsBlocked,
            Summary = summary,
            Identity = source.Identity,
            Candidates = source.Candidates,
            Coordinator = source.Coordinator,
            SavedProfile = success ? source.SavedProfile : null,
            ProfilePath = source.ProfilePath,
            EvidenceLines = evidence.ToArray()
        };
}
