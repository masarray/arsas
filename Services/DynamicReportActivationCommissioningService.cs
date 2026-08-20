using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

internal sealed class DynamicReportActivationCommissioningResult
{
    public bool IsSuccess { get; init; }
    public bool IsBlocked { get; init; }
    public bool CleanupSucceeded { get; init; }
    public string Summary { get; init; } = string.Empty;
    public ArMms.MmsDynamicReportIedIdentity? Identity { get; init; }
    public ArMms.MmsDynamicReportQualificationProfile? InputProfile { get; init; }
    public ArMms.MmsDynamicReportQualificationProfile? SavedProfile { get; init; }
    public ArMms.MmsDynamicRcbActivationProof? ActivationProof { get; init; }
    public ArMms.MmsDynamicInformationReportProof? InformationReportProof { get; init; }
    public string RcbReference { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public IReadOnlyList<string> MemberReferences { get; init; } = Array.Empty<string>();
    public string ProfilePath { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();
}

internal sealed class DynamicReportFrameValidation
{
    public bool IsSuccess { get; init; }
    public string Reason { get; init; } = string.Empty;
    public ArMms.MmsDynamicInformationReportKind Kind { get; init; } = ArMms.MmsDynamicInformationReportKind.Unknown;
    public int AuthoritativePointCount { get; init; }
}

/// <summary>
/// G2.4 explicit commissioning gate. This service never changes the production
/// monitoring planner. It consumes only an identity-compatible EnvelopeQualified
/// profile, opens an auxiliary association, claims exactly one live-verified empty
/// URCB, creates one temporary DataSet inside the proven envelope, and advances the
/// persisted profile only after an actual strictly mapped InformationReport is seen
/// and cleanup is proven.
/// </summary>
internal sealed class DynamicReportActivationCommissioningService
{
    internal const int MaximumG24Members = 8;
    private static readonly TimeSpan AuxiliaryAssociationTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan InformationReportProofWindow = TimeSpan.FromSeconds(10);

    private readonly DynamicReportQualificationProfileStore _profileStore;

    public DynamicReportActivationCommissioningService(
        DynamicReportQualificationProfileStore? profileStore = null)
    {
        _profileStore = profileStore ?? new DynamicReportQualificationProfileStore();
    }

    public async Task<DynamicReportActivationCommissioningResult> RunAsync(
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
            return Blocked("G2.4 identity preflight failed: " + ex.Message, evidence);
        }

        evidence.Add($"G2.4 identity stableKey={identity.StableIdentityKey}; fingerprint={identity.ModelFingerprint}; profileRevision={TextOrDash(identity.ProfileRevision)}");

        var loaded = await _profileStore.LoadAsync(identity, cancellationToken).ConfigureAwait(false);
        evidence.Add($"G2.4 persisted profile: exists={loaded.Exists}; valid={loaded.IsValid}; reason={loaded.Reason}");
        if (!loaded.IsValid || loaded.Profile is null)
        {
            return Blocked(
                "G2.4 requires an identity-compatible persisted G2.3 EnvelopeQualified profile. Run the clean G2.3 qualification first.",
                evidence,
                identity,
                loaded.FilePath);
        }

        var profile = loaded.Profile;
        if (profile.State == ArMms.MmsDynamicReportQualificationState.InformationReportProven ||
            profile.State == ArMms.MmsDynamicReportQualificationState.ProductionEligible)
        {
            return new DynamicReportActivationCommissioningResult
            {
                IsBlocked = true,
                Summary = $"The compatible profile is already {profile.State}; G2.4 will not repeat active RCB mutation or downgrade evidence.",
                Identity = identity,
                InputProfile = profile,
                SavedProfile = profile,
                ProfilePath = loaded.FilePath,
                EvidenceLines = evidence
            };
        }

        if (profile.State != ArMms.MmsDynamicReportQualificationState.EnvelopeQualified ||
            profile.AcceptedEnvelope is null)
        {
            return Blocked(
                $"G2.4 requires profile state EnvelopeQualified, but the compatible profile is {profile.State}. Fail closed rather than infer missing field evidence.",
                evidence,
                identity,
                loaded.FilePath,
                profile);
        }

        var qualifiedReferences = profile.AcceptedEnvelope.ExactProvenMemberReferences
            .Take(Math.Min(MaximumG24Members, profile.ProvenSafeMemberCount))
            .ToArray();
        if (qualifiedReferences.Length == 0 || qualifiedReferences.Length > profile.ProvenSafeMemberCount)
        {
            return Blocked(
                "The accepted envelope has no usable exact member sequence. G2.4 cannot create a DataSet from inferred or unqualified members.",
                evidence,
                identity,
                loaded.FilePath,
                profile);
        }

        evidence.Add($"G2.4 envelope gate: profileState={profile.State}; provenMembers={profile.ProvenSafeMemberCount}; commissioningMembers={qualifiedReferences.Length}; hardCeiling={MaximumG24Members}");
        evidence.Add("G2.4 exact qualified members: " + string.Join(" | ", qualifiedReferences));

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
            evidence.Add($"G2.4 auxiliary association failed: {ex.GetType().Name}: {ex.Message}");
            return Blocked(
                "The isolated G2.4 auxiliary MMS association was not established. No RCB or dynamic DataSet mutation was attempted.",
                evidence,
                identity,
                loaded.FilePath,
                profile);
        }

        evidence.Add($"G2.4 auxiliary association ready: state={auxiliary.State}; handshake={TextOrDash(auxiliary.LastHandshakeMessage)}");

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
            evidence.Add($"G2.4 auxiliary discovery failed: {ex.GetType().Name}: {ex.Message}");
            return Failed(
                "Fresh auxiliary discovery failed before any G2.4 RCB mutation.",
                evidence,
                identity,
                profile,
                loaded.FilePath);
        }

