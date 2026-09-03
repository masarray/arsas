using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

/// <summary>
/// P1.7 field recovery wrapper for the existing G2.3 qualification service.
///
/// The underlying G2.3 coordinator intentionally stops when an attempt loses association
/// continuity or cannot prove DeleteNamedVariableList cleanup. That stop remains authoritative.
/// This wrapper performs exactly one recovery cycle on a NEW MMS association, using the
/// ARIEC exact-residue recovery primitive, then retries the unchanged G2.3 service once.
/// There is no retry loop and no profile is synthesized from the failed first attempt.
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

        progress?.Report(
            $"G2.7 P1.7 G2.3 recovery: first bounded attempt requires a fresh association. Closing the failed transaction and inspecting exact temporary DataSet {failedAttempt.DataSetReference} before any retry…");

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
                var evidence = first.EvidenceLines
                    .Append($"G2.3 fresh recovery exception: {ex.GetType().Name}: {ex.Message}")
                    .ToArray();
                return Copy(
                    first,
                    false,
                    "G2.3 fresh-association recovery could not complete. No retry and no profile advancement were allowed. " + ex.Message,
                    evidence);
            }
        }

        var combined = first.EvidenceLines
            .Concat(recovery.EvidenceLines.Select(line => "G2.3 recovery: " + line))
            .Append($"G2.3 recovery result: success={recovery.IsSuccess}; deleteAttempted={recovery.DeleteAttempted}; exactMembers={recovery.ExactMembersVerifiedBeforeDelete}; associationHealthy={recovery.AssociationHealthy}; summary={recovery.Summary}")
            .ToArray();

        if (!recovery.IsSuccess)
        {
            return Copy(
                first,
                false,
                "G2.3 fresh-association cleanup closure failed closed. No retry and no profile advancement were allowed. " + recovery.Summary,
                combined);
        }

        progress?.Report(
            "G2.7 P1.7 G2.3 recovery PASS: exact temporary qualification residue is closed on a fresh association. Retrying the unchanged bounded G2.3 ladder exactly once on another fresh association…");

        var retry = await new DynamicReportQualificationCommissioningService(_profileStore)
            .RunAsync(device, fullModelSignals, cancellationToken)
            .ConfigureAwait(false);
        var retryEvidence = combined
            .Append("G2.3 one-retry boundary: recovery passed; exactly one new G2.3 commissioning run was started.")
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
