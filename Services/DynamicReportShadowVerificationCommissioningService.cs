using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

internal sealed class DynamicReportShadowVerificationCommissioningResult
{
    public bool IsSuccess { get; init; }
    public bool IsBlocked { get; init; }
    public bool PhysicalCollectionCompleted { get; init; }
    public bool ShadowPassed { get; init; }
    public bool CleanupSucceeded { get; init; }
    public bool ReconnectProven { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string RcbReference { get; init; } = string.Empty;
    public IReadOnlyList<string> MemberReferences { get; init; } = Array.Empty<string>();
    public ArMms.MmsDynamicReportShadowVerificationEvidence? Evidence { get; init; }
    public DynamicReportShadowVerificationAcceptanceResult? Acceptance { get; init; }
    public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();
}

/// <summary>
/// G2.6 physical shadow collector.
///
/// The collector intentionally keeps two independent MMS authorities alive while each
/// report phase is armed: one transactional one-URCB dchg-only report association and one
/// read-only direct-MMS polling association. It performs two bounded report phases with a
/// deliberate teardown/reconnect between them. It never issues a control command, never
/// writes the qualification profile and never calls MarkProductionEligible.
///
/// Report quality/timestamp evidence is accepted only when it is physically carried by the
/// received InformationReport and projected by ARIEC. Poll quality/timestamp evidence is
/// independently read from exact live q/t companion objects on the isolated read-only MMS
/// polling association. Neither side borrows metadata from the other, and host receive/read
/// time is never substituted for an IEC 61850 device timestamp.
/// </summary>
internal sealed class DynamicReportShadowVerificationCommissioningService
{
    internal const string Phase1ReadyMarker = "G2.6 SHADOW PHASE 1 READY — CAUSE ONE SAFE CHANGE";
    internal const string Phase2ReadyMarker = "G2.6 SHADOW PHASE 2 READY — CAUSE ONE SAFE CHANGE";
    internal static readonly TimeSpan AssociationTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan ReportWindow = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    internal const string TemporaryTriggerOptions = "dchg";
    internal const string TemporaryOptionalFields = "reason-for-inclusion data-set-name";

    private readonly DynamicReportQualificationProfileStore _profileStore;
    private readonly DynamicReportShadowVerificationAcceptanceService _acceptanceService;

    public DynamicReportShadowVerificationCommissioningService(
        DynamicReportQualificationProfileStore? profileStore = null)
    {
        _profileStore = profileStore ?? new DynamicReportQualificationProfileStore();
        _acceptanceService = new DynamicReportShadowVerificationAcceptanceService(_profileStore);
    }

    public async Task<DynamicReportShadowVerificationCommissioningResult> RunAsync(
        Iec61850MonitorDevice device,
        IReadOnlyList<SignalDefinition> fullModelSignals,
        IProgress<string>? progress = null,
        bool controlRegressionPassed = false,
        bool staticReportingRegressionPassed = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fullModelSignals);

        var lines = new List<string>
        {
            "G2.6 physical shadow contract: exact persisted InformationReportProven envelope + transactional one-URCB dchg reporting + independent read-only MMS polling + deliberate reconnect.",
            "G2.6 physical shadow command safety: this collector issues ZERO control commands. The operator must cause exactly one already-approved safe process/status change only after each READY marker.",
            "G2.6 physical shadow profile safety: no profile save, no downgrade, no promotion, no MarkProductionEligible. Production automatic dynamic reporting remains OFF.",
            "G2.6 physical shadow q/t safety: report q/t is accepted only from the InformationReport; poll q/t is read independently from exact live q/t companions. Missing metadata stays missing; TimeOfEntry/read time is never a device-timestamp fallback."
        };

        ArMms.MmsDynamicReportIedIdentity identity;
        try
        {
            identity = DynamicReportQualificationIdentity.Build(device, fullModelSignals);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Blocked("G2.6 shadow identity preflight failed: " + ex.Message, lines);
        }

        var loaded = await _profileStore.LoadAsync(identity, cancellationToken).ConfigureAwait(false);
        lines.Add($"G2.6 shadow profile: exists={loaded.Exists}; valid={loaded.IsValid}; state={loaded.Profile?.State.ToString() ?? "-"}; reason={loaded.Reason}");
        if (!loaded.IsValid || loaded.Profile is null)
            return Blocked("G2.6 physical shadow requires the exact identity-compatible persisted qualification profile.", lines);