        evidence.Add($"G2.4 auxiliary discovery: {discovery.Summary}");

        if (!TryResolveExactQualifiedMembers(
                discovery.IedDirectory,
                qualifiedReferences,
                out var exactPoints,
                out var exactReason))
        {
            evidence.Add("G2.4 exact member revalidation failed: " + exactReason);
            return Failed(
                "The persisted qualified envelope no longer maps exactly to the live MMS model. No RCB mutation was attempted.",
                evidence,
                identity,
                profile,
                loaded.FilePath,
                qualifiedReferences);
        }

        foreach (var point in exactPoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await auxiliary.ReadSingleVariableAsync(point.ToObjectReference(), cancellationToken).ConfigureAwait(false);
            evidence.Add($"G2.4 direct-read {point.MmsReference}: success={read.IsSuccess}; result={read.Message}");
            if (!read.IsSuccess || !auxiliary.IsMmsInitiated)
            {
                return Failed(
                    "An exact G2.3 member failed fresh direct MMS validation. No RCB mutation was attempted.",
                    evidence,
                    identity,
                    profile,
                    loaded.FilePath,
                    qualifiedReferences);
            }
        }

        var selectedRcb = SelectQualifiedUrcb(discovery.ReportInventory, exactPoints[0].Domain, out var rcbSelectionReason);
        evidence.Add("G2.4 URCB selection: " + rcbSelectionReason);
        if (selectedRcb is null)
        {
            return Failed(
                "No live-proven empty URCB with GI, DataSet-name reporting and a usable RptID is available for strict G2.4 proof. No RCB mutation was attempted.",
                evidence,
                identity,
                profile,
                loaded.FilePath,
                qualifiedReferences);
        }

