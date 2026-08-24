using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

internal sealed class DynamicReportShadowVerificationAcceptanceResult
{
    public bool IsSuccess { get; init; }
    public bool IsBlocked { get; init; }
    public string Summary { get; init; } = string.Empty;
    public ArMms.MmsDynamicReportShadowVerificationResult? Shadow { get; init; }
    public ArMms.MmsDynamicReportProductionAcceptance? ProductionAcceptanceCandidate { get; init; }
    public ArMms.MmsDynamicReportQualificationProfile? InputProfile { get; init; }
    public string ProfilePath { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();
}

/// <summary>
/// ARSAS-side G2.6 gate from physical report-vs-poll observations to a typed
/// production-acceptance candidate. This service deliberately does not persist or
/// advance the qualification profile. A successful shadow therefore remains weaker
/// than ProductionEligible and production automatic dynamic reporting stays OFF.
/// </summary>
internal sealed class DynamicReportShadowVerificationAcceptanceService
{
    internal static readonly ArMms.MmsDynamicReportShadowVerificationOptions ProductionShadowOptions = new()
    {
        MinimumReportEdges = 2,
        MaximumReportToPollLag = TimeSpan.FromSeconds(3),
        MaximumPollTransitionToReportLag = TimeSpan.FromSeconds(3),
        MaximumDeviceTimestampDelta = TimeSpan.FromMilliseconds(250),
        RequireQualityEvidence = true,
        RequireDeviceTimestampEvidence = true,
        RequireReconnectCycle = true,
        MaximumDynamicActivationAttemptsPerAssociation = 1
    };

    private readonly DynamicReportQualificationProfileStore _profileStore;

    public DynamicReportShadowVerificationAcceptanceService(
        DynamicReportQualificationProfileStore? profileStore = null)
    {
        _profileStore = profileStore ?? new DynamicReportQualificationProfileStore();
    }

    public async Task<DynamicReportShadowVerificationAcceptanceResult> EvaluateAsync(
        Iec61850MonitorDevice device,
        IReadOnlyList<SignalDefinition> fullModelSignals,
        ArMms.MmsDynamicReportShadowVerificationEvidence evidence,
        bool controlRegressionPassed,
        bool staticReportingRegressionPassed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fullModelSignals);
        ArgumentNullException.ThrowIfNull(evidence);

        var lines = new List<string>
        {
            "G2.6 shadow acceptance contract: exact InformationReportProven identity/member envelope -> typed report-vs-independent-MMS shadow -> candidate production acceptance only.",
            "G2.6 shadow safety: this service performs no MMS network I/O, no RCB/DataSet write, no profile save, and never calls MarkProductionEligible.",
            "G2.6 production safety: Shadow PASS != ProductionEligible; production automatic dynamic reporting remains OFF until a separate explicit promotion gate closes."
        };

        ArMms.MmsDynamicReportIedIdentity identity;
        try
        {
            identity = DynamicReportQualificationIdentity.Build(device, fullModelSignals);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Blocked("Shadow identity preflight failed: " + ex.Message, lines);
        }

        var loaded = await _profileStore.LoadAsync(identity, cancellationToken).ConfigureAwait(false);
        lines.Add($"G2.6 shadow profile: exists={loaded.Exists}; valid={loaded.IsValid}; state={loaded.Profile?.State.ToString() ?? "-"}; reason={loaded.Reason}");
        if (!loaded.IsValid || loaded.Profile is null)
            return Blocked("Shadow verification requires the exact identity-compatible persisted profile.", lines, loaded.FilePath);

        var profile = loaded.Profile;
        if (profile.State != ArMms.MmsDynamicReportQualificationState.InformationReportProven ||
            profile.RcbActivationProof?.IsSuccess != true ||
            profile.InformationReportProof?.IsSuccess != true)
        {
            return Blocked(
                $"Shadow verification requires a complete InformationReportProven profile; current state is {profile.State}.",
                lines,
                loaded.FilePath,
                profile);
        }

        var qualifiedMembers = profile.RcbActivationProof.MemberReferences.ToArray();
        if (qualifiedMembers.Length == 0)
            return Blocked("Persisted report proof contains no exact member sequence.", lines, loaded.FilePath, profile);

        if (!ExactSequenceEquals(qualifiedMembers, evidence.MemberReferences))
        {
            lines.Add("G2.6 shadow expected members: " + string.Join(" | ", qualifiedMembers));
            lines.Add("G2.6 shadow observed members: " + string.Join(" | ", evidence.MemberReferences));
            return Blocked(
                "Shadow evidence member sequence does not exactly match the InformationReport-proven DataSet envelope.",
                lines,
                loaded.FilePath,
                profile);
        }

