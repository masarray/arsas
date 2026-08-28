using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

/// <summary>
/// P1.5 reviewed compatibility manifest for the one legacy field profile whose persisted
/// G2.4 InformationReport proof is GI-classified even though a later physical A3 run proved
/// a real NO-GI spontaneous dchg InformationReport.
///
/// This is deliberately NOT a wildcard migration. Every identity and envelope field below
/// is exact. Any firmware/model/profile/RCB/member change fails closed and requires new
/// evidence instead of inheriting this compatibility record.
/// </summary>
internal static class DynamicReportGuardedLegacyCompatibilityEvidenceRegistry
{
    internal const string ExpectedStableIdentityKey = "ied:AA1C1F08R4";
    internal const string ExpectedModelFingerprint = "sha256:50c691318c6d6a16b68b121ac48627c26e6e32b937836d559dca1b9eb559f0d9";
    internal const string ExpectedProfileRevision = "e5f7fe9b93524f8019ff7cd01f042fc1827ef32e8b930262a2eafbf20ef357c0";
    internal const string ExpectedRcbReference = "AA1C1F08R4ADD/LLN0.RP.A_URCB01";
    internal const string EvidenceId = "arsas-g26-p1.5-aa1c1f08r4-a3-dchg-field-pass-20260824";

    internal static readonly string[] ExpectedMemberReferences =
    [
        "AA1C1F08R4Q0/CSWI1$ST$Pos$stVal",
        "AA1C1F08R4Q0/XCBR1$ST$Pos$stVal"
    ];

    internal static bool TryResolve(
        ArMms.MmsDynamicReportIedIdentity identity,
        ArMms.MmsDynamicReportQualificationProfile profile,
        out ArMms.MmsDynamicReportLegacyDataChangeCompatibilityEvidence? evidence,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(profile);

        evidence = null;

        if (!Same(identity.StableIdentityKey, ExpectedStableIdentityKey) ||
            !Same(identity.ModelFingerprint, ExpectedModelFingerprint) ||
            !Same(identity.ProfileRevision, ExpectedProfileRevision))
        {
            reason = "No reviewed P1.5 legacy compatibility evidence matches the exact current stable identity, model fingerprint and profile revision.";
            return false;
        }

        if (profile.State != ArMms.MmsDynamicReportQualificationState.InformationReportProven)
        {
            reason = $"Reviewed P1.5 legacy compatibility applies only to the retained InformationReportProven field profile; current state is {profile.State}.";
            return false;
        }

        var envelope = profile.AcceptedEnvelope;
        var activation = profile.RcbActivationProof;
        var report = profile.InformationReportProof;
        if (envelope is null || activation?.IsSuccess != true || report?.IsSuccess != true)
        {
            reason = "Legacy field profile is missing successful accepted-envelope / activation / InformationReport evidence.";
            return false;
        }

        if (report.Kind != ArMms.MmsDynamicInformationReportKind.GeneralInterrogation)
        {
            reason = $"P1.5 legacy compatibility is only for the reviewed GI-classified profile; stored report kind is {report.Kind}.";
            return false;
        }

        if (!Same(activation.RcbReference, ExpectedRcbReference) ||
            !Same(report.RcbReference, ExpectedRcbReference))
        {
            reason = "Legacy field profile RCB does not exactly match the reviewed A_URCB01 physical evidence.";
            return false;
        }

        if (!ExactSequence(activation.MemberReferences, ExpectedMemberReferences) ||
            !ExactSequence(report.MemberReferences, ExpectedMemberReferences) ||
            !ExactSequence(envelope.ExactProvenMemberReferences, ExpectedMemberReferences))
        {
            reason = "Legacy field profile ordered member sequence does not exactly match the reviewed Q0 CSWI/XCBR A3 evidence.";
            return false;
        }

        // Physical G2.6-P1 A3 evidence supplied during field validation:
        // - exact URCB: AA1C1F08R4ADD/LLN0.RP.A_URCB01
        // - temporary DataSet: AA1C1F08R4ADD/LLN0.AR_G25A_4E20EC7E
        // - actual spontaneous InformationReport with reason=data-change
        // - included DataSet indexes [0,1] mapped in order to the exact CSWI/XCBR members above
        // - GI disabled for the dchg proof
        // - association healthy after report
        // - monitor cleanup, TrgOps/OptFlds restore, and fresh-association closure all passed.
        evidence = new ArMms.MmsDynamicReportLegacyDataChangeCompatibilityEvidence
        {
            EvidenceId = EvidenceId,
            StableIdentityKey = ExpectedStableIdentityKey,
            ModelFingerprint = ExpectedModelFingerprint,
            ProfileRevision = ExpectedProfileRevision,
            RcbReference = ExpectedRcbReference,
            MemberReferences = ExpectedMemberReferences,
            ActualInformationReportReceived = true,
            DataChangeReasonVerified = true,
            GeneralInterrogationDisabled = true,
            ExactMemberMappingVerified = true,
            AssociationHealthyAfterReport = true,
            CleanupSucceeded = true
        };
        reason = "Reviewed AA1C1F08R4 P1.5 legacy compatibility evidence matched exact identity, A_URCB01 and ordered Q0 CSWI/XCBR members.";
        return true;
    }

    private static bool ExactSequence(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
    {
        if (actual.Count != expected.Count)
            return false;

        for (var index = 0; index < actual.Count; index++)
        {
            if (!Same(actual[index], expected[index]))
                return false;
        }

        return true;
    }

    private static bool Same(string? left, string? right)
        => string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
}
