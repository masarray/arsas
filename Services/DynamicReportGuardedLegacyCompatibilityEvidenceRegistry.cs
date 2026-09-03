using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

/// <summary>
/// P1.5b reviewed compatibility manifest for the one legacy field profile whose persisted
/// G2.4 InformationReport proof is GI-classified even though a later physical A3 run proved
/// a real NO-GI spontaneous dchg InformationReport for an exact ordered subset.
///
/// This is deliberately NOT a wildcard migration. The persisted six-member chain remains
/// unchanged qualification evidence. The later A3 proof authorizes only the exact two-member
/// Q0 CSWI/XCBR dchg subset on the same exact identity and RCB.
/// </summary>
internal static class DynamicReportGuardedLegacyCompatibilityEvidenceRegistry
{
    internal const string ExpectedStableIdentityKey = "ied:AA1C1F08R4";
    internal const string ExpectedModelFingerprint = "sha256:50c691318c6d6a16b68b121ac48627c26e6e32b937836d559dca1b9eb559f0d9";
    internal const string ExpectedProfileRevision = "e5f7fe9b93524f8019ff7cd01f042fc1827ef32e8b930262a2eafbf20ef357c0";
    internal const string ExpectedRcbReference = "AA1C1F08R4ADD/LLN0.RP.A_URCB01";
    internal const string EvidenceId = "arsas-g26-p1.5b-aa1c1f08r4-a3-dchg-subset-field-pass-20260824";

    internal static readonly string[] ExpectedPersistedMemberReferences =
    [
        "AA1C1F08R4Q0/CSWI1$ST$Pos$stVal",
        "AA1C1F08R4Q0/XCBR1$ST$Pos$stVal",
        "AA1C1F08R4Q0/CSWI1$ST$Beh$stVal",
        "AA1C1F08R4Q0/CSWI1$ST$Health$stVal",
        "AA1C1F08R4Q0/CSWI1$ST$Loc$stVal",
        "AA1C1F08R4Q0/CSWI1$ST$LocKey$stVal"
    ];

    internal static readonly string[] ExpectedDataChangeSubsetMemberReferences =
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
            reason = "No reviewed P1.5b compatibility evidence matches the exact current stable identity, model fingerprint and profile revision.";
            return false;
        }

        if (profile.State != ArMms.MmsDynamicReportQualificationState.InformationReportProven)
        {
            reason = $"Reviewed P1.5b compatibility applies only to the retained InformationReportProven field profile; current state is {profile.State}.";
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
            reason = $"P1.5b compatibility is only for the reviewed GI-classified profile; stored report kind is {report.Kind}.";
            return false;
        }

        if (!Same(activation.RcbReference, ExpectedRcbReference) ||
            !Same(report.RcbReference, ExpectedRcbReference))
        {
            reason = "Legacy field profile RCB does not exactly match the reviewed A_URCB01 evidence.";
            return false;
        }

        if (!Same(activation.DataSetReference, report.DataSetReference))
        {
            reason = "Legacy field profile activation/report DataSet identities differ.";
            return false;
        }

        if (!ExactSequence(activation.MemberReferences, ExpectedPersistedMemberReferences) ||
            !ExactSequence(report.MemberReferences, ExpectedPersistedMemberReferences) ||
            !ExactSequence(envelope.ExactProvenMemberReferences, ExpectedPersistedMemberReferences))
        {
            reason = "Legacy field profile does not exactly match the reviewed six-member persisted qualification chain.";
            return false;
        }

        if (!IsOrderedSubset(ExpectedDataChangeSubsetMemberReferences, report.MemberReferences) ||
            !IsOrderedSubset(ExpectedDataChangeSubsetMemberReferences, envelope.ExactProvenMemberReferences))
        {
            reason = "Reviewed Q0 CSWI/XCBR A3 dchg members are not an ordered subset of the persisted qualification chain.";
            return false;
        }

        // Physical G2.6-P1 A3 evidence supplied during field validation:
        // - exact URCB: AA1C1F08R4ADD/LLN0.RP.A_URCB01
        // - temporary DataSet: AA1C1F08R4ADD/LLN0.AR_G25A_4E20EC7E
        // - actual spontaneous InformationReport with reason=data-change
        // - included DataSet indexes [0,1] mapped in order to CSWI1.Pos.stVal / XCBR1.Pos.stVal
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
            MemberReferences = ExpectedDataChangeSubsetMemberReferences,
            ActualInformationReportReceived = true,
            DataChangeReasonVerified = true,
            GeneralInterrogationDisabled = true,
            ExactMemberMappingVerified = true,
            AssociationHealthyAfterReport = true,
            CleanupSucceeded = true
        };
        reason =
            "Reviewed AA1C1F08R4 P1.5b evidence matched the exact six-member persisted chain and the exact ordered two-member Q0 CSWI/XCBR NO-GI dchg subset on A_URCB01.";
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

    private static bool IsOrderedSubset(IReadOnlyList<string> subset, IReadOnlyList<string> full)
    {
        var searchIndex = 0;
        foreach (var candidate in subset)
        {
            var found = false;
            while (searchIndex < full.Count)
            {
                if (Same(candidate, full[searchIndex]))
                {
                    found = true;
                    searchIndex++;
                    break;
                }
                searchIndex++;
            }

            if (!found)
                return false;
        }

        return true;
    }

    private static bool Same(string? left, string? right)
        => string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
}
