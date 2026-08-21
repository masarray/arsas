using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

internal enum DynamicReportStimulusEligibilityKind
{
    None,
    TransitionObserved,
    MomentaryOrPulse,
    PersistentOrLatched
}

internal sealed class DynamicReportStimulusEligibilityTransition
{
    public string Reference { get; init; } = string.Empty;
    public string MmsReference { get; init; } = string.Empty;
    public string BeforeValue { get; init; } = string.Empty;
    public string AfterValue { get; init; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; init; }
}

internal sealed class DynamicReportStimulusEligibilityObservation
{
    public int Rank { get; init; }
    public int Score { get; init; }
    public bool FastLane { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string MmsReference { get; init; } = string.Empty;
    public string LogicalNode { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string SelectionReason { get; init; } = string.Empty;
    public string BaselineValue { get; init; } = string.Empty;
    public string FinalValue { get; init; } = string.Empty;
    public int TransitionCount { get; init; }
    public DynamicReportStimulusEligibilityKind Kind { get; init; }
    public double? ObservedActiveMilliseconds { get; init; }
    public IReadOnlyList<DynamicReportStimulusEligibilityTransition> Transitions { get; init; } = Array.Empty<DynamicReportStimulusEligibilityTransition>();
}

internal sealed class DynamicReportStimulusEligibilityDiscoveryResult
{
    public bool IsSuccess { get; init; }
    public bool IsBlocked { get; init; }
    public bool BaselineCaptured { get; init; }
    public bool AssociationHealthy { get; init; }
    public bool StimulusEligibilityProven { get; init; }
    public int CandidateCount { get; init; }
    public int FastLaneCount { get; init; }
    public int SampleCycles { get; init; }
    public int ReadFailures { get; init; }
    public string Summary { get; init; } = string.Empty;
    public ArMms.MmsDynamicReportIedIdentity? Identity { get; init; }
    public ArMms.MmsDynamicReportQualificationProfile? InputProfile { get; init; }
    public IReadOnlyList<DynamicReportStimulusEligibilityObservation> Observations { get; init; } = Array.Empty<DynamicReportStimulusEligibilityObservation>();
    public IReadOnlyList<DynamicReportStimulusEligibilityObservation> EligibleCandidates { get; init; } = Array.Empty<DynamicReportStimulusEligibilityObservation>();
    public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();
}

/// <summary>
/// G2.5-A2 read-only physical stimulus eligibility discovery.
///
/// This gate does NOT touch an RCB and does NOT create or mutate a DataSet. It opens
/// one isolated MMS association, discovers live ST/stVal candidates, captures a
/// baseline, then samples a bounded ranked candidate set after an explicit operator
/// READY marker. The output only answers which live MMS status point(s) really change
/// for the physical stimulus and whether the observed state looks persistent/latched
/// or momentary/pulse-like. It cannot advance the dynamic-report profile.
/// </summary>
internal sealed class DynamicReportStimulusEligibilityDiscoveryService
{
    internal const int MaximumCandidates = 24;
    internal const int MaximumFastLaneCandidates = 8;
    internal const int SecondarySweepEveryCycles = 8;
    internal const string ReadyMarker = "G2.5-A2 READY — READ ONLY";
    internal const string TransitionMarker = "G2.5-A2 TRANSITION OBSERVED";

    internal static readonly TimeSpan AssociationTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan ObservationWindow = TimeSpan.FromSeconds(45);
    internal static readonly TimeSpan PostTransitionSettleWindow = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan InterCycleDelay = TimeSpan.FromMilliseconds(5);

    private readonly DynamicReportQualificationProfileStore _profileStore;

    public DynamicReportStimulusEligibilityDiscoveryService(
        DynamicReportQualificationProfileStore? profileStore = null)
    {
        _profileStore = profileStore ?? new DynamicReportQualificationProfileStore();
    }

    public async Task<DynamicReportStimulusEligibilityDiscoveryResult> RunAsync(
        Iec61850MonitorDevice device,
        IReadOnlyList<SignalDefinition> fullModelSignals,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fullModelSignals);

        var evidence = new List<string>
        {
            "G2.5-A2 contract: READ ONLY stimulus eligibility discovery. No RCB read/write, no RptEna, no Resv, no DatSet mutation, no TrgOps/OptFlds write, no GI, no Define/DeleteNamedVariableList, no report monitor, and no profile save.",
            $"G2.5-A2 sampling contract: maxCandidates={MaximumCandidates}; fastLane={MaximumFastLaneCandidates}; secondarySweepEvery={SecondarySweepEveryCycles}; window={ObservationWindow.TotalSeconds:0}s; settleAfterFirstTransition={PostTransitionSettleWindow.TotalSeconds:0}s."
        };

        ArMms.MmsDynamicReportIedIdentity identity;
        try
        {
            identity = DynamicReportQualificationIdentity.Build(device, fullModelSignals);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Blocked("G2.5-A2 identity preflight failed: " + ex.Message, evidence);
        }

        var loaded = await _profileStore.LoadAsync(identity, cancellationToken).ConfigureAwait(false);
        evidence.Add($"G2.5-A2 persisted profile: exists={loaded.Exists}; valid={loaded.IsValid}; state={loaded.Profile?.State.ToString() ?? "-"}; reason={loaded.Reason}");
        if (!loaded.IsValid || loaded.Profile is null ||
            loaded.Profile.State != ArMms.MmsDynamicReportQualificationState.InformationReportProven)
        {
            return Blocked(
                "G2.5-A2 requires the identity-compatible InformationReportProven G2.4 profile. It will not infer eligibility from an unqualified identity.",
                evidence,
                identity,
                loaded.Profile);
        }

        var profile = loaded.Profile;
        evidence.Add($"G2.5-A2 identity: stableKey={identity.StableIdentityKey}; fingerprint={identity.ModelFingerprint}; profileRevision={TextOrDash(identity.ProfileRevision)}");

        await using var session = new ArMms.MmsClientSession();
        try
        {
            await session.ConnectAsync(device.IpAddress, device.Port, AssociationTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException)
        {
            evidence.Add($"G2.5-A2 association failed: {ex.GetType().Name}: {ex.Message}");
            return Failed(
                "G2.5-A2 could not establish the isolated read-only MMS association.",
                evidence,
                identity,
                profile,
                associationHealthy: false);
        }

        evidence.Add($"G2.5-A2 association ready: state={session.State}; localTcpAddress={TextOrDash(session.LocalTcpAddress)}; READ-ONLY=true");

        ArMms.MmsDiscoveryResult discovery;
        try
        {
            discovery = await session.DiscoverAsync(
                probeReportAttributes: false,
                maxReportAttributeProbes: 0,
                cancellationToken: cancellationToken,
                readDataSetDirectories: false,
                maxDataSetDirectoryReads: 0).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
        {
            evidence.Add($"G2.5-A2 discovery failed: {ex.GetType().Name}: {ex.Message}");
            return Failed(
                "G2.5-A2 live model discovery failed before sampling.",
                evidence,
                identity,
                profile,
                session.IsMmsInitiated);
        }

        evidence.Add("G2.5-A2 discovery: " + discovery.Summary);

        var ranked = BuildRankedCandidates(
            discovery.IedDirectory,
            fullModelSignals,
            profile.RcbActivationProof?.RcbReference)
            .Take(MaximumCandidates)
            .ToArray();

        if (ranked.Length == 0)
        {
            evidence.Add("G2.5-A2 candidate selection returned zero readable-intent ST/stVal candidates.");
            return Failed(
                "No bounded ST/stVal stimulus candidates were found in the live MMS model.",
                evidence,
                identity,
                profile,
                session.IsMmsInitiated);
        }

        evidence.Add($"G2.5-A2 ranked candidates: selected={ranked.Length}; liveDirectoryPoints={discovery.IedDirectory.PointCount}");
        foreach (var candidate in ranked)
            evidence.Add($"G2.5-A2 candidate: score={candidate.Score}; ref={candidate.Point.UserReference}; mms={candidate.Point.MmsReference}; reason={candidate.Reason}");

        var trackers = new List<CandidateTracker>();
        var baselineFailures = 0;
        foreach (var candidate in ranked)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await session.ReadSingleVariableAsync(candidate.Point.ToObjectReference(), cancellationToken).ConfigureAwait(false);
            if (!read.IsSuccess || read.Value is null)
            {
                baselineFailures++;
                evidence.Add($"G2.5-A2 baseline read failed: ref={candidate.Point.UserReference}; success={read.IsSuccess}; result={read.Message}");
                continue;
            }

            var value = NormalizeValue(ArMms.MmsDataValueRenderer.ToCompactString(read.Value));
            if (!IsUsableStatusValue(value))
            {
                evidence.Add($"G2.5-A2 baseline candidate skipped as non-scalar/unsupported: ref={candidate.Point.UserReference}; value={value}");
                continue;
            }

            trackers.Add(new CandidateTracker(candidate, value));
            evidence.Add($"G2.5-A2 baseline: ref={candidate.Point.UserReference}; value={value}");
        }

        if (trackers.Count == 0 || !session.IsMmsInitiated)
        {
            return Failed(
                "G2.5-A2 could not capture a usable live baseline for any bounded candidate.",
                evidence,
                identity,
                profile,
                session.IsMmsInitiated,
                candidateCount: ranked.Length,
                readFailures: baselineFailures);
        }

        var fastLane = trackers
            .OrderByDescending(item => item.Candidate.Score)
            .ThenBy(item => item.Candidate.Point.UserReference, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumFastLaneCandidates)
            .ToArray();
        var secondary = trackers
            .Except(fastLane)
            .Take(Math.Max(0, MaximumCandidates - fastLane.Length))
            .ToArray();

        for (var index = 0; index < fastLane.Length; index++)
            fastLane[index].FastLane = true;

        evidence.Add("G2.5-A2 fast lane: " + string.Join(" | ", fastLane.Select(item => item.Candidate.Point.UserReference)));
        if (secondary.Length > 0)
            evidence.Add("G2.5-A2 secondary lane: " + string.Join(" | ", secondary.Select(item => item.Candidate.Point.UserReference)));

        progress?.Report($"{ReadyMarker} — baseline captured on {trackers.Count} candidate(s). NOW perform ONE normal safe OPEN/CLOSE or equivalent physical stimulus. Do not repeat the stimulus; A2 is sampling live status only.");
        evidence.Add($"{ReadyMarker}: baseline complete; operator may perform ONE safe stimulus now.");

        var startedAt = DateTimeOffset.UtcNow;
        var hardDeadline = startedAt + ObservationWindow;
        DateTimeOffset? settleDeadline = null;
        var cycles = 0;
        var readFailures = baselineFailures;
        var transitionAnnounced = false;

        while (DateTimeOffset.UtcNow < hardDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cycles++;

            readFailures += await SampleLaneAsync(session, fastLane, evidence, cancellationToken).ConfigureAwait(false);
            if (cycles % SecondarySweepEveryCycles == 0 && secondary.Length > 0)
                readFailures += await SampleLaneAsync(session, secondary, evidence, cancellationToken).ConfigureAwait(false);

            if (!session.IsMmsInitiated)
            {
                evidence.Add("G2.5-A2 association left MmsInitiated during sampling.");
                break;
            }

            var firstObserved = trackers
                .SelectMany(item => item.Transitions)
                .OrderBy(item => item.ObservedAtUtc)
                .FirstOrDefault();
            if (firstObserved is not null && !transitionAnnounced)
            {
                transitionAnnounced = true;
                settleDeadline = DateTimeOffset.UtcNow + PostTransitionSettleWindow;
                progress?.Report($"{TransitionMarker} — {firstObserved.Reference}: {firstObserved.BeforeValue} → {firstObserved.AfterValue}. No more stimulus; sampling continues briefly to classify pulse vs persistent state.");
                evidence.Add($"{TransitionMarker}: first={firstObserved.Reference}; before={firstObserved.BeforeValue}; after={firstObserved.AfterValue}; settleUntil={settleDeadline:O}");
            }

            if (settleDeadline.HasValue && DateTimeOffset.UtcNow >= settleDeadline.Value)
                break;

            if (InterCycleDelay > TimeSpan.Zero)
                await Task.Delay(InterCycleDelay, cancellationToken).ConfigureAwait(false);
        }

        // One final bounded sweep gives persistent states a final confirmation and
        // gives secondary candidates one last chance to expose a latched change.
        readFailures += await SampleLaneAsync(session, fastLane, evidence, cancellationToken).ConfigureAwait(false);
        if (secondary.Length > 0)
            readFailures += await SampleLaneAsync(session, secondary, evidence, cancellationToken).ConfigureAwait(false);

        var observations = BuildObservations(trackers, DateTimeOffset.UtcNow);
        var eligible = observations
            .Where(item => item.TransitionCount > 0)
            .OrderByDescending(EligibilitySortScore)
            .ThenBy(item => item.Reference, StringComparer.OrdinalIgnoreCase)
            .Select((item, index) => CloneWithRank(item, index + 1))
            .ToArray();

        foreach (var item in eligible)
        {
            evidence.Add($"G2.5-A2 ELIGIBLE: rank={item.Rank}; kind={item.Kind}; ref={item.Reference}; baseline={item.BaselineValue}; final={item.FinalValue}; transitions={item.TransitionCount}; observedActiveMs={FormatMilliseconds(item.ObservedActiveMilliseconds)}; score={item.Score}; reason={item.SelectionReason}");
        }

        if (eligible.Length == 0)
            evidence.Add($"G2.5-A2 result: no candidate transition observed; cycles={cycles}; readFailures={readFailures}; associationHealthy={session.IsMmsInitiated}");

        var healthy = session.IsMmsInitiated;
        var success = healthy && eligible.Length > 0;
        var summary = success
            ? $"G2.5-A2 PASS: read-only sampling proved {eligible.Length} live stimulus-responsive MMS candidate(s). Top candidate is {eligible[0].Reference} ({eligible[0].Kind}); use this evidence to build the narrow A3 dchg proof. Production automatic dynamic reporting remains OFF."
            : "G2.5-A2 did not observe a live candidate transition during the bounded read-only window. Do not advance to A3 or G2.5-B; refine the stimulus/candidate set first. Production automatic dynamic reporting remains OFF.";

        evidence.Add($"G2.5-A2 combined: success={success}; baselineCaptured=True; associationHealthy={healthy}; candidates={trackers.Count}; fastLane={fastLane.Length}; cycles={cycles}; readFailures={readFailures}; eligible={eligible.Length}");
        evidence.Add("G2.5-A2 safety: profile remains InformationReportProven; no production dynamic-report policy was changed.");

        return new DynamicReportStimulusEligibilityDiscoveryResult
        {
            IsSuccess = success,
            BaselineCaptured = true,
            AssociationHealthy = healthy,
            StimulusEligibilityProven = eligible.Length > 0,
            CandidateCount = trackers.Count,
            FastLaneCount = fastLane.Length,
            SampleCycles = cycles,
            ReadFailures = readFailures,
            Summary = summary,
            Identity = identity,
            InputProfile = profile,
            Observations = observations,
            EligibleCandidates = eligible,
            EvidenceLines = evidence.ToArray()
        };
    }

    internal static IReadOnlyList<RankedCandidate> BuildRankedCandidates(
        ArMms.MmsIedModelDirectory directory,
        IReadOnlyList<SignalDefinition> fullModelSignals,
        string? provenRcbReference)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(fullModelSignals);

        var preferredStatusReferences = fullModelSignals
            .Where(signal => signal.IsControlSignal && !string.IsNullOrWhiteSpace(signal.ControlStatusReference))
            .Select(signal => NormalizeReference(signal.ControlStatusReference))
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var preferredDomain = ExtractDomain(provenRcbReference);

        return directory.Points
            .Where(IsStatusValueCandidate)
            .Select(point => ScoreCandidate(point, preferredStatusReferences, preferredDomain))
            .Where(candidate => candidate.Score > 0)
            .GroupBy(candidate => candidate.Point.MmsReference, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Point.UserReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static DynamicReportStimulusEligibilityKind ClassifyObservation(
        string baselineValue,
        string finalValue,
        IReadOnlyList<DynamicReportStimulusEligibilityTransition> transitions)
    {
        if (transitions.Count == 0)
            return DynamicReportStimulusEligibilityKind.None;

        if (!SameValue(baselineValue, finalValue))
            return DynamicReportStimulusEligibilityKind.PersistentOrLatched;

        if (transitions.Count >= 2)
            return DynamicReportStimulusEligibilityKind.MomentaryOrPulse;

        return DynamicReportStimulusEligibilityKind.TransitionObserved;
    }

    private static RankedCandidate ScoreCandidate(
        ArMms.MmsFcResolvedPoint point,
        IReadOnlyList<string> preferredStatusReferences,
        string preferredDomain)
    {
        var normalizedUser = NormalizeReference(point.UserReference);
        var normalizedMms = NormalizeReference(point.MmsReference);
        var lnClass = ExtractLogicalNodeClass(point.LogicalNode);
        var path = point.DataObjectPath ?? string.Empty;
        var score = 0;
        var reasons = new List<string>();

        if (preferredStatusReferences.Any(reference => ReferenceMatches(reference, normalizedUser, normalizedMms)))
        {
            score += 1400;
            reasons.Add("live ControlStatusReference");
        }

        if (path.Equals("Pos.stVal", StringComparison.OrdinalIgnoreCase))
        {
            score += lnClass switch
            {
                "XCBR" => 1100,
                "CSWI" => 1050,
                "XSWI" => 1000,
                _ => 450
            };
            reasons.Add($"{lnClass}.Pos.stVal");
        }

        if (ContainsAny(path, "CBClsCmdRecv", "CBOpnCmdRecv"))
        {
            score += 900;
            reasons.Add("breaker command-received status");
        }
        else if (ContainsAny(path, "Opn", "Open", "Cls", "Close", "Cmd", "Pos"))
        {
            score += 500;
            reasons.Add("open/close/command/position semantic");
        }

        if (ContainsAny(path, "SwRem", "SwSupervsry"))
        {
            score += 350;
            reasons.Add("switch/supervision status");
        }

        score += lnClass switch
        {
            "XCBR" => 400,
            "CSWI" => 350,
            "XSWI" => 325,
            "GGIO" => 250,
            "CILO" => 175,
            _ => 25
        };

        if (!string.IsNullOrWhiteSpace(preferredDomain) && point.Domain.Equals(preferredDomain, StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
            reasons.Add("same LD as proven URCB");
        }

        if (reasons.Count == 0)
            reasons.Add("ST/stVal fallback");

        return new RankedCandidate(point, score, string.Join(", ", reasons));
    }

    private static bool IsStatusValueCandidate(ArMms.MmsFcResolvedPoint point)
    {
        if (!point.FunctionalConstraint.Equals("ST", StringComparison.OrdinalIgnoreCase) ||
            point.IsReportAttribute || point.IsControlAttribute)
            return false;

        var path = point.DataObjectPath ?? string.Empty;
        return path.Equals("stVal", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int> SampleLaneAsync(
        ArMms.MmsClientSession session,
        IReadOnlyList<CandidateTracker> lane,
        ICollection<string> evidence,
        CancellationToken cancellationToken)
    {
        var failures = 0;
        foreach (var tracker in lane)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await session.ReadSingleVariableAsync(tracker.Candidate.Point.ToObjectReference(), cancellationToken).ConfigureAwait(false);
            if (!read.IsSuccess || read.Value is null)
            {
                failures++;
                tracker.ReadFailures++;
                continue;
            }

            var value = NormalizeValue(ArMms.MmsDataValueRenderer.ToCompactString(read.Value));
            if (!IsUsableStatusValue(value))
                continue;

            var now = DateTimeOffset.UtcNow;
            if (!SameValue(value, tracker.CurrentValue))
            {
                var transition = new DynamicReportStimulusEligibilityTransition
                {
                    Reference = tracker.Candidate.Point.UserReference,
                    MmsReference = tracker.Candidate.Point.MmsReference,
                    BeforeValue = tracker.CurrentValue,
                    AfterValue = value,
                    ObservedAtUtc = now
                };
                tracker.Transitions.Add(transition);
                evidence.Add($"G2.5-A2 transition: ref={transition.Reference}; before={transition.BeforeValue}; after={transition.AfterValue}; at={transition.ObservedAtUtc:O}; fastLane={tracker.FastLane}");
                tracker.CurrentValue = value;
            }
            tracker.LastObservedAtUtc = now;
        }

        return failures;
    }

    private static IReadOnlyList<DynamicReportStimulusEligibilityObservation> BuildObservations(
        IReadOnlyList<CandidateTracker> trackers,
        DateTimeOffset endedAt)
    {
        return trackers
            .OrderByDescending(item => item.Candidate.Score)
            .ThenBy(item => item.Candidate.Point.UserReference, StringComparer.OrdinalIgnoreCase)
            .Select(item =>
            {
                var kind = ClassifyObservation(item.BaselineValue, item.CurrentValue, item.Transitions);
                return new DynamicReportStimulusEligibilityObservation
                {
                    Score = item.Candidate.Score,
                    FastLane = item.FastLane,
                    Reference = item.Candidate.Point.UserReference,
                    MmsReference = item.Candidate.Point.MmsReference,
                    LogicalNode = item.Candidate.Point.LogicalNode,
                    FunctionalConstraint = item.Candidate.Point.FunctionalConstraint,
                    SelectionReason = item.Candidate.Reason,
                    BaselineValue = item.BaselineValue,
                    FinalValue = item.CurrentValue,
                    TransitionCount = item.Transitions.Count,
                    Kind = kind,
                    ObservedActiveMilliseconds = ComputeObservedActiveMilliseconds(item.BaselineValue, item.CurrentValue, item.Transitions, endedAt),
                    Transitions = item.Transitions.ToArray()
                };
            })
            .ToArray();
    }

    private static double? ComputeObservedActiveMilliseconds(
        string baseline,
        string final,
        IReadOnlyList<DynamicReportStimulusEligibilityTransition> transitions,
        DateTimeOffset endedAt)
    {
        if (transitions.Count == 0)
            return null;

        var departure = transitions.FirstOrDefault(item => SameValue(item.BeforeValue, baseline) && !SameValue(item.AfterValue, baseline))
                        ?? transitions[0];
        var returned = transitions.FirstOrDefault(item =>
            item.ObservedAtUtc >= departure.ObservedAtUtc &&
            SameValue(item.AfterValue, baseline));
        var end = returned?.ObservedAtUtc ?? endedAt;
        var milliseconds = (end - departure.ObservedAtUtc).TotalMilliseconds;
        return milliseconds < 0 ? null : milliseconds;
    }

    private static int EligibilitySortScore(DynamicReportStimulusEligibilityObservation observation)
        => observation.Score + observation.Kind switch
        {
            DynamicReportStimulusEligibilityKind.PersistentOrLatched => 500,
            DynamicReportStimulusEligibilityKind.MomentaryOrPulse => 350,
            DynamicReportStimulusEligibilityKind.TransitionObserved => 200,
            _ => 0
        };

    private static DynamicReportStimulusEligibilityObservation CloneWithRank(
        DynamicReportStimulusEligibilityObservation source,
        int rank)
        => new()
        {
            Rank = rank,
            Score = source.Score,
            FastLane = source.FastLane,
            Reference = source.Reference,
            MmsReference = source.MmsReference,
            LogicalNode = source.LogicalNode,
            FunctionalConstraint = source.FunctionalConstraint,
            SelectionReason = source.SelectionReason,
            BaselineValue = source.BaselineValue,
            FinalValue = source.FinalValue,
            TransitionCount = source.TransitionCount,
            Kind = source.Kind,
            ObservedActiveMilliseconds = source.ObservedActiveMilliseconds,
            Transitions = source.Transitions
        };

    private static bool IsUsableStatusValue(string value)
        => !string.IsNullOrWhiteSpace(value) &&
           !value.StartsWith("Structure(", StringComparison.OrdinalIgnoreCase) &&
           !value.StartsWith("Array(", StringComparison.OrdinalIgnoreCase) &&
           !value.Equals("-", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeValue(string? value)
        => (value ?? string.Empty).Trim();

    private static bool SameValue(string? left, string? right)
        => string.Equals(NormalizeValue(left), NormalizeValue(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReference(string? value)
        => (value ?? string.Empty)
            .Trim()
            .Replace('$', '.')
            .Replace("..", ".", StringComparison.Ordinal);

    private static bool ReferenceMatches(string preferred, string user, string mms)
    {
        if (string.IsNullOrWhiteSpace(preferred))
            return false;
        if (preferred.Equals(user, StringComparison.OrdinalIgnoreCase) ||
            preferred.Equals(mms, StringComparison.OrdinalIgnoreCase))
            return true;

        var slash = preferred.IndexOf('/');
        var suffix = slash >= 0 ? preferred[(slash + 1)..] : preferred;
        return user.EndsWith('/' + suffix, StringComparison.OrdinalIgnoreCase) ||
               mms.EndsWith('/' + suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractDomain(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return string.Empty;
        var slash = reference.IndexOf('/');
        return slash > 0 ? reference[..slash].Trim() : string.Empty;
    }

    private static string ExtractLogicalNodeClass(string? logicalNode)
    {
        var value = (logicalNode ?? string.Empty).Trim();
        if (value.Length == 0)
            return string.Empty;
        return new string(value.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
    }

    private static bool ContainsAny(string value, params string[] tokens)
        => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string FormatMilliseconds(double? value)
        => value.HasValue ? value.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "-";

    private static DynamicReportStimulusEligibilityDiscoveryResult Blocked(
        string summary,
        IReadOnlyList<string> evidence,
        ArMms.MmsDynamicReportIedIdentity? identity = null,
        ArMms.MmsDynamicReportQualificationProfile? profile = null)
        => new()
        {
            IsBlocked = true,
            Summary = summary + " Production automatic dynamic reporting remains OFF.",
            Identity = identity,
            InputProfile = profile,
            EvidenceLines = evidence.ToArray()
        };

    private static DynamicReportStimulusEligibilityDiscoveryResult Failed(
        string summary,
        IReadOnlyList<string> evidence,
        ArMms.MmsDynamicReportIedIdentity? identity = null,
        ArMms.MmsDynamicReportQualificationProfile? profile = null,
        bool associationHealthy = false,
        int candidateCount = 0,
        int readFailures = 0)
        => new()
        {
            Summary = summary + " Production automatic dynamic reporting remains OFF.",
            Identity = identity,
            InputProfile = profile,
            AssociationHealthy = associationHealthy,
            CandidateCount = candidateCount,
            ReadFailures = readFailures,
            EvidenceLines = evidence.ToArray()
        };

    internal sealed record RankedCandidate(ArMms.MmsFcResolvedPoint Point, int Score, string Reason);

    private sealed class CandidateTracker
    {
        public CandidateTracker(RankedCandidate candidate, string baselineValue)
        {
            Candidate = candidate;
            BaselineValue = baselineValue;
            CurrentValue = baselineValue;
            LastObservedAtUtc = DateTimeOffset.UtcNow;
        }

        public RankedCandidate Candidate { get; }
        public string BaselineValue { get; }
        public string CurrentValue { get; set; }
        public bool FastLane { get; set; }
        public int ReadFailures { get; set; }
        public DateTimeOffset LastObservedAtUtc { get; set; }
        public List<DynamicReportStimulusEligibilityTransition> Transitions { get; } = new();
    }
}