        var profile = loaded.Profile;
        if (profile.State != ArMms.MmsDynamicReportQualificationState.InformationReportProven ||
            profile.RcbActivationProof?.IsSuccess != true ||
            profile.InformationReportProof?.IsSuccess != true)
        {
            return Blocked(
                $"G2.6 physical shadow requires a complete InformationReportProven profile; current state is {profile.State}.",
                lines,
                profile.RcbActivationProof?.RcbReference,
                profile.RcbActivationProof?.MemberReferences);
        }

        var rcbReference = profile.RcbActivationProof.RcbReference;
        var members = profile.RcbActivationProof.MemberReferences.ToArray();
        if (string.IsNullOrWhiteSpace(rcbReference) || members.Length == 0 ||
            members.Length > DynamicReportActivationCommissioningService.MaximumG24Members)
        {
            return Blocked(
                "G2.6 physical shadow profile does not retain a usable exact one-URCB G2.4 member envelope.",
                lines,
                rcbReference,
                members);
        }

        lines.Add($"G2.6 exact target: rcb={rcbReference}; members={members.Length}; fieldProfile={profile.State}; pollInterval={PollInterval.TotalMilliseconds:0}ms; phaseWindow={ReportWindow.TotalSeconds:0}s");
        lines.Add("G2.6 exact members: " + string.Join(" | ", members));

        var recorder = new DynamicReportShadowEvidenceRecorder(
            $"arsas-g2.6-shadow-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}",
            members);

        var phase1 = await RunPhaseAsync(
            1,
            device,
            members,
            rcbReference,
            recorder,
            lines,
            progress,
            cancellationToken).ConfigureAwait(false);
        if (!phase1.IsSuccess)
        {
            return Failed(
                "G2.6 shadow phase 1 did not close. No reconnect/production conclusion is allowed.",
                lines,
                rcbReference,
                members,
                cleanupSucceeded: phase1.CleanupSucceeded);
        }

        recorder.RecordReconnectAttempt();
        lines.Add("G2.6 deliberate reconnect boundary: phase 1 report + poll associations are closed; phase 2 must independently re-establish both paths and re-arm the exact RCB once.");
        progress?.Report("G2.6 SHADOW RECONNECT — both phase-1 MMS associations closed. Re-establishing independent report + polling paths; do not cause a process change yet.");

        var phase2 = await RunPhaseAsync(
            2,
            device,
            members,
            rcbReference,
            recorder,
            lines,
            progress,
            cancellationToken).ConfigureAwait(false);
        if (!phase2.IsSuccess)
        {
            return Failed(
                "G2.6 shadow reconnect phase did not close both report and polling paths. Production automatic dynamic reporting remains OFF.",
                lines,
                rcbReference,
                members,
                cleanupSucceeded: phase1.CleanupSucceeded && phase2.CleanupSucceeded);
        }

        recorder.RecordReconnectSuccess(
            reportResubscribed: phase2.ActivationProven,
            pollReferenceRecovered: phase2.PollReferenceRecovered);

        var collected = recorder.BuildEvidence(DateTimeOffset.UtcNow);
        lines.Add($"G2.6 physical evidence collected: reports={collected.ReportObservations.Count}; polls={collected.PollObservations.Count}; reconnect={collected.SuccessfulReconnects}/{collected.ReconnectAttempts}; reportResubscriptions={collected.ReportResubscriptionsAfterReconnect}; pollRecoveries={collected.PollReferenceRecoveriesAfterReconnect}; dynamicAttempts={collected.DynamicActivationAttempts}");
        lines.Add($"G2.6 observed report metadata: qualityObservations={collected.ReportObservations.Count(item => !string.IsNullOrWhiteSpace(item.Quality))}; timestampObservations={collected.ReportObservations.Count(item => item.DeviceTimestampUtc.HasValue)}. Missing q/t remains missing by design.");
        lines.Add($"G2.6 observed independent poll metadata: qualityObservations={collected.PollObservations.Count(item => !string.IsNullOrWhiteSpace(item.Quality))}; timestampObservations={collected.PollObservations.Count(item => item.DeviceTimestampUtc.HasValue)}. Missing q/t remains missing by design.");

        var acceptance = await _acceptanceService.EvaluateAsync(
            device,
            fullModelSignals,
            collected,
            controlRegressionPassed,
            staticReportingRegressionPassed,
            cancellationToken).ConfigureAwait(false);
        lines.AddRange(acceptance.EvidenceLines.Select(line => "ACCEPTANCE: " + line));

