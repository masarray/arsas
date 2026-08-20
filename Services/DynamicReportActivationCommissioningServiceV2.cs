using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

/// <summary>
/// G2.4 field-corrected commissioning coordinator.
///
/// The first field correction moved URCB selection from discovery-only state to a
/// forced-live availability sweep. The second physical run then proved 30 empty/free
/// URCBs but also proved that their current TrgOps/OptFlds do not carry GI/DataSetName.
/// This coordinator therefore uses the engine's explicit transactional proof-field
/// lease: capture exact originals, temporarily enable only the self-identifying proof
/// fields, perform one-URCB report proof, then restore exact originals before profile
/// advancement. Production planning remains untouched.
/// </summary>
internal sealed class DynamicReportActivationCommissioningServiceV2
{
    private static readonly TimeSpan AuxiliaryAssociationTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan InformationReportProofWindow = TimeSpan.FromSeconds(10);
    private const string TemporaryTriggerOptions = "dchg gi";
    private const string TemporaryOptionalFields = "reason-for-inclusion data-set-name";

    private readonly DynamicReportQualificationProfileStore _profileStore;

    public DynamicReportActivationCommissioningServiceV2(
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
                "G2.4 requires an identity-compatible persisted G2.3 EnvelopeQualified profile.",
                evidence,
                identity,
                loaded.FilePath);
        }

        var profile = loaded.Profile;
        if (profile.State is ArMms.MmsDynamicReportQualificationState.InformationReportProven or
            ArMms.MmsDynamicReportQualificationState.ProductionEligible)
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
                $"G2.4 requires profile state EnvelopeQualified, but the compatible profile is {profile.State}.",
                evidence,
                identity,
                loaded.FilePath,
                profile);
        }

        var qualifiedReferences = profile.AcceptedEnvelope.ExactProvenMemberReferences
            .Take(Math.Min(DynamicReportActivationCommissioningService.MaximumG24Members, profile.ProvenSafeMemberCount))
            .ToArray();
        if (qualifiedReferences.Length == 0 || qualifiedReferences.Length > profile.ProvenSafeMemberCount)
        {
            return Blocked(
                "The accepted envelope has no usable exact member sequence.",
                evidence,
                identity,
                loaded.FilePath,
                profile);
        }

        evidence.Add($"G2.4 envelope gate: profileState={profile.State}; provenMembers={profile.ProvenSafeMemberCount}; commissioningMembers={qualifiedReferences.Length}; hardCeiling={DynamicReportActivationCommissioningService.MaximumG24Members}");
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
            return Failed("Fresh auxiliary discovery failed before any G2.4 RCB mutation.", evidence, identity, profile, loaded.FilePath);
        }

        evidence.Add($"G2.4 auxiliary discovery: {discovery.Summary}");

        if (!DynamicReportActivationCommissioningService.TryResolveExactQualifiedMembers(
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

        ArMms.MmsRcbAvailabilityResult selectionAvailability;
        try
        {
            selectionAvailability = await auxiliary.CheckReportControlAvailabilityAsync(
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
            evidence.Add($"G2.4 forced live URCB availability sweep failed: {ex.GetType().Name}: {ex.Message}");
            return Failed(
                "G2.4 could not obtain forced live DatSet/RptEna/Resv evidence. No RCB mutation was attempted.",
                evidence,
                identity,
                profile,
                loaded.FilePath,
                qualifiedReferences);
        }

        evidence.Add("G2.4 forced live availability: " + selectionAvailability.Summary);
        foreach (var warning in selectionAvailability.Warnings)
            evidence.Add("G2.4 availability warning: " + warning);

        var selectedRcb = SelectQualifiedUrcbFromFreshAvailability(
            selectionAvailability,
            discovery.ReportInventory,
            exactPoints[0].Domain,
            out var selectedSnapshot,
            out var rcbSelectionReason,
            out var candidateDiagnostics);
        evidence.Add("G2.4 URCB selection: " + rcbSelectionReason);
        foreach (var diagnostic in candidateDiagnostics)
            evidence.Add("G2.4 URCB candidate: " + diagnostic);

        if (selectedRcb is null || selectedSnapshot is null)
        {
            return Failed(
                "No forced-live proven-free URCB is eligible for a transactional G2.4 proof-field lease. No RCB mutation was attempted.",
                evidence,
                identity,
                profile,
                loaded.FilePath,
                qualifiedReferences);
        }

        ApplyFreshSnapshot(selectedRcb, selectedSnapshot);

        ArMms.MmsDynamicRcbCommissioningFieldPrepareResult fieldPrepare;
        try
        {
            fieldPrepare = await auxiliary.PrepareDynamicRcbCommissioningFieldsAsync(
                selectedRcb,
                TemporaryTriggerOptions,
                TemporaryOptionalFields,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
        {
            evidence.Add($"G2.4 proof-field lease exception: {ex.GetType().Name}: {ex.Message}");
            return new DynamicReportActivationCommissioningResult
            {
                IsSuccess = false,
                CleanupSucceeded = false,
                Summary = "G2.4 could not establish the temporary TrgOps/OptFlds lease. Treat the selected URCB as requiring fresh read-only inspection before retry.",
                Identity = identity,
                InputProfile = profile,
                RcbReference = selectedRcb.Reference,
                MemberReferences = qualifiedReferences,
                ProfilePath = loaded.FilePath,
                EvidenceLines = evidence
            };
        }

        AppendWriteSteps(evidence, "G2.4 proof-field prepare", fieldPrepare.WriteSteps);
        foreach (var line in fieldPrepare.Evidence)
            evidence.Add("G2.4 proof-field prepare: " + line);
        evidence.Add($"G2.4 proof-field prepare result: success={fieldPrepare.IsSuccess}; rollback={fieldPrepare.CleanupSucceeded}; result={fieldPrepare.Message}");

        if (!fieldPrepare.IsSuccess || fieldPrepare.Lease is null)
        {
            return new DynamicReportActivationCommissioningResult
            {
                IsSuccess = false,
                CleanupSucceeded = fieldPrepare.CleanupSucceeded,
                Summary = fieldPrepare.CleanupSucceeded
                    ? "G2.4 temporary proof-field preparation failed but exact TrgOps/OptFlds rollback passed. The profile remains EnvelopeQualified."
                    : "G2.4 temporary proof-field preparation failed and exact rollback was not proven. Inspect the RCB from a fresh association before retry.",
                Identity = identity,
                InputProfile = profile,
                RcbReference = selectedRcb.Reference,
                MemberReferences = qualifiedReferences,
                ProfilePath = loaded.FilePath,
                EvidenceLines = evidence
            };
        }

        var fieldLease = fieldPrepare.Lease;
        evidence.Add($"G2.4 proof-field lease ACTIVE: rcb={selectedRcb.Reference}; originalTrgOps={fieldLease.OriginalTriggerOptionsText}; originalOptFlds={fieldLease.OriginalOptionalFieldsText}; temporaryTrgOps={TemporaryTriggerOptions}; temporaryOptFlds={TemporaryOptionalFields}");

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

        if (!DynamicReportActivationCommissioningService.ValidatePlanAgainstEnvelope(
                plan,
                selectedRcb.Reference,
                qualifiedReferences,
                out var planReason))
        {
            evidence.Add("G2.4 plan rejected: " + planReason);
            var fieldRestore = await RestoreProofFieldLeaseAsync(auxiliary, fieldLease, evidence, "plan-reject").ConfigureAwait(false);
            return new DynamicReportActivationCommissioningResult
            {
                IsSuccess = false,
                CleanupSucceeded = fieldRestore,
                Summary = fieldRestore
                    ? "The strict one-URCB plan did not preserve the exact qualified member sequence. Temporary proof fields were restored exactly."
                    : "The strict one-URCB plan was rejected and temporary proof-field restore was not fully proven.",
                Identity = identity,
                InputProfile = profile,
                RcbReference = selectedRcb.Reference,
                DataSetReference = plan.DataSetReference,
                MemberReferences = qualifiedReferences,
                ProfilePath = loaded.FilePath,
                EvidenceLines = evidence
            };
        }

        evidence.Add($"G2.4 plan: rcb={plan.ReportControl!.Reference}; dataset={plan.DataSetReference}; members={plan.DynamicPoints.Count}; mode={plan.Mode}; status={plan.Status}");

        // Re-probe exactly the chosen RCB after the temporary proof-field lease and
        // immediately before the first DataSet/RCB production-like write. This proves
        // both that the URCB is still free and that the temporary GI/DataSetName fields
        // are actually visible from the IED.
        var oneRcbInventory = new ArMms.MmsReportInventory();
        oneRcbInventory.ReportControls.Add(selectedRcb);

        ArMms.MmsRcbAvailabilityResult finalAvailability;
        try
        {
            finalAvailability = await auxiliary.CheckReportControlAvailabilityAsync(
                oneRcbInventory,
                discovery.IedDirectory,
                new ArMms.MmsRcbAvailabilityOptions
                {
                    MaxReportControls = 1,
                    ReadDataSetDirectories = false
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
        {
            evidence.Add($"G2.4 final URCB revalidation failed: {ex.GetType().Name}: {ex.Message}");
            var fieldRestore = await RestoreProofFieldLeaseAsync(auxiliary, fieldLease, evidence, "final-revalidation-exception").ConfigureAwait(false);
            return new DynamicReportActivationCommissioningResult
            {
                IsSuccess = false,
                CleanupSucceeded = fieldRestore,
                Summary = fieldRestore
                    ? "Final URCB state could not be re-read before DataSet binding. Temporary proof fields were restored exactly."
                    : "Final URCB revalidation failed and temporary proof-field restore was not fully proven.",
                Identity = identity,
                InputProfile = profile,
                RcbReference = selectedRcb.Reference,
                DataSetReference = plan.DataSetReference,
                MemberReferences = qualifiedReferences,
                ProfilePath = loaded.FilePath,
                EvidenceLines = evidence
            };
        }

        var freshRcb = finalAvailability.ReportControls.SingleOrDefault();
        if (!DynamicReportActivationCommissioningService.IsFreshUrcbSafeForG24(freshRcb, out var freshReason))
        {
            evidence.Add("G2.4 final URCB rejected: " + freshReason);
            var fieldRestore = await RestoreProofFieldLeaseAsync(auxiliary, fieldLease, evidence, "final-revalidation-reject").ConfigureAwait(false);
            return new DynamicReportActivationCommissioningResult
            {
                IsSuccess = false,
                CleanupSucceeded = fieldRestore,
                Summary = fieldRestore
                    ? "The selected URCB did not remain strict-proof eligible after temporary field configuration. Exact field restore passed."
                    : "The selected URCB failed final strict-proof revalidation and exact field restore was not fully proven.",
                Identity = identity,
                InputProfile = profile,
                RcbReference = selectedRcb.Reference,
                DataSetReference = plan.DataSetReference,
                MemberReferences = qualifiedReferences,
                ProfilePath = loaded.FilePath,
                EvidenceLines = evidence
            };
        }

        ApplyFreshSnapshot(plan.ReportControl!, freshRcb!);
        // The transactional engine direct write/readback is stronger than inconsistent
        // GetNameList child advertisement. Preserve that proven writability for the
        // monitor's defensive attribute gate.
        EnsureAttribute(plan.ReportControl!, "TrgOps");
        EnsureAttribute(plan.ReportControl!, "OptFlds");
        plan.ReportControl!.TriggerOptions = TemporaryTriggerOptions;
        plan.ReportControl.OptionalFields = TemporaryOptionalFields;
        selectedRcb.TriggerOptions = TemporaryTriggerOptions;
        selectedRcb.OptionalFields = TemporaryOptionalFields;

        evidence.Add($"G2.4 final URCB PASS: {freshRcb!.Reference}; probe={freshRcb.DataSetProbeState}; availability={freshRcb.Availability}; RptEna={TextOrDash(freshRcb.EnabledState)}; Resv={TextOrDash(freshRcb.ReservationState)}; DatSet={TextOrDash(freshRcb.DataSetReference)}; RptID={TextOrDash(freshRcb.ReportId)}; TrgOps={TextOrDash(freshRcb.TriggerOptions)}; OptFlds={TextOrDash(freshRcb.OptionalFields)}");

        ArMms.MmsPersistentReportMonitorAttemptResult attempt;
        try
        {
            // Do NOT issue GI during start. First enable and register the persistent monitor;
            // GI is sent only by the receive slice below so the first proof report cannot race
            // ahead of report routing.
            attempt = await auxiliary.StartPersistentReportMonitorWithAttemptEvidenceAsync(
                plan,
                triggerGeneralInterrogation: false,
                deleteDynamicDataSetOnStop: true,
                directory: discovery.IedDirectory,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
        {
            evidence.Add($"G2.4 activation exception: {ex.GetType().Name}: {ex.Message}");
            var fieldRestore = await RestoreProofFieldLeaseAsync(auxiliary, fieldLease, evidence, "activation-exception").ConfigureAwait(false);
            return new DynamicReportActivationCommissioningResult
            {
                IsSuccess = false,
                CleanupSucceeded = false,
                Summary = fieldRestore
                    ? "G2.4 activation threw before a monitor session was returned. Proof fields were restored, but other dynamic mutation cleanup is not proven; inspect the IED from a fresh association."
                    : "G2.4 activation threw and proof-field restore was not fully proven. Inspect the IED from a fresh association before retry.",
                Identity = identity,
                InputProfile = profile,
                RcbReference = selectedRcb.Reference,
                DataSetReference = plan.DataSetReference,
                MemberReferences = qualifiedReferences,
                ProfilePath = loaded.FilePath,
                EvidenceLines = evidence
            };
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

            var fieldRestore = await RestoreProofFieldLeaseAsync(auxiliary, fieldLease, evidence, "failed-start").ConfigureAwait(false);
            var combinedCleanup = attempt.CleanupSucceeded && fieldRestore;

            return new DynamicReportActivationCommissioningResult
            {
                IsSuccess = false,
                CleanupSucceeded = combinedCleanup,
                Summary = combinedCleanup
                    ? "G2.4 one-URCB activation did not complete. Failed-start rollback plus exact TrgOps/OptFlds restore passed; the profile remains EnvelopeQualified."
                    : "G2.4 activation failed and complete rollback (monitor + proof fields) was not fully proven. Inspect the IED from a fresh association.",
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
        var monitorCleanupSucceeded = false;
        var fieldRestoreSucceeded = false;

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

                foreach (var frame in receive.Reports)
                {
                    var validation = DynamicReportActivationCommissioningService.ValidateInformationReportFrame(
                        frame,
                        selectedRcb.ReportId,
                        plan.DataSetReference,
                        qualifiedReferences);
                    evidence.Add($"G2.4 report candidate: rptId={TextOrDash(frame.Header.ReportId)}; dataset={TextOrDash(frame.Header.DataSetReference)}; decoder={frame.DecoderMode}; values={frame.Values.Count}; included=[{string.Join(",", frame.IncludedDataSetIndexes)}]; valid={validation.IsSuccess}; reason={validation.Reason}");
                    if (!validation.IsSuccess)
                        continue;

                    informationProof = new ArMms.MmsDynamicInformationReportProof
                    {
                        EvidenceId = $"arsas-g2.4-report-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}",
                        ObservedAtUtc = frame.ReceivedAt,
                        RcbReference = selectedRcb.Reference,
                        DataSetReference = plan.DataSetReference,
                        MemberReferences = qualifiedReferences,
                        Kind = validation.Kind,
                        ActualInformationReportReceived = true,
                        ReportIdentityVerified = true,
                        ExactMemberMappingVerified = true,
                        AssociationHealthyAfterReport = auxiliary.IsMmsInitiated,
                        ReportAuthoritativePointCount = validation.AuthoritativePointCount
                    };
                    evidence.Add($"G2.4 InformationReport proof: success={informationProof.IsSuccess}; kind={informationProof.Kind}; actual={informationProof.ActualInformationReportReceived}; identity={informationProof.ReportIdentityVerified}; exactMapping={informationProof.ExactMemberMappingVerified}; authoritativePoints={informationProof.ReportAuthoritativePointCount}; associationHealthy={informationProof.AssociationHealthyAfterReport}");
                    break;
                }

                if (informationProof is null)
                    evidence.Add("G2.4 InformationReport proof: success=false; no received frame satisfied strict RptID + DatSet + full exact ordered member mapping requirements. RptEna/GI acceptance is not treated as report proof.");
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
                monitorCleanupSucceeded = stop.IsSuccess;
                AppendWriteSteps(evidence, "G2.4 monitor cleanup", stop.WriteSteps);
                evidence.Add($"G2.4 monitor cleanup: success={stop.IsSuccess}; sessionState={auxiliary.State}; result={stop.Message}");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
            {
                monitorCleanupSucceeded = false;
                evidence.Add($"G2.4 monitor cleanup exception: {ex.GetType().Name}: {ex.Message}");
            }

            // Proof fields are restored only after the monitor has been stopped/disabled
            // and DatSet/Resv cleanup has been attempted.
            fieldRestoreSucceeded = await RestoreProofFieldLeaseAsync(
                auxiliary,
                fieldLease,
                evidence,
                "post-monitor").ConfigureAwait(false);
        }

        var cleanupSucceeded = monitorCleanupSucceeded && fieldRestoreSucceeded;
        evidence.Add($"G2.4 combined cleanup: monitor={monitorCleanupSucceeded}; proofFields={fieldRestoreSucceeded}; success={cleanupSucceeded}");

        if (!cleanupSucceeded)
        {
            return new DynamicReportActivationCommissioningResult
            {
                IsSuccess = false,
                CleanupSucceeded = false,
                Summary = "G2.4 active proof ended without complete cleanup proof. Profile was deliberately NOT advanced; inspect RptEna/DatSet/Resv/TrgOps/OptFlds from a fresh association before retry.",
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
            evidence.Add("G2.4 profile unchanged after complete cleanup: " + why);
            return new DynamicReportActivationCommissioningResult
            {
                IsSuccess = false,
                CleanupSucceeded = true,
                Summary = "G2.4 complete cleanup passed, but actual strict InformationReport proof did not. The persisted profile remains EnvelopeQualified and production dynamic reporting remains OFF.",
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

        ArMms.MmsDynamicReportQualificationProfile finalProfile;
        try
        {
            var activatedProfile = ArMms.MmsDynamicReportQualificationProfilePolicy.RecordRcbActivationProof(
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
                Summary = "Physical G2.4 activation/report evidence passed and all cleanup passed, but the identity-bound profile transition could not be persisted. Production dynamic reporting remains OFF.",
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
            Summary = $"G2.4 PASS: one fresh URCB delivered an actual strictly mapped InformationReport for {qualifiedReferences.Length} qualified member(s), monitor cleanup plus exact TrgOps/OptFlds restore passed, and the identity-bound profile advanced to {finalProfile.State}. Production automatic dynamic reporting remains OFF.",
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

    internal static ArMms.MmsReportControlCandidate? SelectQualifiedUrcbFromFreshAvailability(
        ArMms.MmsRcbAvailabilityResult availability,
        ArMms.MmsReportInventory inventory,
        string preferredLogicalDevice,
        out ArMms.MmsRcbAvailabilitySnapshot? selectedSnapshot,
        out string reason,
        out IReadOnlyList<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(inventory);

        var urcbSnapshots = availability.ReportControls
            .Where(snapshot => !snapshot.Buffered)
            .ToArray();
        var provenEmpty = urcbSnapshots.Count(snapshot =>
            snapshot.DataSetProbeState == ArMms.MmsRcbDataSetProbeState.ReadSucceeded &&
            string.IsNullOrWhiteSpace(snapshot.DataSetReference));

        var evaluated = urcbSnapshots
            .Select(snapshot =>
            {
                var safe = IsLeaseableFreeUrcbForG24(snapshot, out var why);
                return new { Snapshot = snapshot, Safe = safe, Why = why };
            })
            .ToArray();

        diagnostics = evaluated
            .OrderByDescending(item => item.Safe)
            .ThenByDescending(item => item.Snapshot.Domain.Equals(preferredLogicalDevice, StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => item.Snapshot.Reference, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .Select(item =>
                $"ref={item.Snapshot.Reference}; leaseable={item.Safe}; availability={item.Snapshot.Availability}; probe={item.Snapshot.DataSetProbeState}; DatSet={TextOrDash(item.Snapshot.DataSetReference)}; RptEna={TextOrDash(item.Snapshot.EnabledState)}; Resv={TextOrDash(item.Snapshot.ReservationState)}; Owner={TextOrDash(item.Snapshot.Owner)}; RptID={TextOrDash(item.Snapshot.ReportId)}; currentTrgOps={TextOrDash(item.Snapshot.TriggerOptions)}; currentOptFlds={TextOrDash(item.Snapshot.OptionalFields)}; reason={item.Why}")
            .ToArray();

        var selected = evaluated
            .Where(item => item.Safe)
            .OrderByDescending(item => item.Snapshot.Domain.Equals(preferredLogicalDevice, StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => item.Snapshot.Reference, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (selected is null)
        {
            selectedSnapshot = null;
            reason = $"URCB total={urcbSnapshots.Length}; forced-live empty DatSet={provenEmpty}; transactionalLeaseEligible=0. Selection requires a proven-empty/free URCB and usable RptID; current GI/DataSetName bits may be temporarily leased only after exact original capture.";
            return null;
        }

        var candidate = inventory.ReportControls.FirstOrDefault(rcb => SameReference(rcb.Reference, selected.Snapshot.Reference));
        if (candidate is null)
        {
            selectedSnapshot = null;
            reason = $"Forced-live URCB {selected.Snapshot.Reference} passed lease gates but could not be mapped back to the exact discovered RCB identity.";
            return null;
        }

        selectedSnapshot = selected.Snapshot;
        reason = $"selected={candidate.Reference}; sameLD={candidate.Domain.Equals(preferredLogicalDevice, StringComparison.OrdinalIgnoreCase)}; forcedLiveProbe={selected.Snapshot.DataSetProbeState}; availability={selected.Snapshot.Availability}; RptID={TextOrDash(selected.Snapshot.ReportId)}; currentTrgOps={TextOrDash(selected.Snapshot.TriggerOptions)}; currentOptFlds={TextOrDash(selected.Snapshot.OptionalFields)}; transactionalProofFields=true";
        return candidate;
    }

    internal static bool IsLeaseableFreeUrcbForG24(ArMms.MmsRcbAvailabilitySnapshot snapshot, out string reason)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Buffered)
        {
            reason = "G2.4 first proof permits URCB only.";
            return false;
        }

        if (snapshot.DataSetProbeState != ArMms.MmsRcbDataSetProbeState.ReadSucceeded ||
            !string.IsNullOrWhiteSpace(snapshot.DataSetReference))
        {
            reason = "Live DatSet must be positively read and empty before any temporary proof-field mutation.";
            return false;
        }

        if (ParseBool(snapshot.EnabledState) != false)
        {
            reason = $"RptEna is not explicit false: {TextOrDash(snapshot.EnabledState)}";
            return false;
        }

        if (snapshot.Attributes.Contains("Resv", StringComparer.OrdinalIgnoreCase) &&
            ParseBool(snapshot.ReservationState) != false)
        {
            reason = $"URCB Resv is not explicit false: {TextOrDash(snapshot.ReservationState)}";
            return false;
        }

        if (ParseUnsigned(snapshot.ReservationTimeSeconds) is > 0)
        {
            reason = $"Reservation time is positive: {snapshot.ReservationTimeSeconds}";
            return false;
        }

        if (HasOwner(snapshot.Owner))
        {
            reason = $"URCB Owner is non-empty: {snapshot.Owner}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(snapshot.ReportId))
        {
            reason = "RptID is empty, so an actual report cannot be tied to this exact URCB safely.";
            return false;
        }

        reason = $"Live URCB is empty/free with usable RptID. Current TrgOps={TextOrDash(snapshot.TriggerOptions)} and OptFlds={TextOrDash(snapshot.OptionalFields)} may be temporarily configured only through an exact capture/write/readback/restore lease.";
        return true;
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

    private static async Task<bool> RestoreProofFieldLeaseAsync(
        ArMms.MmsClientSession auxiliary,
        ArMms.MmsDynamicRcbCommissioningFieldLease fieldLease,
        ICollection<string> evidence,
        string label)
    {
        try
        {
            var restore = await auxiliary.RestoreDynamicRcbCommissioningFieldsAsync(
                fieldLease,
                CancellationToken.None).ConfigureAwait(false);
            AppendWriteSteps(evidence, $"G2.4 proof-field restore/{label}", restore.WriteSteps);
            foreach (var line in restore.Evidence)
                evidence.Add($"G2.4 proof-field restore/{label}: {line}");
            evidence.Add($"G2.4 proof-field restore/{label}: success={restore.IsSuccess}; result={restore.Message}");
            return restore.IsSuccess;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
        {
            evidence.Add($"G2.4 proof-field restore/{label} exception: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static bool SuccessfulStep(IEnumerable<ArMms.MmsReportAttributeWriteStep> steps, string attribute)
        => steps.Any(step => step.Attempted && step.IsSuccess && step.Attribute.Equals(attribute, StringComparison.OrdinalIgnoreCase));

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
