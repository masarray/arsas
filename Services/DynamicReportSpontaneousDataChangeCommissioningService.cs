using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

internal sealed class DynamicReportSpontaneousDataChangeValidation
{
    public bool IsSuccess { get; init; }
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<int> IncludedIndexes { get; init; } = Array.Empty<int>();
    public IReadOnlyList<string> IncludedMemberReferences { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
}

internal sealed class DynamicReportSpontaneousDataChangeCommissioningResult
{
    public bool IsSuccess { get; init; }
    public bool IsBlocked { get; init; }
    public bool ActivationProven { get; init; }
    public bool SpontaneousDataChangeProven { get; init; }
    public bool MonitorCleanupSucceeded { get; init; }
    public bool ProofFieldRestoreSucceeded { get; init; }
    public bool FreshCleanupClosureSucceeded { get; init; }
    public bool AssociationHealthyAfterReport { get; init; }
    public string Summary { get; init; } = string.Empty;
    public ArMms.MmsDynamicReportIedIdentity? Identity { get; init; }
    public ArMms.MmsDynamicReportQualificationProfile? InputProfile { get; init; }
    public string RcbReference { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public string ReportId { get; init; } = string.Empty;
    public IReadOnlyList<string> MemberReferences { get; init; } = Array.Empty<string>();
    public IReadOnlyList<int> IncludedIndexes { get; init; } = Array.Empty<int>();
    public IReadOnlyList<string> IncludedMemberReferences { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    public string ProfilePath { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();
}

/// <summary>
/// G2.5-A explicit commissioning gate for one spontaneous dchg InformationReport.
///
/// This gate consumes only an identity-compatible InformationReportProven profile,
/// reuses the exact G2.4-proven URCB and exact eight-member set, enables dchg ONLY,
/// never requests GI, and waits for a real spontaneous process/status change. It does
/// not change the persisted profile or production monitoring policy. PASS also requires
/// monitor cleanup, exact proof-field restore, and a second fresh-association read-only
/// cleanup closure.
/// </summary>
internal sealed class DynamicReportSpontaneousDataChangeCommissioningService
{
    private static readonly TimeSpan AuxiliaryAssociationTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan SpontaneousProofWindow = TimeSpan.FromSeconds(60);
    internal const string TemporaryTriggerOptions = "dchg";
    internal const string TemporaryOptionalFields = "reason-for-inclusion data-set-name";
    internal const string ExpectedCanonicalTriggerRaw = "0240";
    internal const string ExpectedCanonicalOptionalFieldsRaw = "061800";

    private readonly DynamicReportQualificationProfileStore _profileStore;

    public DynamicReportSpontaneousDataChangeCommissioningService(
        DynamicReportQualificationProfileStore? profileStore = null)
    {
        _profileStore = profileStore ?? new DynamicReportQualificationProfileStore();
    }

    public async Task<DynamicReportSpontaneousDataChangeCommissioningResult> RunAsync(
        Iec61850MonitorDevice device,
        IReadOnlyList<SignalDefinition> fullModelSignals,
        IProgress<string>? progress = null,
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
            return Blocked("G2.5-A identity preflight failed: " + ex.Message, evidence);
        }

        evidence.Add($"G2.5-A identity stableKey={identity.StableIdentityKey}; fingerprint={identity.ModelFingerprint}; profileRevision={TextOrDash(identity.ProfileRevision)}");

        var loaded = await _profileStore.LoadAsync(identity, cancellationToken).ConfigureAwait(false);
        evidence.Add($"G2.5-A persisted profile: exists={loaded.Exists}; valid={loaded.IsValid}; reason={loaded.Reason}");
        if (!loaded.IsValid || loaded.Profile is null)
            return Blocked("G2.5-A requires the identity-compatible InformationReportProven profile from merged G2.4.", evidence, identity, loaded.FilePath);

        var profile = loaded.Profile;
        if (profile.State != ArMms.MmsDynamicReportQualificationState.InformationReportProven ||
            profile.AcceptedEnvelope is null ||
            profile.RcbActivationProof?.IsSuccess != true ||
            profile.InformationReportProof?.IsSuccess != true)
        {
            return Blocked(
                $"G2.5-A requires a complete InformationReportProven profile; current state is {profile.State}.",
                evidence,
                identity,
                loaded.FilePath,
                profile);
        }

        var rcbReference = profile.RcbActivationProof.RcbReference;
        var qualifiedReferences = profile.RcbActivationProof.MemberReferences.ToArray();
        if (string.IsNullOrWhiteSpace(rcbReference) ||
            qualifiedReferences.Length == 0 ||
            qualifiedReferences.Length > DynamicReportActivationCommissioningService.MaximumG24Members)
        {
            return Blocked("G2.5-A profile does not retain a usable exact one-URCB/eight-member G2.4 target.", evidence, identity, loaded.FilePath, profile);
        }

        evidence.Add($"G2.5-A profile gate: state={profile.State}; rcb={rcbReference}; members={qualifiedReferences.Length}; temporaryTrgOps={TemporaryTriggerOptions}; expectedTrgOpsRaw={ExpectedCanonicalTriggerRaw}; temporaryOptFlds={TemporaryOptionalFields}; expectedOptFldsRaw={ExpectedCanonicalOptionalFieldsRaw}");
        evidence.Add("G2.5-A exact members: " + string.Join(" | ", qualifiedReferences));
        evidence.Add("G2.5-A trigger contract: dchg ONLY. GI=false, integrity=false, qchg=false, dupd=false. No GI request is sent at monitor start or receive time.");
        evidence.Add("G2.5-A profile contract: the persisted InformationReportProven profile is READ ONLY and will not be saved, downgraded, or advanced by this gate.");

        var auxiliary = new ArMms.MmsClientSession();
        ArMms.MmsDynamicRcbCommissioningFieldLease? fieldLease = null;
        ArMms.MmsPersistentReportMonitorSession? monitorSession = null;
        ArMms.MmsReportSubscriptionPlan? plan = null;
        ArMms.MmsReportControlCandidate? selectedRcb = null;
        var activationProven = false;
        var spontaneousProven = false;
        var associationHealthyAfterReport = false;
        var monitorCleanup = false;
        var fieldRestore = false;
        var includedIndexes = Array.Empty<int>();
        var includedMembers = Array.Empty<string>();
        var includedReasons = Array.Empty<string>();
        var reportId = string.Empty;
        var activationAttempted = false;

        try
        {
            progress?.Report("G2.5-A: opening isolated MMS association and revalidating the exact G2.4-proven target…");
            await auxiliary.ConnectAsync(device.IpAddress, device.Port, AuxiliaryAssociationTimeout, cancellationToken).ConfigureAwait(false);
            evidence.Add($"G2.5-A auxiliary association ready: state={auxiliary.State}; localTcpAddress={TextOrDash(auxiliary.LocalTcpAddress)}; handshake={TextOrDash(auxiliary.LastHandshakeMessage)}");

            var discovery = await auxiliary.DiscoverAsync(
                probeReportAttributes: true,
                maxReportAttributeProbes: 64,
                cancellationToken: cancellationToken,
                readDataSetDirectories: false,
                maxDataSetDirectoryReads: 0).ConfigureAwait(false);
            evidence.Add("G2.5-A discovery: " + discovery.Summary);

            if (!DynamicReportActivationCommissioningService.TryResolveExactQualifiedMembers(
                    discovery.IedDirectory,
                    qualifiedReferences,
                    out var exactPoints,
                    out var exactReason))
            {
                evidence.Add("G2.5-A exact member revalidation failed: " + exactReason);
                return Failed("The exact G2.4-proven member set no longer maps to the live model. No RCB mutation was attempted.", evidence, identity, profile, loaded.FilePath, rcbReference, qualifiedReferences);
            }

            foreach (var point in exactPoints)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await auxiliary.ReadSingleVariableAsync(point.ToObjectReference(), cancellationToken).ConfigureAwait(false);
                evidence.Add($"G2.5-A direct-read {point.MmsReference}: success={read.IsSuccess}; result={read.Message}");
                if (!read.IsSuccess || !auxiliary.IsMmsInitiated)
                    return Failed("An exact G2.4-proven member failed fresh direct MMS validation. No RCB mutation was attempted.", evidence, identity, profile, loaded.FilePath, rcbReference, qualifiedReferences);
            }

            selectedRcb = discovery.ReportInventory.ReportControls.FirstOrDefault(candidate => SameReference(candidate.Reference, rcbReference));
            if (selectedRcb is null || selectedRcb.Buffered)
            {
                evidence.Add("G2.5-A exact URCB lookup failed or resolved to a buffered RCB.");
                return Failed("The exact G2.4-proven URCB is not available in fresh discovery. No mutation was attempted.", evidence, identity, profile, loaded.FilePath, rcbReference, qualifiedReferences);
            }

            var oneRcb = new ArMms.MmsReportInventory();
            oneRcb.ReportControls.Add(selectedRcb);
            var preLeaseAvailability = await auxiliary.CheckReportControlAvailabilityAsync(
                oneRcb,
                discovery.IedDirectory,
                new ArMms.MmsRcbAvailabilityOptions { MaxReportControls = 1, ReadDataSetDirectories = false },
                cancellationToken).ConfigureAwait(false);
            var preLeaseSnapshot = preLeaseAvailability.ReportControls.SingleOrDefault();
            evidence.Add("G2.5-A pre-lease availability: " + preLeaseAvailability.Summary);
            if (preLeaseSnapshot is not null)
                evidence.Add($"G2.5-A pre-lease URCB: availability={preLeaseSnapshot.Availability}; probe={preLeaseSnapshot.DataSetProbeState}; DatSet={TextOrDash(preLeaseSnapshot.DataSetReference)}; RptEna={TextOrDash(preLeaseSnapshot.EnabledState)}; Resv={TextOrDash(preLeaseSnapshot.ReservationState)}; Owner={TextOrDash(preLeaseSnapshot.Owner)}; RptID={TextOrDash(preLeaseSnapshot.ReportId)}; TrgOps={TextOrDash(preLeaseSnapshot.TriggerOptions)}; OptFlds={TextOrDash(preLeaseSnapshot.OptionalFields)}");

            if (preLeaseSnapshot is null ||
                !DynamicReportActivationCommissioningServiceV2.IsLeaseableFreeUrcbForG24(preLeaseSnapshot, out var preLeaseReason))
            {
                evidence.Add("G2.5-A pre-lease URCB rejected: " + (preLeaseSnapshot is null ? "snapshot missing" : preLeaseReason));
                return Failed("The exact G2.4-proven URCB is not freshly proven free. No proof-field mutation was attempted.", evidence, identity, profile, loaded.FilePath, rcbReference, qualifiedReferences);
            }

            ApplyFreshSnapshot(selectedRcb, preLeaseSnapshot);

            var fieldPrepare = await auxiliary.PrepareDynamicRcbCommissioningFieldsAsync(
                selectedRcb,
                TemporaryTriggerOptions,
                TemporaryOptionalFields,
                cancellationToken).ConfigureAwait(false);
            AppendWriteSteps(evidence, "G2.5-A proof-field prepare", fieldPrepare.WriteSteps);
            foreach (var line in fieldPrepare.Evidence)
                evidence.Add("G2.5-A proof-field prepare: " + line);
            evidence.Add($"G2.5-A proof-field prepare result: success={fieldPrepare.IsSuccess}; rollback={fieldPrepare.CleanupSucceeded}; result={fieldPrepare.Message}");

            if (!fieldPrepare.IsSuccess || fieldPrepare.Lease is null)
            {
                return new DynamicReportSpontaneousDataChangeCommissioningResult
                {
                    IsSuccess = false,
                    Summary = fieldPrepare.CleanupSucceeded
                        ? "G2.5-A dchg-only proof-field preparation failed, but exact rollback passed. The InformationReportProven profile is unchanged."
                        : "G2.5-A proof-field preparation failed and rollback was not fully proven. Inspect the URCB from a fresh association before retry.",
                    Identity = identity,
                    InputProfile = profile,
                    RcbReference = rcbReference,
                    MemberReferences = qualifiedReferences,
                    ProofFieldRestoreSucceeded = fieldPrepare.CleanupSucceeded,
                    ProfilePath = loaded.FilePath,
                    EvidenceLines = evidence
                };
            }

            fieldLease = fieldPrepare.Lease;
            evidence.Add($"G2.5-A proof-field lease ACTIVE: originalTrgOps={fieldLease.OriginalTriggerOptionsText}; originalOptFlds={fieldLease.OriginalOptionalFieldsText}; temporaryTrgOps=dchg-only/{ExpectedCanonicalTriggerRaw}; temporaryOptFlds=reason+dataset/{ExpectedCanonicalOptionalFieldsRaw}; GI=false");

            var dataSetName = "AR_G25A_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            plan = ArMms.MmsReportSubscriptionPlanner.BuildDynamicPlan(
                discovery.ReportInventory,
                discovery.IedDirectory,
                exactPoints.Select(point => point.UserReference),
                preferredLogicalDevice: selectedRcb.Domain,
                preferredRcbReference: selectedRcb.Reference,
                dataSetName: dataSetName,
                strictRcb: true,
                allowUrCbFallback: true,
                allowPollingFallback: false);

            if (!DynamicReportActivationCommissioningService.ValidatePlanAgainstEnvelope(plan, selectedRcb.Reference, qualifiedReferences, out var planReason))
            {
                evidence.Add("G2.5-A plan rejected: " + planReason);
                return Failed("The G2.5-A strict plan did not preserve the exact G2.4-proven one-URCB/member identity.", evidence, identity, profile, loaded.FilePath, rcbReference, qualifiedReferences, plan.DataSetReference);
            }
            evidence.Add($"G2.5-A plan: rcb={plan.ReportControl!.Reference}; dataset={plan.DataSetReference}; members={plan.DynamicPoints.Count}; mode={plan.Mode}; GI=false");

            var finalAvailability = await auxiliary.CheckReportControlAvailabilityAsync(
                oneRcb,
                discovery.IedDirectory,
                DynamicReportActivationCommissioningServiceV2.BuildPostLeaseAvailabilityOptions(selectedRcb.Reference),
                cancellationToken).ConfigureAwait(false);
            var postLeaseSnapshot = finalAvailability.ReportControls.SingleOrDefault();
            evidence.Add($"G2.5-A post-lease ownership: availability={postLeaseSnapshot?.Availability}; Resv={TextOrDash(postLeaseSnapshot?.ReservationState)}; Owner={TextOrDash(postLeaseSnapshot?.Owner)}; localTcpAddress={TextOrDash(auxiliary.LocalTcpAddress)}; TrgOps={TextOrDash(postLeaseSnapshot?.TriggerOptions)}; OptFlds={TextOrDash(postLeaseSnapshot?.OptionalFields)}");
            if (!IsPostLeaseUrcbSafeForDchg(postLeaseSnapshot, auxiliary.LocalTcpAddress, out var postLeaseReason))
            {
                evidence.Add("G2.5-A post-lease URCB rejected: " + postLeaseReason);
                return Failed("The exact URCB did not retain strict dchg-only caller-owned state after the proof-field lease.", evidence, identity, profile, loaded.FilePath, rcbReference, qualifiedReferences, plan.DataSetReference);
            }

            ApplyFreshSnapshot(plan.ReportControl!, postLeaseSnapshot!);
            EnsureAttribute(plan.ReportControl!, "TrgOps");
            EnsureAttribute(plan.ReportControl!, "OptFlds");
            plan.ReportControl!.TriggerOptions = TemporaryTriggerOptions;
            plan.ReportControl.OptionalFields = TemporaryOptionalFields;
            selectedRcb.TriggerOptions = TemporaryTriggerOptions;
            selectedRcb.OptionalFields = TemporaryOptionalFields;
            reportId = postLeaseSnapshot!.ReportId;

            activationAttempted = true;
            var attempt = await auxiliary.StartPersistentReportMonitorWithAttemptEvidenceAsync(
                plan,
                triggerGeneralInterrogation: false,
                deleteDynamicDataSetOnStop: true,
                directory: discovery.IedDirectory,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            AppendWriteSteps(evidence, "G2.5-A activation", attempt.StartResult.WriteSteps);
            foreach (var warning in attempt.StartResult.Warnings)
                evidence.Add("G2.5-A activation warning: " + warning);

            if (!attempt.IsSuccess || attempt.StartResult.Session is null)
            {
                AppendWriteSteps(evidence, "G2.5-A failed-start cleanup", attempt.CleanupSteps);
                foreach (var warning in attempt.CleanupWarnings)
                    evidence.Add("G2.5-A failed-start cleanup warning: " + warning);
                monitorCleanup = attempt.CleanupSucceeded;
                evidence.Add($"G2.5-A activation failed: cleanup={attempt.CleanupSucceeded}; reason={attempt.FailureReason}; result={attempt.StartResult.Message}");
                return Failed("G2.5-A could not arm the dchg-only monitor. Existing failed-start cleanup evidence is retained; the profile is unchanged.", evidence, identity, profile, loaded.FilePath, rcbReference, qualifiedReferences, plan.DataSetReference, monitorCleanup);
            }

            monitorSession = attempt.StartResult.Session;
            var readback = await auxiliary.GetDataSetDirectoryAsync(plan.DataSetReference, discovery.IedDirectory, cancellationToken).ConfigureAwait(false);
            var exactReadback = readback.IsSuccess && ExactSequenceEquals(qualifiedReferences, readback.Members.Select(member => member.MmsReference));
            evidence.Add($"G2.5-A DataSet readback: success={readback.IsSuccess}; exact={exactReadback}; members={readback.Members.Count}; result={readback.Message}");
            evidence.Add("G2.5-A DataSet readback members: " + string.Join(" | ", readback.Members.Select(member => member.MmsReference)));

            var afterEnable = attempt.StartResult.RcbSnapshots.LastOrDefault(snapshot => snapshot.Stage.Equals("after-enable", StringComparison.OrdinalIgnoreCase));
            var bindingAccepted = SuccessfulStep(attempt.StartResult.WriteSteps, "DatSet") &&
                                  afterEnable is not null && afterEnable.IsSuccess &&
                                  SameReference(afterEnable.DataSetReference, plan.DataSetReference);
            var rptEnaAccepted = SuccessfulStep(attempt.StartResult.WriteSteps, "RptEna") &&
                                 afterEnable is not null && afterEnable.IsSuccess &&
                                 ParseBool(afterEnable.EnabledState) == true;
            activationProven = exactReadback && bindingAccepted && rptEnaAccepted && auxiliary.IsMmsInitiated;
            evidence.Add($"G2.5-A activation proof: success={activationProven}; datasetReadback={exactReadback}; binding={bindingAccepted}; RptEna={rptEnaAccepted}; associationHealthy={auxiliary.IsMmsInitiated}; GIrequested=false");

            if (activationProven)
            {
                progress?.Report($"G2.5-A ARMED — NO GI. Within {SpontaneousProofWindow.TotalSeconds:0}s, cause ONE normal physical/status change affecting one of the 8 proven points. Do not edit any RCB/DataSet manually.");
                evidence.Add($"G2.5-A ARMED: monitor is enabled and routed; GI=false. Waiting up to {SpontaneousProofWindow.TotalSeconds:0}s for a spontaneous data-change report caused by a normal field/process change.");

                var receive = await auxiliary.ReceivePersistentReportMonitorSliceAsync(
                    monitorSession,
                    SpontaneousProofWindow,
                    pollDirectory: null,
                    pollReferences: null,
                    pollInterval: null,
                    triggerGeneralInterrogation: false,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                AppendWriteSteps(evidence, "G2.5-A receive", receive.WriteSteps);
                evidence.Add($"G2.5-A receive: reports={receive.Reports.Count}; unrouted={auxiliary.UnroutedPersistentReportCount}; route={TextOrDash(auxiliary.LastReceiveRoutingSummary)}; GIrequested=false; result={receive.Message}");

                foreach (var frame in receive.Reports)
                {
                    var validation = ValidateSpontaneousDataChangeFrame(frame, reportId, plan.DataSetReference, qualifiedReferences);
                    evidence.Add($"G2.5-A report candidate: rptId={TextOrDash(frame.Header.ReportId)}; dataset={TextOrDash(frame.Header.DataSetReference)}; decoder={frame.DecoderMode}; values={frame.Values.Count}; included=[{string.Join(",", frame.IncludedDataSetIndexes)}]; valid={validation.IsSuccess}; reasons=[{string.Join(",", validation.Reasons)}]; reason={validation.Reason}");
                    if (!validation.IsSuccess)
                        continue;

                    spontaneousProven = true;
                    associationHealthyAfterReport = auxiliary.IsMmsInitiated;
                    includedIndexes = validation.IncludedIndexes.ToArray();
                    includedMembers = validation.IncludedMemberReferences.ToArray();
                    includedReasons = validation.Reasons.ToArray();
                    evidence.Add($"G2.5-A spontaneous dchg proof: success={spontaneousProven && associationHealthyAfterReport}; kind=DataChange; actual=true; identity=true; mappedIncludedMembers={includedIndexes.Length}; associationHealthy={associationHealthyAfterReport}; GIrequested=false");
                    break;
                }

                if (!spontaneousProven)
                    evidence.Add("G2.5-A spontaneous dchg proof: success=false; no received frame proved exact RptID + DataSet + valid included member mapping with data-change reason only. RptEna acceptance or unrelated reports are not success.");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException)
        {
            evidence.Add($"G2.5-A exception: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (monitorSession is not null)
            {
                try
                {
                    var stop = await auxiliary.StopPersistentReportMonitorAsync(monitorSession, CancellationToken.None).ConfigureAwait(false);
                    monitorCleanup = stop.IsSuccess;
                    AppendWriteSteps(evidence, "G2.5-A monitor cleanup", stop.WriteSteps);
                    evidence.Add($"G2.5-A monitor cleanup: success={stop.IsSuccess}; result={stop.Message}");
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
                {
                    monitorCleanup = false;
                    evidence.Add($"G2.5-A monitor cleanup exception: {ex.GetType().Name}: {ex.Message}");
                }
            }
            else if (!activationAttempted)
            {
                monitorCleanup = true;
            }

            if (fieldLease is not null)
            {
                try
                {
                    var restore = await auxiliary.RestoreDynamicRcbCommissioningFieldsAsync(fieldLease, CancellationToken.None).ConfigureAwait(false);
                    fieldRestore = restore.IsSuccess;
                    AppendWriteSteps(evidence, "G2.5-A proof-field restore", restore.WriteSteps);
                    foreach (var line in restore.Evidence)
                        evidence.Add("G2.5-A proof-field restore: " + line);
                    evidence.Add($"G2.5-A proof-field restore: success={restore.IsSuccess}; result={restore.Message}");
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
                {
                    fieldRestore = false;
                    evidence.Add($"G2.5-A proof-field restore exception: {ex.GetType().Name}: {ex.Message}");
                }
            }
            else
            {
                fieldRestore = true;
            }

            await auxiliary.DisposeAsync().ConfigureAwait(false);
        }

        var freshClosure = false;
        if (plan is not null && activationAttempted)
        {
            freshClosure = await ProveFreshCleanupClosureAsync(
                device,
                rcbReference,
                plan.DataSetReference,
                evidence,
                CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            freshClosure = monitorCleanup && fieldRestore;
        }

        var success = activationProven &&
                      spontaneousProven &&
                      associationHealthyAfterReport &&
                      monitorCleanup &&
                      fieldRestore &&
                      freshClosure;
        evidence.Add($"G2.5-A combined result: activation={activationProven}; spontaneousDchg={spontaneousProven}; reportAssociationHealthy={associationHealthyAfterReport}; monitorCleanup={monitorCleanup}; proofFieldRestore={fieldRestore}; freshCleanupClosure={freshClosure}; success={success}");
        evidence.Add("G2.5-A safety: persisted profile remains InformationReportProven. Production automatic dynamic reporting remains OFF; this gate cannot set ProductionEligible.");

        return new DynamicReportSpontaneousDataChangeCommissioningResult
        {
            IsSuccess = success,
            ActivationProven = activationProven,
            SpontaneousDataChangeProven = spontaneousProven,
            MonitorCleanupSucceeded = monitorCleanup,
            ProofFieldRestoreSucceeded = fieldRestore,
            FreshCleanupClosureSucceeded = freshClosure,
            AssociationHealthyAfterReport = associationHealthyAfterReport,
            Summary = success
                ? $"G2.5-A PASS: exact G2.4-proven URCB delivered a spontaneous data-change InformationReport without GI for {includedIndexes.Length} included member(s), and monitor/proof-field/fresh-association cleanup all passed. Profile remains InformationReportProven; production dynamic reporting remains OFF."
                : "G2.5-A did not prove the complete spontaneous dchg gate. Cleanup evidence is shown below; the persisted InformationReportProven profile is unchanged and production dynamic reporting remains OFF.",
            Identity = identity,
            InputProfile = profile,
            RcbReference = rcbReference,
            DataSetReference = plan?.DataSetReference ?? string.Empty,
            ReportId = reportId,
            MemberReferences = qualifiedReferences,
            IncludedIndexes = includedIndexes,
            IncludedMemberReferences = includedMembers,
            Reasons = includedReasons,
            ProfilePath = loaded.FilePath,
            EvidenceLines = evidence
        };
    }

    internal static bool IsPostLeaseUrcbSafeForDchg(
        ArMms.MmsRcbAvailabilitySnapshot? snapshot,
        string localTcpAddress,
        out string reason)
    {
        if (snapshot is null)
        {
            reason = "Selected URCB is missing from post-lease readback.";
            return false;
        }
        if (snapshot.Buffered)
        {
            reason = "G2.5-A permits URCB only.";
            return false;
        }
        if (snapshot.DataSetProbeState != ArMms.MmsRcbDataSetProbeState.ReadSucceeded || !string.IsNullOrWhiteSpace(snapshot.DataSetReference))
        {
            reason = "Post-lease DatSet must still be positively read and empty.";
            return false;
        }
        if (ParseBool(snapshot.EnabledState) != false)
        {
            reason = $"Post-lease RptEna is not explicit false: {TextOrDash(snapshot.EnabledState)}";
            return false;
        }
        if (snapshot.Availability != ArMms.MmsRcbOperationalAvailability.UsedByCaller)
        {
            reason = $"Post-lease ownership is not UsedByCaller: {snapshot.Availability}.";
            return false;
        }
        if (ParseUnsigned(snapshot.ReservationTimeSeconds) is > 0)
        {
            reason = $"Post-lease reservation time is positive: {snapshot.ReservationTimeSeconds}.";
            return false;
        }

        if (HasOwner(snapshot.Owner) &&
            !ArMms.MmsRcbOwnerIdentity.MatchesLocalTcpAddress(snapshot.Owner, localTcpAddress, out var ownerReason))
        {
            reason = "Post-lease Owner does not match the active G2.5-A MMS association: " + ownerReason;
            return false;
        }

        var triggers = ArMms.MmsReportControlFieldCodec.DecodeTriggerOptions(snapshot.TriggerOptions);
        if (!triggers.DataChange || triggers.GeneralInterrogation || triggers.Integrity || triggers.QualityChange || triggers.DataUpdate)
        {
            reason = $"TrgOps is not strict dchg-only: dchg={triggers.DataChange}, qchg={triggers.QualityChange}, dupd={triggers.DataUpdate}, integrity={triggers.Integrity}, GI={triggers.GeneralInterrogation}.";
            return false;
        }

        var fields = ArMms.MmsReportControlFieldCodec.DecodeOptionalFields(snapshot.OptionalFields);
        if (!fields.ReasonForInclusion || !fields.DataSetName || string.IsNullOrWhiteSpace(snapshot.ReportId))
        {
            reason = $"Strict report identity fields missing: RptID={TextOrDash(snapshot.ReportId)}, reason={fields.ReasonForInclusion}, dataSetName={fields.DataSetName}.";
            return false;
        }

        reason = "Caller-owned post-lease URCB is strict dchg-only with GI/integrity/qchg/dupd disabled and reason-for-inclusion + DataSet-name enabled.";
        return true;
    }

    internal static DynamicReportSpontaneousDataChangeValidation ValidateSpontaneousDataChangeFrame(
        ArMms.MmsReportFrame frame,
        string expectedReportId,
        string expectedDataSetReference,
        IReadOnlyList<string> qualifiedReferences)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(qualifiedReferences);

        if (frame.DecoderMode.Equals("rejected-unmapped", StringComparison.OrdinalIgnoreCase))
            return Invalid("Report decoder quarantined the frame as unmapped.");
        if (string.IsNullOrWhiteSpace(expectedReportId) || !frame.Header.ReportId.Trim().Equals(expectedReportId.Trim(), StringComparison.OrdinalIgnoreCase))
            return Invalid($"RptID mismatch. expected={TextOrDash(expectedReportId)}, actual={TextOrDash(frame.Header.ReportId)}");
        if (string.IsNullOrWhiteSpace(frame.Header.DataSetReference) || !SameReference(frame.Header.DataSetReference, expectedDataSetReference))
            return Invalid($"DataSet mismatch. expected={expectedDataSetReference}, actual={TextOrDash(frame.Header.DataSetReference)}");
        if (qualifiedReferences.Count == 0 || frame.Values.Count == 0)
            return Invalid("Spontaneous dchg proof requires at least one included successful DataSet member.");
        if (frame.IncludedDataSetIndexes.Count != frame.Values.Count)
            return Invalid($"Included-index/value count mismatch: indexes={frame.IncludedDataSetIndexes.Count}, values={frame.Values.Count}.");
        if (frame.IncludedDataSetIndexes.Distinct().Count() != frame.IncludedDataSetIndexes.Count)
            return Invalid("Included DataSet indexes contain duplicates.");

        var included = new List<int>();
        var members = new List<string>();
        var reasons = new List<string>();
        for (var offset = 0; offset < frame.Values.Count; offset++)
        {
            var value = frame.Values[offset];
            var dataSetIndex = frame.IncludedDataSetIndexes[offset];
            if (dataSetIndex < 0 || dataSetIndex >= qualifiedReferences.Count)
                return Invalid($"Included DataSet index {dataSetIndex} is outside 0..{qualifiedReferences.Count - 1}.");
            if (value.Index != dataSetIndex)
                return Invalid($"Mapped value index mismatch at offset {offset}: included={dataSetIndex}, value.Index={value.Index}.");
            if (value.Member is null || !SameReference(value.Member.MmsReference, qualifiedReferences[dataSetIndex]))
                return Invalid($"Mapped member mismatch at DataSet index {dataSetIndex}: expected={qualifiedReferences[dataSetIndex]}, actual={value.Member?.MmsReference ?? "<null>"}.");
            if (value.Value is null || value.FailureCode.HasValue)
                return Invalid($"Included member {qualifiedReferences[dataSetIndex]} has no successful process value (failure={value.FailureCode?.ToString() ?? "none"}).");
            if (!string.IsNullOrWhiteSpace(value.DataReference) &&
                !SameReference(value.DataReference, qualifiedReferences[dataSetIndex]) &&
                !SameReference(value.DataReference, value.Member.UserReference))
            {
                return Invalid($"DataRef mismatch at DataSet index {dataSetIndex}: actual={value.DataReference}.");
            }

            var valueReasons = value.ReasonForInclusion
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (!valueReasons.Contains("data-change", StringComparer.OrdinalIgnoreCase))
                return Invalid($"Included member {qualifiedReferences[dataSetIndex]} does not carry reason-for-inclusion=data-change; reasons={TextOrDash(string.Join(",", valueReasons))}.");
            if (valueReasons.Any(item =>
                    item.Equals("general-interrogation", StringComparison.OrdinalIgnoreCase) ||
                    item.Equals("integrity", StringComparison.OrdinalIgnoreCase) ||
                    item.Equals("quality-change", StringComparison.OrdinalIgnoreCase) ||
                    item.Equals("data-update", StringComparison.OrdinalIgnoreCase)))
            {
                return Invalid($"Included member {qualifiedReferences[dataSetIndex]} carries a non-dchg reason under a dchg-only lease: {string.Join(",", valueReasons)}.");
            }

            included.Add(dataSetIndex);
            members.Add(qualifiedReferences[dataSetIndex]);
            reasons.AddRange(valueReasons);
        }

        return new DynamicReportSpontaneousDataChangeValidation
        {
            IsSuccess = true,
            IncludedIndexes = included.ToArray(),
            IncludedMemberReferences = members.ToArray(),
            Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Reason = $"Actual spontaneous InformationReport verified with dchg-only reasons for {included.Count} included member(s); GI was not requested."
        };
    }

    private async Task<bool> ProveFreshCleanupClosureAsync(
        Iec61850MonitorDevice device,
        string rcbReference,
        string temporaryDataSetReference,
        ICollection<string> evidence,
        CancellationToken cancellationToken)
    {
        await using var fresh = new ArMms.MmsClientSession();
        try
        {
            await fresh.ConnectAsync(device.IpAddress, device.Port, AuxiliaryAssociationTimeout, cancellationToken).ConfigureAwait(false);
            evidence.Add($"G2.5-A fresh cleanup association ready: state={fresh.State}; localTcpAddress={TextOrDash(fresh.LocalTcpAddress)}");
            var discovery = await fresh.DiscoverAsync(
                probeReportAttributes: true,
                maxReportAttributeProbes: 64,
                cancellationToken: cancellationToken,
                readDataSetDirectories: false,
                maxDataSetDirectoryReads: 0).ConfigureAwait(false);

            var rcb = discovery.ReportInventory.ReportControls.FirstOrDefault(candidate => SameReference(candidate.Reference, rcbReference));
            if (rcb is null)
            {
                evidence.Add("G2.5-A fresh cleanup: exact URCB not found.");
                return false;
            }

            var oneRcb = new ArMms.MmsReportInventory();
            oneRcb.ReportControls.Add(rcb);
            var availability = await fresh.CheckReportControlAvailabilityAsync(
                oneRcb,
                discovery.IedDirectory,
                new ArMms.MmsRcbAvailabilityOptions { MaxReportControls = 1, ReadDataSetDirectories = false },
                cancellationToken).ConfigureAwait(false);
            var snapshot = availability.ReportControls.SingleOrDefault();
            if (snapshot is not null)
                evidence.Add($"G2.5-A fresh cleanup URCB: availability={snapshot.Availability}; probe={snapshot.DataSetProbeState}; DatSet={TextOrDash(snapshot.DataSetReference)}; RptEna={TextOrDash(snapshot.EnabledState)}; Resv={TextOrDash(snapshot.ReservationState)}; Owner={TextOrDash(snapshot.Owner)}; TrgOps={TextOrDash(snapshot.TriggerOptions)}; OptFlds={TextOrDash(snapshot.OptionalFields)}");

            var nameAbsent = DynamicReportCleanupClosureCommissioningService.IsTemporaryDataSetAbsentFromNameList(
                discovery.Snapshot,
                temporaryDataSetReference,
                out var nameReason);
            evidence.Add("G2.5-A fresh cleanup namespace: " + nameReason);

            var directory = await fresh.GetDataSetDirectoryAsync(temporaryDataSetReference, discovery.IedDirectory, cancellationToken).ConfigureAwait(false);
            var directoryAbsent = !directory.IsSuccess;
            evidence.Add($"G2.5-A fresh cleanup DataSet directory: absent={directoryAbsent}; success={directory.IsSuccess}; members={directory.Members.Count}; result={directory.Message}");

            var closed = DynamicReportCleanupClosureCommissioningService.IsFreshCleanupClosed(
                snapshot,
                nameAbsent,
                directoryAbsent,
                fresh.IsMmsInitiated,
                out var closureReason);
            evidence.Add("G2.5-A fresh cleanup evaluation: " + closureReason);
            return closed;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException)
        {
            evidence.Add($"G2.5-A fresh cleanup exception: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
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

    private static void EnsureAttribute(ArMms.MmsReportControlCandidate target, string attribute)
    {
        if (!target.Attributes.Contains(attribute, StringComparer.OrdinalIgnoreCase))
            target.Attributes.Add(attribute);
    }

    private static bool SuccessfulStep(IEnumerable<ArMms.MmsReportAttributeWriteStep> steps, string attribute)
        => steps.Any(step => step.Attempted && step.IsSuccess && step.Attribute.Equals(attribute, StringComparison.OrdinalIgnoreCase));

    private static void AppendWriteSteps(ICollection<string> evidence, string label, IEnumerable<ArMms.MmsReportAttributeWriteStep> steps)
    {
        foreach (var step in steps)
            evidence.Add($"{label} write: attribute={step.Attribute}; reference={step.Reference}; attempted={step.Attempted}; success={step.IsSuccess}; result={step.Message}");
    }

    private static DynamicReportSpontaneousDataChangeValidation Invalid(string reason)
        => new() { IsSuccess = false, Reason = reason };

    private static bool ExactSequenceEquals(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        var left = expected.ToArray();
        var right = actual.ToArray();
        return left.Length == right.Length && left.Select(NormalizeReference).SequenceEqual(right.Select(NormalizeReference), StringComparer.OrdinalIgnoreCase);
    }

    private static bool SameReference(string? left, string? right)
        => NormalizeReference(left).Equals(NormalizeReference(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');

    private static bool? ParseBool(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text == "-") return null;
        if (bool.TryParse(text, out var parsed)) return parsed;
        if (text is "1" or "01" || text.Equals("yes", StringComparison.OrdinalIgnoreCase) || text.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
        if (text is "0" or "00" || text.Equals("no", StringComparison.OrdinalIgnoreCase) || text.Equals("off", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    private static ulong? ParseUnsigned(string? value)
        => ulong.TryParse((value ?? string.Empty).Trim(), out var parsed) ? parsed : null;

    private static bool HasOwner(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text == "-" || text == "[]" || text.Equals("null", StringComparison.OrdinalIgnoreCase)) return false;
        var compact = text.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return compact.Length > 0 && compact.Any(character => character != '0');
    }

    private static DynamicReportSpontaneousDataChangeCommissioningResult Blocked(
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

    private static DynamicReportSpontaneousDataChangeCommissioningResult Failed(
        string summary,
        IReadOnlyList<string> evidence,
        ArMms.MmsDynamicReportIedIdentity identity,
        ArMms.MmsDynamicReportQualificationProfile profile,
        string profilePath,
        string rcbReference,
        IReadOnlyList<string> memberReferences,
        string dataSetReference = "",
        bool monitorCleanupSucceeded = false)
        => new()
        {
            IsSuccess = false,
            Summary = summary,
            Identity = identity,
            InputProfile = profile,
            RcbReference = rcbReference,
            DataSetReference = dataSetReference,
            MemberReferences = memberReferences.ToArray(),
            MonitorCleanupSucceeded = monitorCleanupSucceeded,
            ProfilePath = profilePath,
            EvidenceLines = evidence.ToArray()
        };

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