        var shadowPassed = acceptance.Shadow?.IsSuccess == true;
        var cleanup = phase1.CleanupSucceeded && phase2.CleanupSucceeded;
        var reconnect = collected.ReconnectAttempts == 1 &&
                        collected.SuccessfulReconnects == 1 &&
                        collected.ReportResubscriptionsAfterReconnect == 1 &&
                        collected.PollReferenceRecoveriesAfterReconnect == 1;
        var physicalComplete = phase1.IsSuccess && phase2.IsSuccess && cleanup && reconnect;
        var success = physicalComplete && shadowPassed;

        lines.Add($"G2.6 final collector result: physicalComplete={physicalComplete}; shadowPassed={shadowPassed}; cleanup={cleanup}; reconnect={reconnect}; strictProductionCandidate={acceptance.ProductionAcceptanceCandidate?.AllPassed == true}; collectorSuccess={success}");
        lines.Add("G2.6 final state boundary: physical shadow evidence cannot modify the persisted profile. Shadow PASS != ProductionEligible; production automatic dynamic reporting remains OFF.");

        return new DynamicReportShadowVerificationCommissioningResult
        {
            IsSuccess = success,
            PhysicalCollectionCompleted = physicalComplete,
            ShadowPassed = shadowPassed,
            CleanupSucceeded = cleanup,
            ReconnectProven = reconnect,
            RcbReference = rcbReference,
            MemberReferences = members,
            Evidence = collected,
            Acceptance = acceptance,
            Summary = success
                ? "G2.6 physical shadow PASS: two exact dchg/report-vs-poll phases plus deliberate reconnect closed the typed shadow gates. Profile remains InformationReportProven; ProductionEligible is still OFF pending separate explicit promotion."
                : physicalComplete
                    ? "G2.6 physical collection completed, but the strict typed shadow remains fail-closed. Inspect q/t, parity, missing-edge and independent regression gates; profile remains InformationReportProven."
                    : "G2.6 physical shadow did not complete every collection/cleanup/reconnect gate. Production automatic dynamic reporting remains OFF.",
            EvidenceLines = lines.ToArray()
        };
    }

