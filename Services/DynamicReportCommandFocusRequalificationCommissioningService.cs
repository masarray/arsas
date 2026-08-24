using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

internal sealed class DynamicReportCommandFocusRequalificationAssessment
{
    public bool IsSuccess { get; init; }
    public bool RequiresRequalification { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();
}

internal sealed class DynamicReportCommandFocusRequalificationResult
{
    public bool IsSuccess { get; init; }
    public bool IsBlocked { get; init; }
    public bool LiveProfileReplaced { get; init; }
    public bool FreshCleanupClosureSucceeded { get; init; }
    public string Summary { get; init; } = string.Empty;
    public ArMms.MmsDynamicReportQualificationProfile? OriginalProfile { get; init; }
    public ArMms.MmsDynamicReportQualificationProfile? SavedProfile { get; init; }
    public DynamicReportActivationCommissioningResult? ActivationResult { get; init; }
    public DynamicReportCleanupClosureCommissioningResult? CleanupClosureResult { get; init; }
    public IReadOnlyList<string> QualifiedMemberReferences { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Field-discovered G2.6-P1 recovery path for an InformationReportProven profile whose
/// exact member envelope cannot witness any existing ARSAS command.
///
/// The live profile is treated as immutable until a completely separate staging profile
/// has passed all of the following:
///   1. exact command-status discovery + direct read validation;
///   2. explicit dynamic NamedVariableList qualification with cleanup continuity;
///   3. G2.4 V2 one-URCB activation + actual InformationReport proof;
///   4. G2.4-C fresh-association read-only cleanup closure.
///
/// Staging uses a private temporary profile-store root. Only after every stage succeeds,
/// and after the live profile is re-read to prove it did not change concurrently, is the
/// new InformationReportProven profile atomically moved into the normal store. This
/// service never executes a control command and can never mark ProductionEligible.
/// </summary>
internal sealed class DynamicReportCommandFocusRequalificationCommissioningService
{
    private const int MaximumCommandFocusMembers = DynamicReportActivationCommissioningService.MaximumG24Members;
    private static readonly TimeSpan AuxiliaryAssociationTimeout = TimeSpan.FromSeconds(10);

    private readonly DynamicReportQualificationProfileStore _liveProfileStore;

    public DynamicReportCommandFocusRequalificationCommissioningService(
        DynamicReportQualificationProfileStore? liveProfileStore = null)
    {
        _liveProfileStore = liveProfileStore ?? new DynamicReportQualificationProfileStore();
    }

    public async Task<DynamicReportCommandFocusRequalificationAssessment> AssessAsync(
        Iec61850MonitorDevice device,
        IReadOnlyList<SignalDefinition> fullModelSignals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fullModelSignals);

        var evidence = new List<string>
        {
            "G2.6-P1 recovery assessment: READ ONLY; no DataSet/RCB/profile/control mutation is permitted."
        };

        ArMms.MmsDynamicReportIedIdentity identity;
        try
        {
            identity = DynamicReportQualificationIdentity.Build(device, fullModelSignals);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return AssessmentFailure("Command-focus recovery identity preflight failed: " + ex.Message, evidence);
        }

        var loaded = await _liveProfileStore.LoadAsync(identity, cancellationToken).ConfigureAwait(false);
        evidence.Add($"Recovery assessment profile: exists={loaded.Exists}; valid={loaded.IsValid}; state={loaded.Profile?.State.ToString() ?? "-"}; reason={loaded.Reason}");
        if (!IsInformationReportProven(loaded.Profile) || !loaded.IsValid)
        {
            return AssessmentFailure(
                "Command-focus recovery requires the exact identity-compatible InformationReportProven profile.",
                evidence);
        }

        var commandSignals = GetCommandSignals(fullModelSignals);
        if (commandSignals.Length == 0)
            return AssessmentFailure("No live ARSAS control object exposes ControlStatusReference.", evidence);
        if (commandSignals.Any(signal => signal.ControlCommandBusy))
            return AssessmentFailure("A control command is already in progress; recovery assessment must be performed while controls are idle.", evidence);

        await using var session = new ArMms.MmsClientSession();
        try
        {
            await session.ConnectAsync(
                device.IpAddress,
                device.Port,
                AuxiliaryAssociationTimeout,
                cancellationToken).ConfigureAwait(false);
            var discovery = await session.DiscoverAsync(
                probeReportAttributes: false,
                maxReportAttributeProbes: 0,
                cancellationToken: cancellationToken,
                readDataSetDirectories: false,
                maxDataSetDirectoryReads: 0).ConfigureAwait(false);

            var qualifiedReferences = loaded.Profile!.RcbActivationProof!.MemberReferences.ToArray();
            if (!DynamicReportActivationCommissioningService.TryResolveExactQualifiedMembers(
                    discovery.IedDirectory,
                    qualifiedReferences,
                    out _,
                    out var memberReason))
            {
                evidence.Add("Recovery assessment exact member resolution failed: " + memberReason);
                return AssessmentFailure("The existing InformationReportProven envelope no longer resolves exactly on the live IED.", evidence);
            }

            var eligible = DynamicReportCommandBoundDataChangeCommissioningService.BuildEligibleCommandTargets(
                discovery.IedDirectory,
                commandSignals,
                qualifiedReferences,
                evidence);
            if (eligible.Count > 0)
            {
                evidence.Add("Recovery assessment: existing envelope already has command-focus intersection: " +
                             string.Join(" | ", eligible.Select(item => item.Signal.ObjectReference)));
                return new DynamicReportCommandFocusRequalificationAssessment
                {
                    IsSuccess = true,
                    RequiresRequalification = false,
                    Summary = "The existing InformationReportProven envelope already contains an eligible ARSAS command-focus status member; no requalification is required.",
                    EvidenceLines = evidence.ToArray()
                };
            }

            evidence.Add("Recovery assessment: zero existing command-focus intersections. Live profile remains untouched.");
            return new DynamicReportCommandFocusRequalificationAssessment
            {
                IsSuccess = true,
                RequiresRequalification = true,
                Summary = "The existing InformationReportProven envelope cannot witness an ARSAS command. Transactional command-focus requalification is required before deterministic A3 can arm.",
                EvidenceLines = evidence.ToArray()
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException)
        {
            evidence.Add($"Recovery assessment exception: {ex.GetType().Name}: {ex.Message}");
            return AssessmentFailure("The read-only recovery assessment could not complete.", evidence);
        }
    }

    public async Task<DynamicReportCommandFocusRequalificationResult> RunAsync(
        Iec61850MonitorDevice device,
        IReadOnlyList<SignalDefinition> fullModelSignals,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fullModelSignals);

        var evidence = new List<string>
        {
            "G2.6-P1 command-focus requalification contract: stage everything away from the live profile, prove activation/report/cleanup completely, then atomically replace only with InformationReportProven.",
            "G2.6-P1 recovery control safety: ZERO control execution. Existing ARSAS SBO/SBOw/Operate path is not called, wrapped, delayed or re-issued.",
            "G2.6-P1 recovery production safety: ProductionEligible is forbidden; production automatic dynamic reporting remains OFF."
        };

        ArMms.MmsDynamicReportIedIdentity identity;
        try
        {
            identity = DynamicReportQualificationIdentity.Build(device, fullModelSignals);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Blocked("Command-focus requalification identity preflight failed: " + ex.Message, evidence);
        }

        var originalLoad = await _liveProfileStore.LoadAsync(identity, cancellationToken).ConfigureAwait(false);
        if (!originalLoad.IsValid || !IsInformationReportProven(originalLoad.Profile))
        {
            evidence.Add($"Live profile rejected: exists={originalLoad.Exists}; valid={originalLoad.IsValid}; state={originalLoad.Profile?.State.ToString() ?? "-"}; reason={originalLoad.Reason}");
            return Blocked("Transactional command-focus recovery requires the exact existing InformationReportProven profile.", evidence, originalLoad.Profile);
        }

        var originalProfile = originalLoad.Profile!;
        var commandSignals = GetCommandSignals(fullModelSignals);
        if (commandSignals.Length == 0)
            return Blocked("No ARSAS control object exposes ControlStatusReference; recovery will not guess a status point.", evidence, originalProfile);
        if (commandSignals.Any(signal => signal.ControlCommandBusy))
            return Blocked("A control command is already in progress. Recovery must complete before the one A3 test command.", evidence, originalProfile);

        var stagingRoot = Path.Combine(Path.GetTempPath(), "ARSAS", "g26-p1-command-focus-" + Guid.NewGuid().ToString("N"));
        try
        {
            progress?.Report("G2.6-P1 recovery: discovering exact command-status points and qualifying a staging-only dynamic DataSet…");
            var envelope = await BuildStagedEnvelopeAsync(
                device,
                fullModelSignals,
                commandSignals,
                identity,
                evidence,
                cancellationToken).ConfigureAwait(false);
            if (!envelope.IsSuccess || envelope.Profile is null)
            {
                return Failed(
                    envelope.Summary,
                    evidence,
                    originalProfile,
                    envelope.MemberReferences);
            }

            var stagingStore = new DynamicReportQualificationProfileStore(stagingRoot);
            await stagingStore.SaveAsync(envelope.Profile, cancellationToken).ConfigureAwait(false);
            evidence.Add($"Staging profile persisted outside live store: state={envelope.Profile.State}; members={envelope.Profile.ProvenSafeMemberCount}; liveProfileTouched=false");

            progress?.Report("G2.6-P1 recovery: staging envelope qualified; proving one-URCB activation + actual InformationReport without touching the live profile…");
            var activationService = new DynamicReportActivationCommissioningServiceV2(stagingStore);
            var activation = await activationService.RunAsync(
                device,
                fullModelSignals,
                cancellationToken).ConfigureAwait(false);
            evidence.Add("Staged G2.4 V2: " + activation.Summary);
            evidence.AddRange(activation.EvidenceLines.Select(line => "staged/G2.4: " + line));

            if (!activation.IsSuccess || !activation.CleanupSucceeded || !IsInformationReportProven(activation.SavedProfile))
            {
                return Failed(
                    "Staged command-focus G2.4 did not close activation + actual InformationReport + cleanup. The original live InformationReportProven profile was not changed.",
                    evidence,
                    originalProfile,
                    envelope.MemberReferences,
                    activation);
            }

            progress?.Report("G2.6-P1 recovery: staged report proof passed; opening a fresh READ-ONLY association to close RCB/DataSet cleanup…");
            var closureService = new DynamicReportCleanupClosureCommissioningService(stagingStore);
            var closure = await closureService.RunAsync(
                device,
                fullModelSignals,
                cancellationToken).ConfigureAwait(false);
            evidence.Add("Staged G2.4-C: " + closure.Summary);
            evidence.AddRange(closure.EvidenceLines.Select(line => "staged/G2.4-C: " + line));

            if (!closure.IsSuccess)
            {
                return Failed(
                    "Staged command-focus report proof passed, but fresh-association cleanup closure did not. The original live profile remains untouched.",
                    evidence,
                    originalProfile,
                    envelope.MemberReferences,
                    activation,
                    closure);
            }

            var finalProfile = activation.SavedProfile!;
            if (!FinalProfileMatchesCommandFocus(finalProfile, envelope.CommandStatusReferences, out var finalReason))
            {
                evidence.Add("Final staged profile rejected: " + finalReason);
                return Failed(
                    "Staged proof completed but the resulting InformationReportProven profile lost the exact command-focus member invariant. Live profile was not changed.",
                    evidence,
                    originalProfile,
                    envelope.MemberReferences,
                    activation,
                    closure);
            }

            // Optimistic concurrency gate: a long physical staging transaction must never
            // overwrite a live qualification profile that another commissioning action
            // changed while this recovery was running.
            var currentLoad = await _liveProfileStore.LoadAsync(identity, cancellationToken).ConfigureAwait(false);
            if (!currentLoad.IsValid || currentLoad.Profile is null || !SameProfileEvidence(originalProfile, currentLoad.Profile))
            {
                evidence.Add("Live profile concurrency gate failed: the persisted evidence changed during staging. No replacement was attempted.");
                return Failed(
                    "The live qualification profile changed while command-focus staging was running. Recovery aborted rather than overwrite newer evidence.",
                    evidence,
                    originalProfile,
                    envelope.MemberReferences,
                    activation,
                    closure);
            }

            await _liveProfileStore.SaveAsync(finalProfile, cancellationToken).ConfigureAwait(false);
            evidence.Add($"LIVE PROFILE ATOMIC REPLACEMENT PASS: oldState={originalProfile.State}; newState={finalProfile.State}; rcb={finalProfile.RcbActivationProof?.RcbReference}; members={finalProfile.RcbActivationProof?.MemberReferences.Count}; ProductionEligible=false");
            evidence.Add("G2.6-P1 recovery complete: the new exact InformationReportProven envelope contains command-status evidence; deterministic A3 may now be armed. Production automatic dynamic reporting remains OFF.");

            progress?.Report("G2.6-P1 recovery PASS — command-focus profile is InformationReportProven and cleanup-closed. Re-arming deterministic A3 automatically; DO NOT command until the exact A3 READY marker appears.");
            return new DynamicReportCommandFocusRequalificationResult
            {
                IsSuccess = true,
                LiveProfileReplaced = true,
                FreshCleanupClosureSucceeded = true,
                Summary = "G2.6-P1 command-focus requalification PASS: a staging-only envelope passed dynamic DataSet qualification, one-URCB actual InformationReport proof and fresh cleanup closure; only then was the live profile atomically replaced at InformationReportProven. A3 can be re-armed; ProductionEligible remains OFF.",
                OriginalProfile = originalProfile,
                SavedProfile = finalProfile,
                ActivationResult = activation,
                CleanupClosureResult = closure,
                QualifiedMemberReferences = envelope.MemberReferences,
                EvidenceLines = evidence.ToArray()
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException or UnauthorizedAccessException or ArgumentException)
        {
            evidence.Add($"G2.6-P1 recovery exception: {ex.GetType().Name}: {ex.Message}");
            return Failed(
                "Transactional command-focus requalification stopped before atomic live-profile replacement. The previous InformationReportProven profile remains authoritative.",
                evidence,
                originalProfile);
        }
        finally
        {
            TryDeleteStagingRoot(stagingRoot, evidence);
        }
    }

    private static async Task<StagedEnvelopeResult> BuildStagedEnvelopeAsync(
        Iec61850MonitorDevice device,
        IReadOnlyList<SignalDefinition> fullModelSignals,
        IReadOnlyList<SignalDefinition> commandSignals,
        ArMms.MmsDynamicReportIedIdentity identity,
        ICollection<string> evidence,
        CancellationToken cancellationToken)
    {
        await using var session = new ArMms.MmsClientSession();
        try
        {
            await session.ConnectAsync(
                device.IpAddress,
                device.Port,
                AuxiliaryAssociationTimeout,
                cancellationToken).ConfigureAwait(false);
            evidence.Add($"Recovery staging association ready: state={session.State}; localTcpAddress={TextOrDash(session.LocalTcpAddress)}");

            var discovery = await session.DiscoverAsync(
                probeReportAttributes: false,
                maxReportAttributeProbes: 0,
                cancellationToken: cancellationToken,
                readDataSetDirectories: false,
                maxDataSetDirectoryReads: 0).ConfigureAwait(false);
            evidence.Add("Recovery staging discovery: " + discovery.Summary);

            var statusPoints = DynamicReportCommandBoundStimulusWitnessService.ResolveCommandStatusPoints(
                discovery.IedDirectory,
                commandSignals,
                evidence);
            if (statusPoints.Count == 0)
                return StagedEnvelopeResult.Fail("No ControlStatusReference resolved to a live ST/stVal MMS point.");

            var candidates = SelectCommandFocusCandidates(discovery.IedDirectory, statusPoints)
                .Take(MaximumCommandFocusMembers)
                .ToArray();
            if (candidates.Length < 2)
            {
                evidence.Add("Recovery staging candidates: " + string.Join(" | ", candidates.Select(point => point.UserReference)));
                return StagedEnvelopeResult.Fail("Command-focus recovery requires at least two bounded ST/stVal candidates so the G2.3 multi-member envelope gate is not weakened.");
            }

            var validated = new List<ArMms.MmsObjectReference>();
            var validatedMms = new List<string>();
            foreach (var point in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await session.ReadSingleVariableAsync(point.ToObjectReference(), cancellationToken).ConfigureAwait(false);
                evidence.Add($"Recovery direct-read candidate: ref={point.UserReference}; mms={point.MmsReference}; success={read.IsSuccess}; result={read.Message}");
                if (!read.IsSuccess)
                {
                    if (!session.IsMmsInitiated)
                        return StagedEnvelopeResult.Fail("The staging association was lost during direct-read validation.");
                    continue;
                }

                validated.Add(point.ToObjectReference());
                validatedMms.Add(point.MmsReference);
            }

            if (validated.Count < 2)
                return StagedEnvelopeResult.Fail("Fewer than two command-focus candidates passed exact direct MMS-read validation.");

            var commandStatusReferences = statusPoints.Values
                .Select(point => point.MmsReference)
                .Where(status => validatedMms.Any(candidate => SameMms(candidate, status)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (commandStatusReferences.Length == 0)
                return StagedEnvelopeResult.Fail("Direct-read validation removed every exact ControlStatusReference; recovery will not qualify a status envelope by inference.");

            var dataSetReference = BuildTemporaryDataSetReference(validated[0].Domain);
            evidence.Add($"Recovery qualification dataset={dataSetReference}; candidates={validated.Count}; commandStatusCandidates={commandStatusReferences.Length}; liveProfileTouched=false");

            var coordinator = await session.RunDynamicDataSetQualificationCommissioningAsync(
                dataSetReference,
                validated,
                new ArMms.MmsDynamicDataSetQualificationCoordinatorOptions
                {
                    ExecutionMode = ArMms.MmsDynamicDataSetQualificationExecutionMode.ExplicitCommissioning,
                    MaxAttempts = 16,
                    LocalizeFailedBatch = true,
                    Ladder = new ArMms.MmsDynamicDataSetQualificationLadderOptions
                    {
                        Milestones = [1, 4, 8],
                        ApplicationSafetyMemberLimit = MaximumCommandFocusMembers,
                        IncludeTerminalCandidateCount = true
                    },
                    Probe = new ArMms.MmsDynamicDataSetQualificationProbeOptions
                    {
                        ApplicationSafetyMemberLimit = MaximumCommandFocusMembers,
                        RejectKnownNegotiatedPduOverflow = true
                    }
                },
                discovery.IedDirectory,
                cancellationToken).ConfigureAwait(false);

            evidence.Add("Recovery qualification coordinator: " + coordinator.Summary);
            foreach (var attempt in coordinator.Attempts)
            {
                evidence.Add($"Recovery qualification attempt {attempt.AttemptId}: members={attempt.MemberCount}; success={attempt.IsQualificationSuccess}; associationSurvived={attempt.AssociationSurvived}; cleanup={attempt.CleanupSucceeded}; stage={attempt.FailureStage}");
            }
            evidence.AddRange(coordinator.Warnings.Select(warning => "Recovery qualification warning: " + warning));

            if (coordinator.RequiresFreshAssociation ||
                !coordinator.Assessment.HasMultiMemberEnvelopeCandidate ||
                string.IsNullOrWhiteSpace(coordinator.EnvelopeCandidateAttemptId))
            {
                return StagedEnvelopeResult.Fail(
                    coordinator.RequiresFreshAssociation
                        ? "Dynamic DataSet qualification did not prove association/cleanup continuity."
                        : "Dynamic DataSet qualification did not produce a cleanup-safe multi-member envelope.");
            }

            var acceptedEnvelope = ArMms.MmsDynamicDataSetQualificationLadder.AcceptExactEnvelope(
                coordinator.Assessment,
                coordinator.EnvelopeCandidateAttemptId);
            var profile = ArMms.MmsDynamicReportQualificationProfilePolicy.CreateEnvelopeQualifiedProfile(
                identity,
                acceptedEnvelope,
                coordinator.Assessment,
                capacityEvidence: null,
                sourceEvidenceId: $"arsas-g2.6-p1-command-focus-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}",
                nowUtc: DateTimeOffset.UtcNow);

            var accepted = profile.AcceptedEnvelope?.ExactProvenMemberReferences?.ToArray() ?? Array.Empty<string>();
            var acceptedStatuses = commandStatusReferences.Where(status => accepted.Any(member => SameMms(member, status))).ToArray();
            if (acceptedStatuses.Length == 0)
            {
                return StagedEnvelopeResult.Fail("The accepted exact envelope did not retain any exact ControlStatusReference member.");
            }

            evidence.Add($"Recovery staged EnvelopeQualified PASS: members={accepted.Length}; exactCommandStatuses={acceptedStatuses.Length}; state={profile.State}; liveProfileTouched=false");
            evidence.Add("Recovery staged exact members: " + string.Join(" | ", accepted));
            return new StagedEnvelopeResult
            {
                IsSuccess = true,
                Summary = "Command-focus dynamic DataSet envelope qualified in staging.",
                Profile = profile,
                MemberReferences = accepted,
                CommandStatusReferences = acceptedStatuses
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException or ArgumentException)
        {
            evidence.Add($"Recovery staging qualification exception: {ex.GetType().Name}: {ex.Message}");
            return StagedEnvelopeResult.Fail("Command-focus staging qualification ended on a transport/protocol/policy exception.");
        }
    }

    private static IReadOnlyList<ArMms.MmsFcResolvedPoint> SelectCommandFocusCandidates(
        ArMms.MmsIedModelDirectory directory,
        IReadOnlyDictionary<SignalDefinition, ArMms.MmsFcResolvedPoint> statusPoints)
    {
        var result = new List<ArMms.MmsFcResolvedPoint>();

        // Exact ControlStatusReference values come first. With multiple live controls this
        // makes the bounded G2.4 envelope useful for more than one normal ARSAS command.
        foreach (var pair in statusPoints.OrderBy(item => item.Key.ObjectReference, StringComparer.OrdinalIgnoreCase))
            AddDistinct(result, pair.Value);

        // Then add the same A2.1 focus chain used by the physical command witness. This
        // naturally adds XCBR/CSWI/XSWI Pos.stVal corroboration when the IED exposes it.
        foreach (var pair in statusPoints.OrderBy(item => item.Key.ObjectReference, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var point in DynamicReportCommandBoundStimulusWitnessService.BuildFocusChain(directory, pair.Value))
            {
                if (!point.FunctionalConstraint.Equals("ST", StringComparison.OrdinalIgnoreCase) ||
                    point.IsControlAttribute || point.IsReportAttribute ||
                    !(point.DataObjectPath.Equals("stVal", StringComparison.OrdinalIgnoreCase) ||
                      point.DataObjectPath.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase)))
                    continue;
                AddDistinct(result, point);
            }
        }

        return result.Take(MaximumCommandFocusMembers).ToArray();
    }

    private static void AddDistinct(List<ArMms.MmsFcResolvedPoint> target, ArMms.MmsFcResolvedPoint point)
    {
        if (target.Any(existing => SameMms(existing.MmsReference, point.MmsReference)))
            return;
        target.Add(point);
    }

    private static bool FinalProfileMatchesCommandFocus(
        ArMms.MmsDynamicReportQualificationProfile profile,
        IReadOnlyList<string> commandStatusReferences,
        out string reason)
    {
        if (!IsInformationReportProven(profile))
        {
            reason = $"final state/proofs are incomplete: state={profile.State}";
            return false;
        }

        var members = profile.RcbActivationProof!.MemberReferences;
        if (!commandStatusReferences.Any(status => members.Any(member => SameMms(member, status))))
        {
            reason = "final G2.4 exact member sequence has no retained command-status member";
            return false;
        }

        if (profile.State == ArMms.MmsDynamicReportQualificationState.ProductionEligible)
        {
            reason = "staging unexpectedly produced ProductionEligible, which is forbidden in P1 recovery";
            return false;
        }

        reason = "exact InformationReportProven command-focus member invariant passed";
        return true;
    }

    private static bool SameProfileEvidence(
        ArMms.MmsDynamicReportQualificationProfile expected,
        ArMms.MmsDynamicReportQualificationProfile current)
    {
        if (expected.State != current.State ||
            !string.Equals(expected.Identity.StableIdentityKey, current.Identity.StableIdentityKey, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expected.Identity.ModelFingerprint, current.Identity.ModelFingerprint, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(expected.RcbActivationProof?.EvidenceId, current.RcbActivationProof?.EvidenceId, StringComparison.Ordinal) ||
            !string.Equals(expected.InformationReportProof?.EvidenceId, current.InformationReportProof?.EvidenceId, StringComparison.Ordinal))
            return false;

        var expectedMembers = expected.RcbActivationProof?.MemberReferences ?? Array.Empty<string>();
        var currentMembers = current.RcbActivationProof?.MemberReferences ?? Array.Empty<string>();
        return expectedMembers.Count == currentMembers.Count &&
               expectedMembers.Zip(currentMembers).All(pair => SameMms(pair.First, pair.Second));
    }

    private static SignalDefinition[] GetCommandSignals(IReadOnlyList<SignalDefinition> signals)
        => signals
            .Where(signal => signal.IsControlSignal && !string.IsNullOrWhiteSpace(signal.ControlStatusReference))
            .Distinct()
            .OrderBy(signal => signal.ObjectReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsInformationReportProven(ArMms.MmsDynamicReportQualificationProfile? profile)
        => profile is not null &&
           profile.State == ArMms.MmsDynamicReportQualificationState.InformationReportProven &&
           profile.RcbActivationProof?.IsSuccess == true &&
           profile.InformationReportProof?.IsSuccess == true;

    private static string BuildTemporaryDataSetReference(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new InvalidOperationException("The first command-focus member has no logical-device domain.");
        return $"{domain.Trim()}/LLN0.ARQ{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
    }

    private static bool SameMms(string? left, string? right)
        => ArMms.MmsFcReferenceNormalizer.NormalizeMmsReference(left ?? string.Empty)
            .Equals(
                ArMms.MmsFcReferenceNormalizer.NormalizeMmsReference(right ?? string.Empty),
                StringComparison.OrdinalIgnoreCase);

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static void TryDeleteStagingRoot(string path, ICollection<string> evidence)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            evidence.Add($"Recovery staging-directory cleanup warning: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static DynamicReportCommandFocusRequalificationAssessment AssessmentFailure(
        string summary,
        IReadOnlyList<string> evidence)
        => new()
        {
            IsSuccess = false,
            Summary = summary + " Production automatic dynamic reporting remains OFF.",
            EvidenceLines = evidence.ToArray()
        };

    private static DynamicReportCommandFocusRequalificationResult Blocked(
        string summary,
        IReadOnlyList<string> evidence,
        ArMms.MmsDynamicReportQualificationProfile? originalProfile = null)
        => new()
        {
            IsBlocked = true,
            Summary = summary + " The existing live profile was not changed; ProductionEligible remains OFF.",
            OriginalProfile = originalProfile,
            EvidenceLines = evidence.ToArray()
        };

    private static DynamicReportCommandFocusRequalificationResult Failed(
        string summary,
        IReadOnlyList<string> evidence,
        ArMms.MmsDynamicReportQualificationProfile? originalProfile,
        IReadOnlyList<string>? members = null,
        DynamicReportActivationCommissioningResult? activation = null,
        DynamicReportCleanupClosureCommissioningResult? closure = null)
        => new()
        {
            IsSuccess = false,
            IsBlocked = false,
            LiveProfileReplaced = false,
            FreshCleanupClosureSucceeded = closure?.IsSuccess == true,
            Summary = summary + " Production automatic dynamic reporting remains OFF.",
            OriginalProfile = originalProfile,
            ActivationResult = activation,
            CleanupClosureResult = closure,
            QualifiedMemberReferences = members ?? Array.Empty<string>(),
            EvidenceLines = evidence.ToArray()
        };

    private sealed class StagedEnvelopeResult
    {
        public bool IsSuccess { get; init; }
        public string Summary { get; init; } = string.Empty;
        public ArMms.MmsDynamicReportQualificationProfile? Profile { get; init; }
        public IReadOnlyList<string> MemberReferences { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> CommandStatusReferences { get; init; } = Array.Empty<string>();

        public static StagedEnvelopeResult Fail(string summary) => new() { Summary = summary };
    }
}