        lines.Add($"G2.6 shadow exact envelope: rcb={profile.RcbActivationProof.RcbReference}; members={qualifiedMembers.Length}; evidenceId={evidence.EvidenceId}; reports={evidence.ReportObservations.Count}; polls={evidence.PollObservations.Count}; reconnect={evidence.SuccessfulReconnects}/{evidence.ReconnectAttempts}; dynamicAttempts={evidence.DynamicActivationAttempts}");

        ArMms.MmsDynamicReportShadowVerificationResult shadow;
        try
        {
            shadow = ArMms.MmsDynamicReportShadowVerificationPolicy.Evaluate(
                evidence,
                ProductionShadowOptions);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException or OverflowException)
        {
            lines.Add($"G2.6 shadow evaluator rejected evidence: {ex.GetType().Name}: {ex.Message}");
            return Blocked("Typed shadow evidence is invalid and cannot be accepted.", lines, loaded.FilePath, profile);
        }

        lines.Add("G2.6 typed shadow: " + shadow.Summary);
        lines.Add($"G2.6 typed gates: identity={shadow.ExactMemberIdentityPassed}; value={shadow.ValueParityPassed}; quality={shadow.QualityParityPassed}; timestamp={shadow.TimestampParityPassed}; order={shadow.ReportOrderPassed}; noMissing={shadow.NoMissingReportEdgesPassed}; noDuplicate={shadow.NoDuplicateReportEdgesPassed}; pollingAuthority={shadow.PollingAuthorityGuardPassed}; reconnect={shadow.ReconnectRegressionPassed}; noMutationLoop={shadow.NoRepeatedMutationLoopPassed}");
        foreach (var failure in shadow.Failures)
            lines.Add("G2.6 shadow failure: " + failure);

        if (!shadow.IsSuccess)
        {
            return new DynamicReportShadowVerificationAcceptanceResult
            {
                Summary = "G2.6 shadow did not close every report-vs-poll gate. Profile remains InformationReportProven; ProductionEligible is OFF.",
                Shadow = shadow,
                InputProfile = profile,
                ProfilePath = loaded.FilePath,
                EvidenceLines = lines.ToArray()
            };
        }

        var acceptance = ArMms.MmsDynamicReportShadowVerificationPolicy.BuildProductionAcceptance(
            evidence,
            shadow,
            controlRegressionPassed,
            staticReportingRegressionPassed);

        lines.Add($"G2.6 acceptance candidate: control={acceptance.ControlRegressionPassed}; staticReporting={acceptance.StaticReportingRegressionPassed}; dynamicInformationReport={acceptance.DynamicInformationReportRegressionPassed}; pollingAuthority={acceptance.PollingAuthorityGuardPassed}; reconnect={acceptance.ReconnectRegressionPassed}; quality={acceptance.QualityRegressionPassed}; noMutationLoop={acceptance.NoRepeatedMutationLoopPassed}; allPassed={acceptance.AllPassed}");
        lines.Add("G2.6 state boundary: candidate was NOT persisted and MarkProductionEligible was NOT called. Shadow PASS != ProductionEligible.");

        return new DynamicReportShadowVerificationAcceptanceResult
        {
            IsSuccess = shadow.IsSuccess && acceptance.AllPassed,
            Summary = acceptance.AllPassed
                ? "G2.6 shadow and independent control/static regression inputs form a complete production-acceptance candidate. Profile is intentionally unchanged at InformationReportProven; explicit promotion remains a separate step."
                : "G2.6 shadow passed, but independent control/static regression acceptance is incomplete. Profile remains InformationReportProven; ProductionEligible is OFF.",
            Shadow = shadow,
            ProductionAcceptanceCandidate = acceptance,
            InputProfile = profile,
            ProfilePath = loaded.FilePath,
            EvidenceLines = lines.ToArray()
        };
    }

    internal static bool ExactSequenceEquals(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        var left = expected.Select(NormalizeReference).ToArray();
        var right = actual.Select(NormalizeReference).ToArray();
        return left.Length == right.Length && left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');

    private static DynamicReportShadowVerificationAcceptanceResult Blocked(
        string summary,
        IReadOnlyList<string> evidence,
        string profilePath = "",
        ArMms.MmsDynamicReportQualificationProfile? profile = null)
        => new()
        {
            IsBlocked = true,
            Summary = summary + " Production automatic dynamic reporting remains OFF.",
            InputProfile = profile,
            ProfilePath = profilePath,
            EvidenceLines = evidence.ToArray()
        };
}