    private static async Task<ShadowPhaseResult> RunPhaseAsync(
        int phaseNumber,
        Iec61850MonitorDevice device,
        IReadOnlyList<string> qualifiedReferences,
        string rcbReference,
        DynamicReportShadowEvidenceRecorder recorder,
        ICollection<string> evidence,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var label = $"G2.6 shadow phase {phaseNumber}";
        var pollReferenceRecovered = false;
        var activationProven = false;
        var reportProven = false;
        var monitorCleanup = true;
        var fieldRestore = true;
        var freshClosure = true;
        var temporaryDataSetReference = string.Empty;

        await using var pollSession = new ArMms.MmsClientSession();
        try
        {
            await pollSession.ConnectAsync(device.IpAddress, device.Port, AssociationTimeout, cancellationToken).ConfigureAwait(false);
            var pollDiscovery = await pollSession.DiscoverAsync(
                probeReportAttributes: false,
                maxReportAttributeProbes: 0,
                cancellationToken: cancellationToken,
                readDataSetDirectories: false,
                maxDataSetDirectoryReads: 0).ConfigureAwait(false);
            evidence.Add($"{label} polling association: state={pollSession.State}; localTcpAddress={TextOrDash(pollSession.LocalTcpAddress)}; readOnly=true; discovery={pollDiscovery.Summary}");

            if (!DynamicReportActivationCommissioningService.TryResolveExactQualifiedMembers(
                    pollDiscovery.IedDirectory,
                    qualifiedReferences,
                    out var pollPoints,
                    out var pollReason))
            {
                evidence.Add($"{label} polling exact-member resolution failed: {pollReason}");
                return ShadowPhaseResult.Fail(cleanupSucceeded: true);
            }

            pollReferenceRecovered = await CapturePollCycleAsync(
                pollSession,
                pollDiscovery.IedDirectory,
                pollPoints,
                qualifiedReferences,
                recorder,
                evidence,
                label + " initial poll",
                cancellationToken).ConfigureAwait(false);
            if (!pollReferenceRecovered || !pollSession.IsMmsInitiated)
            {
                evidence.Add($"{label} polling baseline did not prove every exact reference; no report mutation will be attempted.");
                return ShadowPhaseResult.Fail(cleanupSucceeded: true);
            }

            var reportSession = new ArMms.MmsClientSession();
            ArMms.MmsDynamicRcbCommissioningFieldLease? fieldLease = null;
            ArMms.MmsPersistentReportMonitorSession? monitor = null;
            try
            {
                await reportSession.ConnectAsync(device.IpAddress, device.Port, AssociationTimeout, cancellationToken).ConfigureAwait(false);
                var discovery = await reportSession.DiscoverAsync(
                    probeReportAttributes: true,
                    maxReportAttributeProbes: 64,
                    cancellationToken: cancellationToken,
                    readDataSetDirectories: false,
                    maxDataSetDirectoryReads: 0).ConfigureAwait(false);
                evidence.Add($"{label} report association: state={reportSession.State}; localTcpAddress={TextOrDash(reportSession.LocalTcpAddress)}; discovery={discovery.Summary}");

                if (!DynamicReportActivationCommissioningService.TryResolveExactQualifiedMembers(
                        discovery.IedDirectory,
                        qualifiedReferences,
                        out var exactPoints,
                        out var exactReason))
                {
                    evidence.Add($"{label} report exact-member resolution failed: {exactReason}");
                    return ShadowPhaseResult.Fail(cleanupSucceeded: true);
                }

                foreach (var point in exactPoints)
                {
                    var read = await reportSession.ReadSingleVariableAsync(point.ToObjectReference(), cancellationToken).ConfigureAwait(false);
                    if (!read.IsSuccess || read.Value is null || !reportSession.IsMmsInitiated)
                    {
                        evidence.Add($"{label} report preflight direct read failed: ref={point.MmsReference}; result={read.Message}");
                        return ShadowPhaseResult.Fail(cleanupSucceeded: true);
                    }
                }

                var selectedRcb = discovery.ReportInventory.ReportControls.FirstOrDefault(candidate => SameReference(candidate.Reference, rcbReference));
                if (selectedRcb is null || selectedRcb.Buffered)
                {
                    evidence.Add($"{label} exact persisted URCB is absent or no longer unbuffered: {rcbReference}");
                    return ShadowPhaseResult.Fail(cleanupSucceeded: true);
                }

                var oneRcb = new ArMms.MmsReportInventory();
                oneRcb.ReportControls.Add(selectedRcb);
                var preLeaseAvailability = await reportSession.CheckReportControlAvailabilityAsync(
                    oneRcb,
                    discovery.IedDirectory,
                    new ArMms.MmsRcbAvailabilityOptions { MaxReportControls = 1, ReadDataSetDirectories = false },
                    cancellationToken).ConfigureAwait(false);
                var preLease = preLeaseAvailability.ReportControls.SingleOrDefault();
                var freeReason = "snapshot missing";
                var free = preLease is not null && DynamicReportActivationCommissioningServiceV2.IsLeaseableFreeUrcbForG24(preLease, out freeReason);
                evidence.Add($"{label} pre-lease exact URCB: free={free}; reason={freeReason}");
                if (!free || preLease is null)
                    return ShadowPhaseResult.Fail(cleanupSucceeded: true);

                ApplyFreshSnapshot(selectedRcb, preLease);
                var prepare = await reportSession.PrepareDynamicRcbCommissioningFieldsAsync(
                    selectedRcb,
                    TemporaryTriggerOptions,
                    TemporaryOptionalFields,
                    cancellationToken).ConfigureAwait(false);
                AppendWriteSteps(evidence, label + " proof-field prepare", prepare.WriteSteps);
                if (!prepare.IsSuccess || prepare.Lease is null)
                {
                    evidence.Add($"{label} dchg-only proof-field lease failed: rollback={prepare.CleanupSucceeded}; result={prepare.Message}");
                    return ShadowPhaseResult.Fail(cleanupSucceeded: prepare.CleanupSucceeded);
                }
                fieldLease = prepare.Lease;

                var plan = ArMms.MmsReportSubscriptionPlanner.BuildDynamicPlan(
                    discovery.ReportInventory,
                    discovery.IedDirectory,
                    exactPoints.Select(point => point.UserReference),
                    preferredLogicalDevice: selectedRcb.Domain,
                    preferredRcbReference: selectedRcb.Reference,
                    dataSetName: $"AR_G26S{phaseNumber}_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                    strictRcb: true,
                    allowUrCbFallback: true,
                    allowPollingFallback: false);
                temporaryDataSetReference = plan.DataSetReference;
                if (!DynamicReportActivationCommissioningService.ValidatePlanAgainstEnvelope(plan, selectedRcb.Reference, qualifiedReferences, out var planReason))
                {
                    evidence.Add($"{label} strict plan rejected: {planReason}");
                    return ShadowPhaseResult.Fail(cleanupSucceeded: false);
                }

                var postLeaseAvailability = await reportSession.CheckReportControlAvailabilityAsync(
                    oneRcb,
                    discovery.IedDirectory,
                    DynamicReportActivationCommissioningServiceV2.BuildPostLeaseAvailabilityOptions(selectedRcb.Reference),
                    cancellationToken).ConfigureAwait(false);
                var postLease = postLeaseAvailability.ReportControls.SingleOrDefault();
                if (!DynamicReportSpontaneousDataChangeCommissioningService.IsPostLeaseUrcbSafeForDchg(
                        postLease,
                        reportSession.LocalTcpAddress,
                        out var postLeaseReason))
                {
                    evidence.Add($"{label} post-lease exact URCB rejected: {postLeaseReason}");
                    return ShadowPhaseResult.Fail(cleanupSucceeded: false);
                }

                ApplyFreshSnapshot(plan.ReportControl!, postLease!);
                EnsureAttribute(plan.ReportControl!, "TrgOps");
                EnsureAttribute(plan.ReportControl!, "OptFlds");
                plan.ReportControl!.TriggerOptions = TemporaryTriggerOptions;
                plan.ReportControl.OptionalFields = TemporaryOptionalFields;
                selectedRcb.TriggerOptions = TemporaryTriggerOptions;
                selectedRcb.OptionalFields = TemporaryOptionalFields;

                recorder.RecordDynamicActivationAttempt();
                monitorCleanup = false;
                var attempt = await reportSession.StartPersistentReportMonitorWithAttemptEvidenceAsync(
                    plan,
                    triggerGeneralInterrogation: false,
                    deleteDynamicDataSetOnStop: true,
                    directory: discovery.IedDirectory,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                AppendWriteSteps(evidence, label + " activation", attempt.StartResult.WriteSteps);
                if (!attempt.IsSuccess || attempt.StartResult.Session is null)
                {
                    monitorCleanup = attempt.CleanupSucceeded;
                    evidence.Add($"{label} activation failed: reason={attempt.FailureReason}; cleanup={attempt.CleanupSucceeded}; result={attempt.StartResult.Message}");
                    return ShadowPhaseResult.Fail(cleanupSucceeded: monitorCleanup);
                }

                monitor = attempt.StartResult.Session;
                var readback = await reportSession.GetDataSetDirectoryAsync(plan.DataSetReference, discovery.IedDirectory, cancellationToken).ConfigureAwait(false);
                var exactReadback = readback.IsSuccess && ExactSequenceEquals(qualifiedReferences, readback.Members.Select(member => member.MmsReference));
                var afterEnable = attempt.StartResult.RcbSnapshots.LastOrDefault(snapshot => snapshot.Stage.Equals("after-enable", StringComparison.OrdinalIgnoreCase));
                var bindingAccepted = SuccessfulStep(attempt.StartResult.WriteSteps, "DatSet") && afterEnable is not null && afterEnable.IsSuccess && SameReference(afterEnable.DataSetReference, plan.DataSetReference);
                var rptEnaAccepted = SuccessfulStep(attempt.StartResult.WriteSteps, "RptEna") && afterEnable is not null && afterEnable.IsSuccess && ParseBool(afterEnable.EnabledState) == true;
                activationProven = exactReadback && bindingAccepted && rptEnaAccepted && reportSession.IsMmsInitiated;
                evidence.Add($"{label} activation proof: success={activationProven}; exactReadback={exactReadback}; binding={bindingAccepted}; RptEna={rptEnaAccepted}; GI=false; associationHealthy={reportSession.IsMmsInitiated}");
                if (!activationProven)
                    return ShadowPhaseResult.Fail(cleanupSucceeded: false);

                using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var pollTask = PollLoopAsync(
                    pollSession,
                    pollDiscovery.IedDirectory,
                    pollPoints,
                    qualifiedReferences,
                    recorder,
                    evidence,
                    label,
                    pollCts.Token);

                var readyMarker = phaseNumber == 1 ? Phase1ReadyMarker : Phase2ReadyMarker;
                progress?.Report($"{readyMarker} — report is strict dchg-only and independent MMS polling is already active. Cause exactly ONE approved safe change affecting the proven member envelope. No automatic command is issued.");
                evidence.Add($"{readyMarker}: waiting up to {ReportWindow.TotalSeconds:0}s; GI=false; independentPoll=true; autoControl=false");

                ArMms.MmsPersistentReportMonitorReceiveResult receive;
                try
                {
                    receive = await reportSession.ReceivePersistentReportMonitorSliceAsync(
                        monitor,
                        ReportWindow,
                        pollDirectory: null,
                        pollReferences: null,
                        pollInterval: null,
                        triggerGeneralInterrogation: false,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    pollCts.Cancel();
                    try { await pollTask.ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }

                evidence.Add($"{label} receive: reports={receive.Reports.Count}; unrouted={reportSession.UnroutedPersistentReportCount}; route={TextOrDash(reportSession.LastReceiveRoutingSummary)}; GI=false; result={receive.Message}");
                foreach (var frame in receive.Reports)
                {
                    var validation = DynamicReportSpontaneousDataChangeCommissioningService.ValidateSpontaneousDataChangeFrame(
                        frame,
                        postLease!.ReportId,
                        plan.DataSetReference,
                        qualifiedReferences);
                    evidence.Add($"{label} report candidate: receivedAt={frame.ReceivedAt:O}; sqNum={frame.Header.SequenceNumber?.ToString() ?? "-"}; values={frame.Values.Count}; included=[{string.Join(",", frame.IncludedDataSetIndexes)}]; valid={validation.IsSuccess}; reason={validation.Reason}");
                    if (!validation.IsSuccess)
                        continue;

                    RecordFrame(frame, qualifiedReferences, recorder, evidence, label);
                    reportProven = true;
                    break;
                }

                if (!reportProven)
                    evidence.Add($"{label} did not receive one exact dchg-only InformationReport inside the bounded window.");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException)
            {
                evidence.Add($"{label} fail-closed exception: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (monitor is not null)
                {
                    try
                    {
                        var stop = await reportSession.StopPersistentReportMonitorAsync(monitor, CancellationToken.None).ConfigureAwait(false);
                        monitorCleanup = stop.IsSuccess;
                        AppendWriteSteps(evidence, label + " monitor cleanup", stop.WriteSteps);
                        evidence.Add($"{label} monitor cleanup: success={stop.IsSuccess}; result={stop.Message}");
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
                    {
                        monitorCleanup = false;
                        evidence.Add($"{label} monitor cleanup exception: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                if (fieldLease is not null)
                {
                    fieldRestore = false;
                    try
                    {
                        var restore = await reportSession.RestoreDynamicRcbCommissioningFieldsAsync(fieldLease, CancellationToken.None).ConfigureAwait(false);
                        fieldRestore = restore.IsSuccess;
                        AppendWriteSteps(evidence, label + " proof-field restore", restore.WriteSteps);
                        evidence.Add($"{label} proof-field restore: success={restore.IsSuccess}; result={restore.Message}");
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
                    {
                        evidence.Add($"{label} proof-field restore exception: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                await reportSession.DisposeAsync().ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(temporaryDataSetReference))
            {
                freshClosure = await ProveFreshCleanupClosureAsync(
                    device,
                    rcbReference,
                    temporaryDataSetReference,
                    evidence,
                    label,
                    CancellationToken.None).ConfigureAwait(false);
            }

            var cleanup = monitorCleanup && fieldRestore && freshClosure;
            var success = activationProven && reportProven && pollReferenceRecovered && cleanup;
            evidence.Add($"{label} combined: activation={activationProven}; report={reportProven}; pollReference={pollReferenceRecovered}; monitorCleanup={monitorCleanup}; fieldRestore={fieldRestore}; freshClosure={freshClosure}; success={success}");
            return new ShadowPhaseResult(success, cleanup, activationProven, pollReferenceRecovered);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException)
        {
            evidence.Add($"{label} polling association exception: {ex.GetType().Name}: {ex.Message}");
            return ShadowPhaseResult.Fail(cleanupSucceeded: true);
        }
    }

    private static async Task<bool> CapturePollCycleAsync(
        ArMms.MmsClientSession session,
        ArMms.MmsIedModelDirectory directory,
        IReadOnlyList<ArMms.MmsFcResolvedPoint> points,
        IReadOnlyList<string> qualifiedReferences,
        DynamicReportShadowEvidenceRecorder recorder,
        ICollection<string> evidence,
        string label,
        CancellationToken cancellationToken)
    {
        if (points.Count != qualifiedReferences.Count)
            return false;

        for (var index = 0; index < points.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var readAtUtc = DateTimeOffset.UtcNow;
            var read = await session.ReadSingleVariableAsync(points[index].ToObjectReference(), cancellationToken).ConfigureAwait(false);
            if (!read.IsSuccess || read.Value is null || !session.IsMmsInitiated)
            {
                evidence.Add($"{label}: read failed index={index}; ref={qualifiedReferences[index]}; result={read.Message}");
                return false;
            }

            var companion = await DynamicReportShadowPollingCompanionReader.ReadAsync(
                session,
                directory,
                points[index],
                cancellationToken).ConfigureAwait(false);

            recorder.RecordPoll(
                index,
                qualifiedReferences[index],
                ArMms.MmsDataValueRenderer.ToCompactString(read.Value),
                companion.Quality,
                companion.DeviceTimestampUtc,
                readAtUtc);
            evidence.Add($"{label}: read success index={index}; ref={qualifiedReferences[index]}; q={(string.IsNullOrWhiteSpace(companion.Quality) ? "missing" : "observed")}; t={(companion.DeviceTimestampUtc.HasValue ? "observed" : "missing")}; qAttempt={companion.QualityReadAttempted}; tAttempt={companion.TimestampReadAttempted}; qRef={TextOrDash(companion.QualityReference)}; tRef={TextOrDash(companion.TimestampReference)}");
        }

        return true;
    }

    private static async Task PollLoopAsync(
        ArMms.MmsClientSession session,
        ArMms.MmsIedModelDirectory directory,
        IReadOnlyList<ArMms.MmsFcResolvedPoint> points,
        IReadOnlyList<string> qualifiedReferences,
        DynamicReportShadowEvidenceRecorder recorder,
        ICollection<string> evidence,
        string label,
        CancellationToken cancellationToken)
    {
        var cycles = 0;
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested && session.IsMmsInitiated)
        {
            cycles++;
            for (var index = 0; index < points.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var readAtUtc = DateTimeOffset.UtcNow;
                var read = await session.ReadSingleVariableAsync(points[index].ToObjectReference(), cancellationToken).ConfigureAwait(false);
                if (!read.IsSuccess || read.Value is null)
                {
                    failures++;
                    continue;
                }

                var companion = await DynamicReportShadowPollingCompanionReader.ReadAsync(
                    session,
                    directory,
                    points[index],
                    cancellationToken).ConfigureAwait(false);

                recorder.RecordPoll(
                    index,
                    qualifiedReferences[index],
                    ArMms.MmsDataValueRenderer.ToCompactString(read.Value),
                    companion.Quality,
                    companion.DeviceTimestampUtc,
                    readAtUtc);
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }

        evidence.Add($"{label} independent polling stopped: cycles={cycles}; failures={failures}; associationHealthy={session.IsMmsInitiated}");
    }

    private static void RecordFrame(
        ArMms.MmsReportFrame frame,
        IReadOnlyList<string> qualifiedReferences,
        DynamicReportShadowEvidenceRecorder recorder,
        ICollection<string> evidence,
        string label)
    {
        var projection = ArMms.MmsReportValueProjector.Project(frame);
        foreach (var value in frame.Values)
        {
            if (value.Index < 0 || value.Index >= qualifiedReferences.Count || value.Value is null || value.FailureCode.HasValue)
                continue;

            var expected = qualifiedReferences[value.Index];
            var projected = projection.Updates.FirstOrDefault(update => SameReference(update.Reference, expected) ||
                (value.Member is not null && SameReference(update.Reference, value.Member.UserReference)));

            var quality = projected?.HasQuality == true ? projected.Quality : null;
            DateTimeOffset? deviceTimestamp = null;
            if (projected?.HasTimestamp == true &&
                DateTimeOffset.TryParse(projected.Timestamp, out var parsedTimestamp))
            {
                deviceTimestamp = parsedTimestamp;
            }

            recorder.RecordReport(
                value.Index,
                expected,
                ArMms.MmsDataValueRenderer.ToCompactString(value.Value),
                quality,
                deviceTimestamp,
                frame.ReceivedAt,
                frame.Header.SequenceNumber);
            evidence.Add($"{label} recorded report observation: index={value.Index}; member={expected}; q={(string.IsNullOrWhiteSpace(quality) ? "missing" : "observed")}; t={(deviceTimestamp.HasValue ? "observed" : "missing")}; receivedAt={frame.ReceivedAt:O}; sqNum={frame.Header.SequenceNumber?.ToString() ?? "-"}");
        }

        foreach (var warning in projection.Warnings)
            evidence.Add($"{label} report projection warning: {warning}");
    }

    private static async Task<bool> ProveFreshCleanupClosureAsync(
        Iec61850MonitorDevice device,
        string rcbReference,
        string temporaryDataSetReference,
        ICollection<string> evidence,
        string label,
        CancellationToken cancellationToken)
    {
        await using var fresh = new ArMms.MmsClientSession();
        try
        {
            await fresh.ConnectAsync(device.IpAddress, device.Port, AssociationTimeout, cancellationToken).ConfigureAwait(false);
            var discovery = await fresh.DiscoverAsync(
                probeReportAttributes: true,
                maxReportAttributeProbes: 64,
                cancellationToken: cancellationToken,
                readDataSetDirectories: false,
                maxDataSetDirectoryReads: 0).ConfigureAwait(false);
            var rcb = discovery.ReportInventory.ReportControls.FirstOrDefault(candidate => SameReference(candidate.Reference, rcbReference));
            if (rcb is null)
            {
                evidence.Add($"{label} fresh cleanup: exact RCB absent.");
                return false;
            }

            var one = new ArMms.MmsReportInventory();
            one.ReportControls.Add(rcb);
            var availability = await fresh.CheckReportControlAvailabilityAsync(
                one,
                discovery.IedDirectory,
                new ArMms.MmsRcbAvailabilityOptions { MaxReportControls = 1, ReadDataSetDirectories = false },
                cancellationToken).ConfigureAwait(false);
            var snapshot = availability.ReportControls.SingleOrDefault();
            var nameAbsent = DynamicReportCleanupClosureCommissioningService.IsTemporaryDataSetAbsentFromNameList(
                discovery.Snapshot,
                temporaryDataSetReference,
                out var nameReason);
            var directory = await fresh.GetDataSetDirectoryAsync(temporaryDataSetReference, discovery.IedDirectory, cancellationToken).ConfigureAwait(false);
            var directoryAbsent = !directory.IsSuccess;
            var closed = DynamicReportCleanupClosureCommissioningService.IsFreshCleanupClosed(
                snapshot,
                nameAbsent,
                directoryAbsent,
                fresh.IsMmsInitiated,
                out var closureReason);
            evidence.Add($"{label} fresh cleanup: nameAbsent={nameAbsent}; directoryAbsent={directoryAbsent}; association={fresh.IsMmsInitiated}; namespace={nameReason}; result={closureReason}");
            return closed;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException)
        {
            evidence.Add($"{label} fresh cleanup exception: {ex.GetType().Name}: {ex.Message}");
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

    private static bool ExactSequenceEquals(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        var left = expected.Select(NormalizeReference).ToArray();
        var right = actual.Select(NormalizeReference).ToArray();
        return left.Length == right.Length && left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);
    }

    private static bool SameReference(string? left, string? right)
        => NormalizeReference(left).Equals(NormalizeReference(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');

    private static bool? ParseBool(string? text)
    {
        if (bool.TryParse(text, out var parsed)) return parsed;
        return (text ?? string.Empty).Trim() switch { "1" => true, "0" => false, _ => null };
    }

    private static string TextOrDash(string? text)
        => string.IsNullOrWhiteSpace(text) ? "-" : text.Trim();

    private static DynamicReportShadowVerificationCommissioningResult Blocked(
        string summary,
        IReadOnlyList<string> evidence,
        string? rcbReference = null,
        IReadOnlyList<string>? members = null)
        => new()
        {
            IsBlocked = true,
            Summary = summary + " Production automatic dynamic reporting remains OFF.",
            RcbReference = rcbReference ?? string.Empty,
            MemberReferences = members?.ToArray() ?? Array.Empty<string>(),
            EvidenceLines = evidence.ToArray()
        };

    private static DynamicReportShadowVerificationCommissioningResult Failed(
        string summary,
        IReadOnlyList<string> evidence,
        string rcbReference,
        IReadOnlyList<string> members,
        bool cleanupSucceeded)
        => new()
        {
            Summary = summary,
            RcbReference = rcbReference,
            MemberReferences = members.ToArray(),
            CleanupSucceeded = cleanupSucceeded,
            EvidenceLines = evidence.ToArray()
        };

    private sealed record ShadowPhaseResult(
        bool IsSuccess,
        bool CleanupSucceeded,
        bool ActivationProven,
        bool PollReferenceRecovered)
    {
        public static ShadowPhaseResult Fail(bool cleanupSucceeded)
            => new(false, cleanupSucceeded, false, false);
    }
}