        var dataSetName = "AR_G24_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var plan = ArMms.MmsReportSubscriptionPlanner.BuildDynamicPlan(
            discovery.ReportInventory,
            discovery.IedDirectory,
            exactPoints.Select(point => point.UserReference),
            preferredLogicalDevice: selectedRcb.Domain,
            preferredRcbReference: selectedRcb.Reference,
            dataSetName: dataSetName,
            strictRcb: true,
            allowUrCbFallback: true,
            allowPollingFallback: false);

        if (!ValidatePlanAgainstEnvelope(plan, selectedRcb.Reference, qualifiedReferences, out var planReason))
        {
            evidence.Add("G2.4 plan rejected: " + planReason);
            return Failed(
                "The strict one-URCB plan did not preserve the exact qualified member sequence. No RCB mutation was attempted.",
                evidence,
                identity,
                profile,
                loaded.FilePath,
                qualifiedReferences,
                selectedRcb.Reference,
                plan.DataSetReference);
        }

        evidence.Add($"G2.4 plan: rcb={plan.ReportControl!.Reference}; dataset={plan.DataSetReference}; members={plan.DynamicPoints.Count}; mode={plan.Mode}; status={plan.Status}");

        ArMms.MmsRcbAvailabilityResult freshAvailability;
        try
        {
            freshAvailability = await auxiliary.CheckReportControlAvailabilityAsync(
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
            evidence.Add($"G2.4 fresh RCB revalidation failed: {ex.GetType().Name}: {ex.Message}");
            return Failed(
                "Fresh URCB availability could not be re-read immediately before mutation. G2.4 stopped without claiming the RCB.",
                evidence,
                identity,
                profile,
                loaded.FilePath,
                qualifiedReferences,
                selectedRcb.Reference,
                plan.DataSetReference);
        }

        var freshRcb = freshAvailability.ReportControls.FirstOrDefault(snapshot => SameReference(snapshot.Reference, selectedRcb.Reference));
        if (!IsFreshUrcbSafeForG24(freshRcb, out var freshReason))
        {
            evidence.Add("G2.4 fresh URCB rejected: " + freshReason);
            return Failed(
                "The selected URCB was not still proven free at the final pre-mutation check. G2.4 stopped without claiming it.",
                evidence,
                identity,
                profile,
                loaded.FilePath,
                qualifiedReferences,
                selectedRcb.Reference,
                plan.DataSetReference);
        }

        ApplyFreshSnapshot(plan.ReportControl!, freshRcb!);
        evidence.Add($"G2.4 fresh URCB PASS: {freshRcb!.Reference}; RptEna={TextOrDash(freshRcb.EnabledState)}; Resv={TextOrDash(freshRcb.ReservationState)}; DatSet={TextOrDash(freshRcb.DataSetReference)}; RptID={TextOrDash(freshRcb.ReportId)}; TrgOps={TextOrDash(freshRcb.TriggerOptions)}; OptFlds={TextOrDash(freshRcb.OptionalFields)}");

        ArMms.MmsPersistentReportMonitorAttemptResult attempt;
        try
        {
            attempt = await auxiliary.StartPersistentReportMonitorWithAttemptEvidenceAsync(
                plan,
                triggerGeneralInterrogation: true,
                deleteDynamicDataSetOnStop: true,
                directory: discovery.IedDirectory,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
        {
            evidence.Add($"G2.4 activation exception: {ex.GetType().Name}: {ex.Message}");
            return Failed(
                "G2.4 activation threw before a persistent monitor session was returned. The profile was not advanced; inspect the IED from a fresh association before retrying.",
                evidence,
                identity,
                profile,
                loaded.FilePath,
                qualifiedReferences,
                selectedRcb.Reference,
                plan.DataSetReference);
        }

        AppendWriteSteps(evidence, "G2.4 activation", attempt.StartResult.WriteSteps);
        foreach (var warning in attempt.StartResult.Warnings)
            evidence.Add("G2.4 activation warning: " + warning);
        if (!attempt.IsSuccess || attempt.StartResult.Session is null)
        {
            AppendWriteSteps(evidence, "G2.4 failed-start cleanup", attempt.CleanupSteps);
            foreach (var warning in attempt.CleanupWarnings)
                evidence.Add("G2.4 cleanup warning: " + warning);
            evidence.Add($"G2.4 activation failed: reason={attempt.FailureReason}; dynamicAttempted={attempt.DynamicAttempted}; cleanupAttempted={attempt.CleanupAttempted}; cleanupSucceeded={attempt.CleanupSucceeded}; sessionState={auxiliary.State}; message={attempt.StartResult.Message}");

            return new DynamicReportActivationCommissioningResult
            {
                IsSuccess = false,
                CleanupSucceeded = attempt.CleanupSucceeded,
                Summary = attempt.CleanupSucceeded
                    ? "G2.4 one-URCB activation did not complete. Failed-start rollback was proven; the persisted profile remains EnvelopeQualified."
                    : "G2.4 activation failed and rollback was not fully proven. Do not retry on the same association; inspect the IED from a fresh commissioning association.",
                Identity = identity,
                InputProfile = profile,
                RcbReference = selectedRcb.Reference,
                DataSetReference = plan.DataSetReference,
                MemberReferences = qualifiedReferences,
                ProfilePath = loaded.FilePath,
                EvidenceLines = evidence
            };
        }

        var session = attempt.StartResult.Session;
        ArMms.MmsDynamicRcbActivationProof? activationProof = null;
        ArMms.MmsDynamicInformationReportProof? informationProof = null;
        var proofException = string.Empty;
        var cleanupSucceeded = false;
        var cleanupMessage = string.Empty;

        try
        {
            var readback = await auxiliary.GetDataSetDirectoryAsync(
                plan.DataSetReference,
                discovery.IedDirectory,
                cancellationToken).ConfigureAwait(false);
            var exactReadback = readback.IsSuccess && ExactSequenceEquals(
                qualifiedReferences,
                readback.Members.Select(member => member.MmsReference));
            evidence.Add($"G2.4 DataSet readback: success={readback.IsSuccess}; exact={exactReadback}; members={readback.Members.Count}; deletable={readback.IsDeletable?.ToString().ToLowerInvariant() ?? "unknown"}; result={readback.Message}");
            evidence.Add("G2.4 DataSet readback members: " + string.Join(" | ", readback.Members.Select(member => member.MmsReference)));

            var afterEnable = attempt.StartResult.RcbSnapshots.LastOrDefault(snapshot =>
                snapshot.Stage.Equals("after-enable", StringComparison.OrdinalIgnoreCase));
            var bindingAccepted = SuccessfulStep(attempt.StartResult.WriteSteps, "DatSet") &&
                                  afterEnable is not null &&
                                  afterEnable.IsSuccess &&
                                  SameReference(afterEnable.DataSetReference, plan.DataSetReference);
            var rptEnaAccepted = SuccessfulStep(attempt.StartResult.WriteSteps, "RptEna") &&
                                 afterEnable is not null &&
                                 afterEnable.IsSuccess &&
                                 ParseBool(afterEnable.EnabledState) == true;

            activationProof = new ArMms.MmsDynamicRcbActivationProof
            {
                EvidenceId = $"arsas-g2.4-activation-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}",
                ObservedAtUtc = DateTimeOffset.UtcNow,
                RcbReference = selectedRcb.Reference,
                DataSetReference = plan.DataSetReference,
                MemberReferences = qualifiedReferences,
                FreshRcbAvailabilityVerified = true,
                DataSetReadbackVerified = exactReadback,
                RcbDataSetBindingAccepted = bindingAccepted,
                RptEnaAccepted = rptEnaAccepted,
                AssociationHealthyAfterActivation = auxiliary.IsMmsInitiated
            };
            evidence.Add($"G2.4 activation proof: success={activationProof.IsSuccess}; freshRcb={activationProof.FreshRcbAvailabilityVerified}; datasetReadback={activationProof.DataSetReadbackVerified}; binding={activationProof.RcbDataSetBindingAccepted}; rptEna={activationProof.RptEnaAccepted}; associationHealthy={activationProof.AssociationHealthyAfterActivation}");

            if (activationProof.IsSuccess)
            {
                var receive = await auxiliary.ReceivePersistentReportMonitorSliceAsync(
                    session,
                    InformationReportProofWindow,
                    pollDirectory: null,
                    pollReferences: null,
                    pollInterval: null,
                    triggerGeneralInterrogation: true,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                AppendWriteSteps(evidence, "G2.4 receive", receive.WriteSteps);
                evidence.Add($"G2.4 receive: reports={receive.Reports.Count}; unrouted={auxiliary.UnroutedPersistentReportCount}; route={TextOrDash(auxiliary.LastReceiveRoutingSummary)}; result={receive.Message}");

                DynamicReportFrameValidation? acceptedValidation = null;
                ArMms.MmsReportFrame? acceptedFrame = null;
                foreach (var frame in receive.Reports)
                {
                    var validation = ValidateInformationReportFrame(
                        frame,
                        selectedRcb.ReportId,
                        plan.DataSetReference,
                        qualifiedReferences);
                    evidence.Add($"G2.4 report candidate: rptId={TextOrDash(frame.Header.ReportId)}; dataset={TextOrDash(frame.Header.DataSetReference)}; decoder={frame.DecoderMode}; values={frame.Values.Count}; included=[{string.Join(",", frame.IncludedDataSetIndexes)}]; valid={validation.IsSuccess}; reason={validation.Reason}");
                    if (validation.IsSuccess)
                    {
                        acceptedValidation = validation;
                        acceptedFrame = frame;
                        break;
                    }
                }

                if (acceptedValidation is not null && acceptedFrame is not null)
                {
                    informationProof = new ArMms.MmsDynamicInformationReportProof
                    {
                        EvidenceId = $"arsas-g2.4-report-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}",
                        ObservedAtUtc = acceptedFrame.ReceivedAt,
                        RcbReference = selectedRcb.Reference,
                        DataSetReference = plan.DataSetReference,
                        MemberReferences = qualifiedReferences,
                        Kind = acceptedValidation.Kind,
                        ActualInformationReportReceived = true,
                        ReportIdentityVerified = true,
                        ExactMemberMappingVerified = true,
                        AssociationHealthyAfterReport = auxiliary.IsMmsInitiated,
                        ReportAuthoritativePointCount = acceptedValidation.AuthoritativePointCount
                    };
                    evidence.Add($"G2.4 InformationReport proof: success={informationProof.IsSuccess}; kind={informationProof.Kind}; actual={informationProof.ActualInformationReportReceived}; identity={informationProof.ReportIdentityVerified}; exactMapping={informationProof.ExactMemberMappingVerified}; authoritativePoints={informationProof.ReportAuthoritativePointCount}; associationHealthy={informationProof.AssociationHealthyAfterReport}");
                }
                else
                {
                    evidence.Add("G2.4 InformationReport proof: success=false; no received frame satisfied strict RptID + DatSet + full exact ordered member mapping requirements. RptEna/GI acceptance is not treated as report proof.");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
        {
            proofException = $"{ex.GetType().Name}: {ex.Message}";
            evidence.Add("G2.4 proof exception: " + proofException);
        }
        finally
        {
            try
            {
                var stop = await auxiliary.StopPersistentReportMonitorAsync(session, CancellationToken.None).ConfigureAwait(false);
                cleanupSucceeded = stop.IsSuccess;
                cleanupMessage = stop.Message;
                AppendWriteSteps(evidence, "G2.4 cleanup", stop.WriteSteps);
                evidence.Add($"G2.4 cleanup: success={stop.IsSuccess}; sessionState={auxiliary.State}; result={stop.Message}");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
            {
                cleanupSucceeded = false;
                cleanupMessage = $"{ex.GetType().Name}: {ex.Message}";
                evidence.Add("G2.4 cleanup exception: " + cleanupMessage);
            }
        }

        if (!cleanupSucceeded)
        {
            return new DynamicReportActivationCommissioningResult
            {
                IsSuccess = false,
                CleanupSucceeded = false,
                Summary = "G2.4 active proof ended without proven cleanup. The persisted profile was deliberately NOT advanced; use a fresh association to inspect RptEna/DatSet/Resv before any retry.",
                Identity = identity,
                InputProfile = profile,
                ActivationProof = activationProof,
                InformationReportProof = informationProof,
                RcbReference = selectedRcb.Reference,
                DataSetReference = plan.DataSetReference,
                MemberReferences = qualifiedReferences,
                ProfilePath = loaded.FilePath,
                EvidenceLines = evidence
            };
        }

        if (activationProof?.IsSuccess != true || informationProof?.IsSuccess != true)
        {
            var why = !string.IsNullOrWhiteSpace(proofException)
                ? proofException
                : activationProof?.IsSuccess != true
                    ? "RCB activation evidence was incomplete."
                    : "No strict actual InformationReport proof was obtained.";
            evidence.Add("G2.4 profile unchanged after safe cleanup: " + why);
            return new DynamicReportActivationCommissioningResult
            {
                IsSuccess = false,
                CleanupSucceeded = true,
                Summary = "G2.4 cleanup passed, but actual strict InformationReport proof did not. The persisted profile remains EnvelopeQualified and production dynamic reporting remains OFF.",
                Identity = identity,
                InputProfile = profile,
                ActivationProof = activationProof,
                InformationReportProof = informationProof,
                RcbReference = selectedRcb.Reference,
                DataSetReference = plan.DataSetReference,
                MemberReferences = qualifiedReferences,
                ProfilePath = loaded.FilePath,
                EvidenceLines = evidence
            };
        }

        ArMms.MmsDynamicReportQualificationProfile activatedProfile;
        ArMms.MmsDynamicReportQualificationProfile finalProfile;
        try
        {
            activatedProfile = ArMms.MmsDynamicReportQualificationProfilePolicy.RecordRcbActivationProof(
                profile,
                identity,
                activationProof);
            finalProfile = ArMms.MmsDynamicReportQualificationProfilePolicy.RecordInformationReportProof(
                activatedProfile,
                identity,
                informationProof);
            await _profileStore.SaveAsync(finalProfile, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            evidence.Add($"G2.4 profile transition/save failed: {ex.GetType().Name}: {ex.Message}");
            return new DynamicReportActivationCommissioningResult
            {
                IsSuccess = false,
                CleanupSucceeded = true,
                Summary = "Physical G2.4 activation/report evidence passed and cleanup passed, but the identity-bound profile transition could not be persisted. Production dynamic reporting remains OFF.",
                Identity = identity,
                InputProfile = profile,
                ActivationProof = activationProof,
                InformationReportProof = informationProof,
                RcbReference = selectedRcb.Reference,
                DataSetReference = plan.DataSetReference,
                MemberReferences = qualifiedReferences,
                ProfilePath = loaded.FilePath,
                EvidenceLines = evidence
            };
        }

        evidence.Add($"G2.4 profile saved: state={finalProfile.State}; rcb={finalProfile.RcbActivationProof?.RcbReference}; dataset={finalProfile.RcbActivationProof?.DataSetReference}; members={finalProfile.RcbActivationProof?.MemberReferences.Count}; path={loaded.FilePath}");
        evidence.Add("G2.4 safety: InformationReportProven is NOT ProductionEligible. Production automatic dynamic reporting remains OFF until G2.5 scale-out and G2.6 regressions pass.");

        return new DynamicReportActivationCommissioningResult
        {
            IsSuccess = true,
            CleanupSucceeded = true,
            Summary = $"G2.4 PASS: one fresh URCB delivered an actual strictly mapped InformationReport for {qualifiedReferences.Length} qualified member(s), cleanup passed, and the identity-bound profile advanced to {finalProfile.State}. Production automatic dynamic reporting remains OFF.",
            Identity = identity,
            InputProfile = profile,
            SavedProfile = finalProfile,
            ActivationProof = activationProof,
            InformationReportProof = informationProof,
            RcbReference = selectedRcb.Reference,
            DataSetReference = plan.DataSetReference,
            MemberReferences = qualifiedReferences,
            ProfilePath = loaded.FilePath,
            EvidenceLines = evidence
        };
    }

    internal static bool TryResolveExactQualifiedMembers(
        ArMms.MmsIedModelDirectory directory,
        IReadOnlyList<string> qualifiedReferences,
        out IReadOnlyList<ArMms.MmsFcResolvedPoint> points,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(qualifiedReferences);
        var resolved = new List<ArMms.MmsFcResolvedPoint>();

        foreach (var reference in qualifiedReferences)
        {
            if (string.IsNullOrWhiteSpace(reference) || !directory.TryFindByMmsReference(reference, out var point))
            {
                points = Array.Empty<ArMms.MmsFcResolvedPoint>();
                reason = $"Exact live MMS reference not found: {reference}";
                return false;
            }
            if (point.IsControlAttribute || point.IsReportAttribute ||
                !(point.FunctionalConstraint.Equals("ST", StringComparison.OrdinalIgnoreCase) ||
                  point.FunctionalConstraint.Equals("MX", StringComparison.OrdinalIgnoreCase)))
            {
                points = Array.Empty<ArMms.MmsFcResolvedPoint>();
                reason = $"Qualified reference no longer resolves to a safe ST/MX process point: {reference}";
                return false;
            }
            if (!SameReference(point.MmsReference, reference))
            {
                points = Array.Empty<ArMms.MmsFcResolvedPoint>();
                reason = $"Live MMS normalization changed the qualified identity: expected={reference}, actual={point.MmsReference}";
                return false;
            }
            resolved.Add(point);
        }

        points = resolved;
        reason = $"Exact ordered live mapping preserved for {resolved.Count} qualified member(s).";
        return resolved.Count == qualifiedReferences.Count;
    }

    internal static ArMms.MmsReportControlCandidate? SelectQualifiedUrcb(
        ArMms.MmsReportInventory inventory,
        string preferredLogicalDevice,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var urcbOnly = new ArMms.MmsReportInventory();
        foreach (var rcb in inventory.ReportControls.Where(candidate => !candidate.Buffered))
            urcbOnly.ReportControls.Add(rcb);

        var selection = ArMms.MmsRcbPoolSelector.BuildDynamicSelection(
            urcbOnly,
            preferredLogicalDevice,
            preferredRcbReference: null,
            strictRcb: false,
            allowUrCbFallback: true,
            allowPollingFallback: false);

        var eligible = selection.Candidates
            .Where(candidate => candidate.Availability == ArMms.MmsRcbAvailabilityKind.AvailableDynamicEmpty)
            .Where(candidate => !candidate.IsBuffered)
            .Select(candidate => new
            {
                Evaluation = candidate,
                Rcb = urcbOnly.ReportControls.FirstOrDefault(rcb => SameReference(rcb.Reference, candidate.Reference))
            })
            .Where(item => item.Rcb is not null && HasStrictReportIdentityFields(item.Rcb))
            .OrderByDescending(item => item.Evaluation.IsSameLogicalDevice)
            .ThenByDescending(item => item.Evaluation.Score)
            .ThenBy(item => item.Evaluation.Reference, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (eligible?.Rcb is null)
        {
            var dynamicEmpty = selection.Candidates.Count(candidate =>
                candidate.Availability == ArMms.MmsRcbAvailabilityKind.AvailableDynamicEmpty && !candidate.IsBuffered);
            reason = $"URCB total={urcbOnly.ReportControls.Count}; live dynamic-empty={dynamicEmpty}; strictProofEligible=0. Strict proof also requires non-empty RptID, GI in current TrgOps and data-set-name in current OptFlds.";
            return null;
        }

        reason = $"selected={eligible.Rcb.Reference}; score={eligible.Evaluation.Score}; sameLD={eligible.Evaluation.IsSameLogicalDevice}; RptID={TextOrDash(eligible.Rcb.ReportId)}; TrgOps={TextOrDash(eligible.Rcb.TriggerOptions)}; OptFlds={TextOrDash(eligible.Rcb.OptionalFields)}";
        return eligible.Rcb;
    }

    internal static bool IsFreshUrcbSafeForG24(ArMms.MmsRcbAvailabilitySnapshot? snapshot, out string reason)
    {
        if (snapshot is null)
        {
            reason = "Selected URCB was missing from the fresh availability read.";
            return false;
        }
        if (snapshot.Buffered)
        {
            reason = "G2.4 first proof permits URCB only; the fresh candidate is buffered.";
            return false;
        }
        if (snapshot.DataSetProbeState != ArMms.MmsRcbDataSetProbeState.ReadSucceeded ||
            !string.IsNullOrWhiteSpace(snapshot.DataSetReference))
        {
            reason = "Fresh live DatSet must be positively read and empty before G2.4 mutation.";
            return false;
        }
        if (ParseBool(snapshot.EnabledState) != false)
        {
            reason = $"Fresh RptEna is not explicit false: {TextOrDash(snapshot.EnabledState)}";
            return false;
        }
        if (snapshot.Attributes.Contains("Resv", StringComparer.OrdinalIgnoreCase) &&
            ParseBool(snapshot.ReservationState) != false)
        {
            reason = $"Fresh URCB Resv is not explicit false: {TextOrDash(snapshot.ReservationState)}";
            return false;
        }
        if (ParseUnsigned(snapshot.ReservationTimeSeconds) is > 0)
        {
            reason = $"Fresh reservation time is positive: {snapshot.ReservationTimeSeconds}";
            return false;
        }
        if (HasOwner(snapshot.Owner))
        {
            reason = $"Fresh URCB Owner is non-empty: {snapshot.Owner}";
            return false;
        }
        if (!HasStrictReportIdentityFields(snapshot))
        {
            reason = "Fresh URCB no longer has strict G2.4 report identity fields (RptID + GI TrgOps + data-set-name OptFlds).";
            return false;
        }

        reason = "Fresh live DatSet is empty, RptEna=false, reservation/Owner is free, and strict report identity fields are present.";
        return true;
    }

    internal static bool ValidatePlanAgainstEnvelope(
        ArMms.MmsReportSubscriptionPlan plan,
        string expectedRcbReference,
        IReadOnlyList<string> qualifiedReferences,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(qualifiedReferences);
        if (!plan.IsReady || plan.ReportControl is null || plan.Mode != ArMms.MmsReportSubscriptionPlanMode.DynamicDataSet)
        {
            reason = "Plan is not a ready DynamicDataSet plan.";
            return false;
        }
        if (plan.ReportControl.Buffered || !SameReference(plan.ReportControl.Reference, expectedRcbReference))
        {
            reason = $"Plan did not preserve the exact selected URCB. expected={expectedRcbReference}, actual={plan.ReportControl.Reference}, buffered={plan.ReportControl.Buffered}";
            return false;
        }
        var planned = plan.DynamicPoints.Select(point => point.MmsReference).ToArray();
        if (!ExactSequenceEquals(qualifiedReferences, planned))
        {
            reason = "Plan member sequence differs from the exact persisted qualified envelope.";
            return false;
        }
        if (planned.Length == 0 || planned.Length > MaximumG24Members)
        {
            reason = $"Plan member count {planned.Length} is outside G2.4 commissioning bound 1..{MaximumG24Members}.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(plan.DataSetReference))
        {
            reason = "Plan did not produce a temporary DataSet reference.";
            return false;
        }

        reason = "Strict one-URCB plan preserves the exact ordered qualified envelope.";
        return true;
    }

    internal static DynamicReportFrameValidation ValidateInformationReportFrame(
        ArMms.MmsReportFrame frame,
        string expectedReportId,
        string expectedDataSetReference,
        IReadOnlyList<string> qualifiedReferences)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(qualifiedReferences);

        if (frame.DecoderMode.Equals("rejected-unmapped", StringComparison.OrdinalIgnoreCase))
            return InvalidFrame("Report decoder quarantined the frame as unmapped.");
        if (string.IsNullOrWhiteSpace(expectedReportId) ||
            !frame.Header.ReportId.Trim().Equals(expectedReportId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return InvalidFrame($"RptID mismatch. expected={TextOrDash(expectedReportId)}, actual={TextOrDash(frame.Header.ReportId)}");
        }
        if (string.IsNullOrWhiteSpace(frame.Header.DataSetReference) ||
            !SameReference(frame.Header.DataSetReference, expectedDataSetReference))
        {
            return InvalidFrame($"DatSet identity mismatch. expected={expectedDataSetReference}, actual={TextOrDash(frame.Header.DataSetReference)}");
        }
        if (qualifiedReferences.Count == 0 || frame.Values.Count != qualifiedReferences.Count)
        {
            return InvalidFrame($"Full exact member proof requires {qualifiedReferences.Count} values, received {frame.Values.Count}.");
        }

        var expectedIndexes = Enumerable.Range(0, qualifiedReferences.Count).ToArray();
        if (!frame.IncludedDataSetIndexes.SequenceEqual(expectedIndexes))
        {
            return InvalidFrame($"Inclusion sequence is not the complete ordered DataSet. expected=[{string.Join(",", expectedIndexes)}], actual=[{string.Join(",", frame.IncludedDataSetIndexes)}]");
        }

        for (var index = 0; index < qualifiedReferences.Count; index++)
        {
            var value = frame.Values[index];
            if (value.Index != index)
                return InvalidFrame($"Mapped value index mismatch at offset {index}: DataSet index={value.Index}.");
            if (value.Member is null || !SameReference(value.Member.MmsReference, qualifiedReferences[index]))
            {
                return InvalidFrame($"Mapped member mismatch at offset {index}: expected={qualifiedReferences[index]}, actual={value.Member?.MmsReference ?? "<null>"}.");
            }
            if (value.Value is null || value.FailureCode.HasValue)
                return InvalidFrame($"Mapped member {qualifiedReferences[index]} has no successful process value (failure={value.FailureCode?.ToString() ?? "none"}).");
            if (!string.IsNullOrWhiteSpace(value.DataReference) &&
                !SameReference(value.DataReference, qualifiedReferences[index]) &&
                !SameReference(value.DataReference, value.Member.UserReference))
            {
                return InvalidFrame($"DataRef mismatch at offset {index}: expected={qualifiedReferences[index]}, actual={value.DataReference}.");
            }
        }

        var reasons = frame.Values
            .SelectMany(value => value.ReasonForInclusion)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var kind = reasons.Contains("general-interrogation", StringComparer.OrdinalIgnoreCase)
            ? ArMms.MmsDynamicInformationReportKind.GeneralInterrogation
            : reasons.Contains("integrity", StringComparer.OrdinalIgnoreCase)
                ? ArMms.MmsDynamicInformationReportKind.Integrity
                : reasons.Any(reason => reason.Equals("data-change", StringComparison.OrdinalIgnoreCase) ||
                                        reason.Equals("quality-change", StringComparison.OrdinalIgnoreCase) ||
                                        reason.Equals("data-update", StringComparison.OrdinalIgnoreCase))
                    ? ArMms.MmsDynamicInformationReportKind.DataChange
                    : ArMms.MmsDynamicInformationReportKind.OtherVerified;

        return new DynamicReportFrameValidation
        {
            IsSuccess = true,
            Kind = kind,
            AuthoritativePointCount = qualifiedReferences.Count,
            Reason = $"Actual InformationReport identity and full ordered mapping verified for {qualifiedReferences.Count} member(s); reasons={TextOrDash(string.Join(",", reasons))}."
        };
    }

    private static bool HasStrictReportIdentityFields(ArMms.MmsReportControlCandidate rcb)
    {
        if (string.IsNullOrWhiteSpace(rcb.ReportId))
            return false;
        var triggers = ArMms.MmsReportControlFieldCodec.DecodeTriggerOptions(rcb.TriggerOptions);
        var fields = ArMms.MmsReportControlFieldCodec.DecodeOptionalFields(rcb.OptionalFields);
        return triggers.GeneralInterrogation && fields.DataSetName;
    }

    private static bool HasStrictReportIdentityFields(ArMms.MmsRcbAvailabilitySnapshot rcb)
    {
        if (string.IsNullOrWhiteSpace(rcb.ReportId))
            return false;
        var triggers = ArMms.MmsReportControlFieldCodec.DecodeTriggerOptions(rcb.TriggerOptions);
        var fields = ArMms.MmsReportControlFieldCodec.DecodeOptionalFields(rcb.OptionalFields);
        return triggers.GeneralInterrogation && fields.DataSetName;
    }

    private static void ApplyFreshSnapshot(ArMms.MmsReportControlCandidate target, ArMms.MmsRcbAvailabilitySnapshot source)
    {
        target.DataSetReference = source.DataSetReference;
        target.DataSetProbeState = source.DataSetProbeState;
        target.DataSetProbeMessage = source.DataSetProbeMessage;
        target.ReportId = source.ReportId;
        target.ConfRev = source.ConfRev;
        target.BufferTimeMs = source.BufferTimeMs;
        target.IntegrityPeriodMs = source.IntegrityPeriodMs;
        target.TriggerOptions = source.TriggerOptions;
        target.OptionalFields = source.OptionalFields;
        target.EnabledState = source.EnabledState;
        target.ReservationState = source.ReservationState;
        target.ReservationTimeSeconds = source.ReservationTimeSeconds;
        target.Owner = source.Owner;
        target.Attributes = source.Attributes.ToList();
    }

    private static bool SuccessfulStep(IEnumerable<ArMms.MmsReportAttributeWriteStep> steps, string attribute)
        => steps.Any(step => step.Attempted && step.IsSuccess && step.Attribute.Equals(attribute, StringComparison.OrdinalIgnoreCase));

    private static DynamicReportFrameValidation InvalidFrame(string reason)
        => new() { IsSuccess = false, Reason = reason };

    private static bool ExactSequenceEquals(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        var left = expected.ToArray();
        var right = actual.ToArray();
        if (left.Length != right.Length)
            return false;
        for (var index = 0; index < left.Length; index++)
        {
            if (!SameReference(left[index], right[index]))
                return false;
        }
        return true;
    }

    private static bool SameReference(string? left, string? right)
        => NormalizeReference(left).Equals(NormalizeReference(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');

    private static bool? ParseBool(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text == "-")
            return null;
        if (bool.TryParse(text, out var parsed))
            return parsed;
        if (text is "1" or "01" || text.Equals("yes", StringComparison.OrdinalIgnoreCase) || text.Equals("on", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text is "0" or "00" || text.Equals("no", StringComparison.OrdinalIgnoreCase) || text.Equals("off", StringComparison.OrdinalIgnoreCase))
            return false;
        return null;
    }

    private static ulong? ParseUnsigned(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return ulong.TryParse(text, out var parsed) ? parsed : null;
    }

    private static bool HasOwner(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text == "-" || text == "[]" || text.Equals("null", StringComparison.OrdinalIgnoreCase))
            return false;
        var compact = text.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return compact.Length > 0 && compact.Any(character => character != '0');
    }

    private static void AppendWriteSteps(
        ICollection<string> evidence,
        string label,
        IEnumerable<ArMms.MmsReportAttributeWriteStep> steps)
    {
        foreach (var step in steps)
            evidence.Add($"{label} write: attribute={step.Attribute}; reference={step.Reference}; attempted={step.Attempted}; success={step.IsSuccess}; result={step.Message}");
    }

    private static DynamicReportActivationCommissioningResult Blocked(
        string summary,
        IReadOnlyList<string> evidence,
        ArMms.MmsDynamicReportIedIdentity? identity = null,
        string profilePath = "",
        ArMms.MmsDynamicReportQualificationProfile? profile = null)
        => new()
        {
            IsBlocked = true,
            Summary = summary,
            Identity = identity,
            InputProfile = profile,
            ProfilePath = profilePath,
            EvidenceLines = evidence.ToArray()
        };

    private static DynamicReportActivationCommissioningResult Failed(
        string summary,
        IReadOnlyList<string> evidence,
        ArMms.MmsDynamicReportIedIdentity identity,
        ArMms.MmsDynamicReportQualificationProfile profile,
        string profilePath,
        IReadOnlyList<string>? memberReferences = null,
        string rcbReference = "",
        string dataSetReference = "")
        => new()
        {
            IsSuccess = false,
            Summary = summary,
            Identity = identity,
            InputProfile = profile,
            MemberReferences = memberReferences?.ToArray() ?? Array.Empty<string>(),
            RcbReference = rcbReference,
            DataSetReference = dataSetReference,
            ProfilePath = profilePath,
            EvidenceLines = evidence.ToArray()
        };

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
