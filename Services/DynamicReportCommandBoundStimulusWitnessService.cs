using System.ComponentModel;
using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

internal sealed class DynamicReportCommandBoundTransition
{
    public string Reference { get; init; } = string.Empty;
    public string MmsReference { get; init; } = string.Empty;
    public string BeforeValue { get; init; } = string.Empty;
    public string AfterValue { get; init; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; init; }
}

internal sealed class DynamicReportCommandBoundObservation
{
    public int Rank { get; init; }
    public bool ExactControlStatus { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string MmsReference { get; init; } = string.Empty;
    public string LogicalNode { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string BaselineValue { get; init; } = string.Empty;
    public string FinalValue { get; init; } = string.Empty;
    public int TransitionCount { get; init; }
    public DynamicReportStimulusEligibilityKind Kind { get; init; }
    public double? ObservedActiveMilliseconds { get; init; }
    public IReadOnlyList<DynamicReportCommandBoundTransition> Transitions { get; init; } = Array.Empty<DynamicReportCommandBoundTransition>();
}

internal sealed class DynamicReportCommandBoundStimulusWitnessResult
{
    public bool IsSuccess { get; init; }
    public bool IsBlocked { get; init; }
    public bool BaselineCaptured { get; init; }
    public bool CommandCaptured { get; init; }
    public bool AssociationHealthy { get; init; }
    public bool StimulusWitnessProven { get; init; }
    public string CommandSignalReference { get; init; } = string.Empty;
    public string ControlStatusReference { get; init; } = string.Empty;
    public string ControlModelText { get; init; } = string.Empty;
    public int PreCommandBaselineCount { get; init; }
    public int FocusCandidateCount { get; init; }
    public int SampleCycles { get; init; }
    public int ReadFailures { get; init; }
    public string Summary { get; init; } = string.Empty;
    public ArMms.MmsDynamicReportIedIdentity? Identity { get; init; }
    public ArMms.MmsDynamicReportQualificationProfile? InputProfile { get; init; }
    public IReadOnlyList<DynamicReportCommandBoundObservation> Observations { get; init; } = Array.Empty<DynamicReportCommandBoundObservation>();
    public IReadOnlyList<DynamicReportCommandBoundObservation> EligibleCandidates { get; init; } = Array.Empty<DynamicReportCommandBoundObservation>();
    public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();
}

/// <summary>
/// G2.5-A2.1 command-bound high-speed stimulus witness.
///
/// The witness is read-only. It does not alter the existing ARSAS control transaction.
/// Before the operator issues a command, an isolated MMS association captures a bounded
/// baseline for live control-feedback/status points. The service then listens only to
/// SignalDefinition.PropertyChanged and identifies the exact signal whose existing
/// ControlCommandBusy state becomes true. Once captured, sampling narrows to the exact
/// ControlStatusReference plus a tiny related status chain. No RCB/DataSet/report API is
/// used and the qualification profile is never saved or advanced.
/// </summary>
internal sealed class DynamicReportCommandBoundStimulusWitnessService
{
    internal const string ReadyMarker = "G2.5-A2.1 READY — ISSUE ONE ARSAS COMMAND";
    internal const string CommandCapturedMarker = "G2.5-A2.1 COMMAND CAPTURED";
    internal const string TransitionMarker = "G2.5-A2.1 TRANSITION OBSERVED";
    internal const int MaximumPreCommandBaselinePoints = 128;
    internal const int MaximumFocusCandidates = 6;

    internal static readonly TimeSpan AssociationTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan CommandWaitWindow = TimeSpan.FromSeconds(45);
    internal static readonly TimeSpan FocusObservationWindow = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan PostTransitionSettleWindow = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan InterCycleDelay = TimeSpan.FromMilliseconds(1);

    private readonly DynamicReportQualificationProfileStore _profileStore;

    public DynamicReportCommandBoundStimulusWitnessService(
        DynamicReportQualificationProfileStore? profileStore = null)
    {
        _profileStore = profileStore ?? new DynamicReportQualificationProfileStore();
    }

    public async Task<DynamicReportCommandBoundStimulusWitnessResult> RunAsync(
        Iec61850MonitorDevice device,
        IReadOnlyList<SignalDefinition> fullModelSignals,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fullModelSignals);

        var evidence = new List<string>
        {
            "G2.5-A2.1 contract: command-bound HIGH-SPEED witness is READ ONLY. Existing ARSAS control logic is not modified, delayed, wrapped, replaced or re-issued by this service.",
            "G2.5-A2.1 witness performs no RCB attribute access, no RptEna/Resv/DatSet/TrgOps/OptFlds mutation, no GI, no Define/DeleteNamedVariableList, no report monitor and no profile save.",
            $"G2.5-A2.1 bounds: preCommandBaseline<={MaximumPreCommandBaselinePoints}; focusedCandidates<={MaximumFocusCandidates}; waitForCommand={CommandWaitWindow.TotalSeconds:0}s; focusWindow={FocusObservationWindow.TotalSeconds:0}s; settleAfterFirstTransition={PostTransitionSettleWindow.TotalSeconds:0}s."
        };

        ArMms.MmsDynamicReportIedIdentity identity;
        try
        {
            identity = DynamicReportQualificationIdentity.Build(device, fullModelSignals);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Blocked("G2.5-A2.1 identity preflight failed: " + ex.Message, evidence);
        }

        var loaded = await _profileStore.LoadAsync(identity, cancellationToken).ConfigureAwait(false);
        evidence.Add($"G2.5-A2.1 persisted profile: exists={loaded.Exists}; valid={loaded.IsValid}; state={loaded.Profile?.State.ToString() ?? "-"}; reason={loaded.Reason}");
        if (!loaded.IsValid || loaded.Profile is null ||
            loaded.Profile.State != ArMms.MmsDynamicReportQualificationState.InformationReportProven)
        {
            return Blocked(
                "G2.5-A2.1 requires the identity-compatible InformationReportProven G2.4 profile.",
                evidence,
                identity,
                loaded.Profile);
        }

        var profile = loaded.Profile;
        var commandSignals = fullModelSignals
            .Where(signal => signal.IsControlSignal && !string.IsNullOrWhiteSpace(signal.ControlStatusReference))
            .Distinct()
            .ToArray();

        if (commandSignals.Length == 0)
        {
            return Failed(
                "No live control signal exposes a ControlStatusReference. Inspect the control model first; A2.1 will not guess a command object.",
                evidence,
                identity,
                profile,
                associationHealthy: true);
        }

        if (commandSignals.Any(signal => signal.ControlCommandBusy))
        {
            return Blocked(
                "A control command is already in progress. A2.1 must be armed before the one test command begins.",
                evidence,
                identity,
                profile);
        }

        await using var session = new ArMms.MmsClientSession();
        try
        {
            await session.ConnectAsync(device.IpAddress, device.Port, AssociationTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException)
        {
            evidence.Add($"G2.5-A2.1 association failed: {ex.GetType().Name}: {ex.Message}");
            return Failed("The isolated read-only A2.1 MMS association could not be established.", evidence, identity, profile, false);
        }

        evidence.Add($"G2.5-A2.1 association ready: state={session.State}; localTcpAddress={TextOrDash(session.LocalTcpAddress)}; READ-ONLY=true");

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
            evidence.Add($"G2.5-A2.1 discovery failed: {ex.GetType().Name}: {ex.Message}");
            return Failed("A2.1 live discovery failed before command arming.", evidence, identity, profile, session.IsMmsInitiated);
        }

        evidence.Add("G2.5-A2.1 discovery: " + discovery.Summary);

        var signalStatusPoints = ResolveCommandStatusPoints(discovery.IedDirectory, commandSignals, evidence);
        if (signalStatusPoints.Count == 0)
        {
            return Failed(
                "None of the live ControlStatusReference values resolved to an ST/stVal MMS point.",
                evidence,
                identity,
                profile,
                session.IsMmsInitiated);
        }

        var preCommandPoints = BuildPreCommandBaselinePoints(discovery.IedDirectory, signalStatusPoints.Values)
            .Take(MaximumPreCommandBaselinePoints + 1)
            .ToArray();
        if (preCommandPoints.Length > MaximumPreCommandBaselinePoints)
        {
            return Blocked(
                $"A2.1 bounded baseline would exceed {MaximumPreCommandBaselinePoints} status points. Narrow the connected model rather than silently truncating command evidence.",
                evidence,
                identity,
                profile);
        }

        var baseline = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var readFailures = 0;
        foreach (var point in preCommandPoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await session.ReadSingleVariableAsync(point.ToObjectReference(), cancellationToken).ConfigureAwait(false);
            if (!read.IsSuccess || read.Value is null)
            {
                readFailures++;
                evidence.Add($"G2.5-A2.1 pre-command baseline read failed: ref={point.UserReference}; result={read.Message}");
                continue;
            }

            baseline[point.MmsReference] = NormalizeValue(ArMms.MmsDataValueRenderer.ToCompactString(read.Value));
        }

        if (!session.IsMmsInitiated || baseline.Count == 0)
        {
            return Failed(
                "A2.1 could not capture a reliable pre-command read-only baseline.",
                evidence,
                identity,
                profile,
                session.IsMmsInitiated,
                baselineCount: baseline.Count,
                readFailures: readFailures);
        }

        evidence.Add($"G2.5-A2.1 pre-command baseline captured: successful={baseline.Count}/{preCommandPoints.Length}; failures={readFailures}");
        evidence.Add("G2.5-A2.1 resolved command status references: " + string.Join(" | ", signalStatusPoints.Select(pair => $"{pair.Key.ObjectReference} -> {pair.Value.UserReference}")));

        var commandCapture = new TaskCompletionSource<SignalDefinition>(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler handler = (sender, args) =>
        {
            if (args.PropertyName == nameof(SignalDefinition.ControlCommandBusy) &&
                sender is SignalDefinition signal &&
                signal.ControlCommandBusy &&
                signalStatusPoints.ContainsKey(signal))
            {
                commandCapture.TrySetResult(signal);
            }
        };

        foreach (var signal in commandSignals)
            signal.PropertyChanged += handler;

        SignalDefinition commandedSignal;
        try
        {
            progress?.Report($"{ReadyMarker} — baseline is already captured. NOW issue exactly ONE already-proven safe OPEN/CLOSE from the ARSAS Command Panel. Do not use an external/manual stimulus for A2.1.");
            evidence.Add($"{ReadyMarker}: waiting for one existing ARSAS command; witness itself will not issue or delay the command.");
            commandedSignal = await commandCapture.Task.WaitAsync(CommandWaitWindow, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            evidence.Add("G2.5-A2.1 command wait timed out with no ControlCommandBusy transition on a resolved command signal.");
            return Failed(
                "A2.1 timed out before an ARSAS command was captured. No stimulus conclusion is possible.",
                evidence,
                identity,
                profile,
                session.IsMmsInitiated,
                baselineCount: baseline.Count,
                readFailures: readFailures);
        }
        finally
        {
            foreach (var signal in commandSignals)
                signal.PropertyChanged -= handler;
        }

        var commandObservedAt = DateTimeOffset.UtcNow;
        var exactStatus = signalStatusPoints[commandedSignal];
        var focusPoints = BuildFocusChain(discovery.IedDirectory, exactStatus)
            .Take(MaximumFocusCandidates)
            .ToArray();

        evidence.Add($"{CommandCapturedMarker}: signal={commandedSignal.ObjectReference}; controlStatus={commandedSignal.ControlStatusReference}; resolvedStatus={exactStatus.UserReference}; controlModel={TextOrDash(commandedSignal.ControlModelText)}; at={commandObservedAt:O}");
        evidence.Add("G2.5-A2.1 focused chain: " + string.Join(" | ", focusPoints.Select(point => point.UserReference)));
        progress?.Report($"{CommandCapturedMarker} — exact object {commandedSignal.ObjectReference}. High-speed read-only sampling is active; do NOT issue another command.");

        var trackers = new List<FocusTracker>();
        foreach (var point in focusPoints)
        {
            if (!baseline.TryGetValue(point.MmsReference, out var value))
            {
                var read = await session.ReadSingleVariableAsync(point.ToObjectReference(), cancellationToken).ConfigureAwait(false);
                if (!read.IsSuccess || read.Value is null)
                {
                    readFailures++;
                    evidence.Add($"G2.5-A2.1 focused fallback baseline failed: ref={point.UserReference}; result={read.Message}");
                    continue;
                }

                value = NormalizeValue(ArMms.MmsDataValueRenderer.ToCompactString(read.Value));
                evidence.Add($"G2.5-A2.1 focused fallback baseline captured immediately after command claim: ref={point.UserReference}; value={value}");
            }

            trackers.Add(new FocusTracker(point, value, SameReference(point.MmsReference, exactStatus.MmsReference)));
        }

        if (trackers.Count == 0)
        {
            return Failed(
                "A2.1 captured the command but could not establish any focused status point for high-speed sampling.",
                evidence,
                identity,
                profile,
                session.IsMmsInitiated,
                commandedSignal,
                baseline.Count,
                0,
                0,
                readFailures);
        }

        var hardDeadline = DateTimeOffset.UtcNow + FocusObservationWindow;
        DateTimeOffset? settleDeadline = null;
        var cycles = 0;
        var transitionAnnounced = false;

        while (DateTimeOffset.UtcNow < hardDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cycles++;

            foreach (var tracker in trackers)
            {
                var read = await session.ReadSingleVariableAsync(tracker.Point.ToObjectReference(), cancellationToken).ConfigureAwait(false);
                if (!read.IsSuccess || read.Value is null)
                {
                    readFailures++;
                    continue;
                }

                var current = NormalizeValue(ArMms.MmsDataValueRenderer.ToCompactString(read.Value));
                if (!SameValue(current, tracker.CurrentValue))
                {
                    var transition = new DynamicReportCommandBoundTransition
                    {
                        Reference = tracker.Point.UserReference,
                        MmsReference = tracker.Point.MmsReference,
                        BeforeValue = tracker.CurrentValue,
                        AfterValue = current,
                        ObservedAtUtc = DateTimeOffset.UtcNow
                    };
                    tracker.Transitions.Add(transition);
                    tracker.CurrentValue = current;
                    evidence.Add($"G2.5-A2.1 transition: exactStatus={tracker.ExactControlStatus}; ref={transition.Reference}; before={transition.BeforeValue}; after={transition.AfterValue}; at={transition.ObservedAtUtc:O}");
                }
            }

            if (!session.IsMmsInitiated)
            {
                evidence.Add("G2.5-A2.1 association left MmsInitiated during focused sampling.");
                break;
            }

            var firstTransition = trackers.SelectMany(item => item.Transitions).OrderBy(item => item.ObservedAtUtc).FirstOrDefault();
            if (firstTransition is not null && !transitionAnnounced)
            {
                transitionAnnounced = true;
                settleDeadline = DateTimeOffset.UtcNow + PostTransitionSettleWindow;
                evidence.Add($"{TransitionMarker}: first={firstTransition.Reference}; {firstTransition.BeforeValue}->{firstTransition.AfterValue}; settleUntil={settleDeadline:O}");
                progress?.Report($"{TransitionMarker} — {firstTransition.Reference}: {firstTransition.BeforeValue} → {firstTransition.AfterValue}. No more commands; classifying pulse vs persistent state.");
            }

            if (settleDeadline.HasValue && DateTimeOffset.UtcNow >= settleDeadline.Value)
                break;

            if (InterCycleDelay > TimeSpan.Zero)
                await Task.Delay(InterCycleDelay, cancellationToken).ConfigureAwait(false);
        }

        var endedAt = DateTimeOffset.UtcNow;
        var observations = trackers.Select(item => BuildObservation(item, endedAt)).ToArray();
        var eligible = observations
            .Where(item => item.TransitionCount > 0)
            .OrderByDescending(EligibilityScore)
            .ThenBy(item => item.Reference, StringComparer.OrdinalIgnoreCase)
            .Select((item, index) => CloneWithRank(item, index + 1))
            .ToArray();

        foreach (var item in eligible)
        {
            evidence.Add($"G2.5-A2.1 ELIGIBLE: rank={item.Rank}; exactStatus={item.ExactControlStatus}; kind={item.Kind}; ref={item.Reference}; baseline={item.BaselineValue}; final={item.FinalValue}; transitions={item.TransitionCount}; activeMs={FormatMilliseconds(item.ObservedActiveMilliseconds)}");
        }

        var healthy = session.IsMmsInitiated;
        var success = healthy && eligible.Length > 0;
        var summary = success
            ? $"G2.5-A2.1 PASS: exact ARSAS command {commandedSignal.ObjectReference} was captured and {eligible.Length} command-bound status candidate(s) changed. Top candidate: {eligible[0].Reference} ({eligible[0].Kind}). Use this exact evidence for narrow A3 dchg qualification. Production automatic dynamic reporting remains OFF."
            : $"G2.5-A2.1 captured exact ARSAS command {commandedSignal.ObjectReference}, but no focused status transition was observed. Do not advance to A3/G2.5-B; inspect the exact control-feedback mapping. Production automatic dynamic reporting remains OFF.";

        evidence.Add($"G2.5-A2.1 combined: success={success}; commandCaptured=True; exactSignal={commandedSignal.ObjectReference}; exactStatus={exactStatus.UserReference}; focused={trackers.Count}; cycles={cycles}; readFailures={readFailures}; eligible={eligible.Length}; associationHealthy={healthy}");
        evidence.Add("G2.5-A2.1 safety: witness did not modify the control transaction, persisted InformationReportProven profile, RCB/DataSet state, or production dynamic-report policy.");

        return new DynamicReportCommandBoundStimulusWitnessResult
        {
            IsSuccess = success,
            BaselineCaptured = true,
            CommandCaptured = true,
            AssociationHealthy = healthy,
            StimulusWitnessProven = eligible.Length > 0,
            CommandSignalReference = commandedSignal.ObjectReference,
            ControlStatusReference = commandedSignal.ControlStatusReference,
            ControlModelText = commandedSignal.ControlModelText,
            PreCommandBaselineCount = baseline.Count,
            FocusCandidateCount = trackers.Count,
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

    internal static IReadOnlyDictionary<SignalDefinition, ArMms.MmsFcResolvedPoint> ResolveCommandStatusPoints(
        ArMms.MmsIedModelDirectory directory,
        IReadOnlyList<SignalDefinition> commandSignals,
        ICollection<string>? evidence = null)
    {
        var result = new Dictionary<SignalDefinition, ArMms.MmsFcResolvedPoint>();
        foreach (var signal in commandSignals)
        {
            var point = ResolveStatusPoint(directory, signal.ControlStatusReference);
            if (point is null)
            {
                evidence?.Add($"G2.5-A2.1 unresolved ControlStatusReference: signal={signal.ObjectReference}; status={signal.ControlStatusReference}");
                continue;
            }
            result[signal] = point;
        }
        return result;
    }

    internal static IReadOnlyList<ArMms.MmsFcResolvedPoint> BuildPreCommandBaselinePoints(
        ArMms.MmsIedModelDirectory directory,
        IEnumerable<ArMms.MmsFcResolvedPoint> resolvedStatusPoints)
    {
        var list = new List<ArMms.MmsFcResolvedPoint>();
        AddDistinct(list, resolvedStatusPoints);
        AddDistinct(list, directory.Points.Where(IsPositionStatusPoint));
        AddDistinct(list, directory.Points.Where(IsCommandCorrelationPoint));
        return list;
    }

    internal static IReadOnlyList<ArMms.MmsFcResolvedPoint> BuildFocusChain(
        ArMms.MmsIedModelDirectory directory,
        ArMms.MmsFcResolvedPoint exactStatus)
    {
        var list = new List<ArMms.MmsFcResolvedPoint> { exactStatus };

        AddDistinct(list, directory.Points.Where(point =>
            point.Domain.Equals(exactStatus.Domain, StringComparison.OrdinalIgnoreCase) &&
            IsPositionStatusPoint(point)));

        AddDistinct(list, directory.Points.Where(point =>
            point.Domain.Equals(exactStatus.Domain, StringComparison.OrdinalIgnoreCase) &&
            point.LogicalNode.Equals(exactStatus.LogicalNode, StringComparison.OrdinalIgnoreCase) &&
            IsStatusValuePoint(point)));

        AddDistinct(list, directory.Points.Where(IsCommandCorrelationPoint));
        return list.Take(MaximumFocusCandidates).ToArray();
    }

    internal static DynamicReportStimulusEligibilityKind Classify(
        string baseline,
        string final,
        IReadOnlyList<DynamicReportCommandBoundTransition> transitions)
    {
        if (transitions.Count == 0)
            return DynamicReportStimulusEligibilityKind.None;
        if (!SameValue(final, baseline))
            return DynamicReportStimulusEligibilityKind.PersistentOrLatched;
        if (transitions.Count >= 2)
            return DynamicReportStimulusEligibilityKind.MomentaryOrPulse;
        return DynamicReportStimulusEligibilityKind.TransitionObserved;
    }

    private static ArMms.MmsFcResolvedPoint? ResolveStatusPoint(ArMms.MmsIedModelDirectory directory, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        if (directory.TryFindByMmsReference(reference, out var direct) && IsStatusValuePoint(direct))
            return direct;

        var userMatches = directory.FindByUserReference(reference).Where(IsStatusValuePoint).OrderByDescending(point => point.Confidence).ToArray();
        if (userMatches.Length > 0)
            return userMatches[0];

        return directory.FindByPathSuffix(reference).Where(IsStatusValuePoint).OrderByDescending(point => point.Confidence).FirstOrDefault();
    }

    private static bool IsStatusValuePoint(ArMms.MmsFcResolvedPoint point)
        => point.FunctionalConstraint.Equals("ST", StringComparison.OrdinalIgnoreCase) &&
           !point.IsReportAttribute &&
           !point.IsControlAttribute &&
           (point.DataObjectPath.Equals("stVal", StringComparison.OrdinalIgnoreCase) ||
            point.DataObjectPath.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase));

    private static bool IsPositionStatusPoint(ArMms.MmsFcResolvedPoint point)
    {
        if (!IsStatusValuePoint(point) || !point.DataObjectPath.Equals("Pos.stVal", StringComparison.OrdinalIgnoreCase))
            return false;
        var lnClass = ExtractLogicalNodeClass(point.LogicalNode);
        return lnClass is "XCBR" or "CSWI" or "XSWI";
    }

    private static bool IsCommandCorrelationPoint(ArMms.MmsFcResolvedPoint point)
    {
        if (!IsStatusValuePoint(point))
            return false;
        var path = point.DataObjectPath;
        return path.Contains("CBClsCmdRecv", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("CBOpnCmdRecv", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("LocClsCMDsta", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("LocOpnCMDsta", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractLogicalNodeClass(string logicalNode)
    {
        var text = logicalNode ?? string.Empty;
        var index = 0;
        while (index < text.Length && !char.IsDigit(text[index]))
            index++;
        return text[..index].ToUpperInvariant();
    }

    private static void AddDistinct(List<ArMms.MmsFcResolvedPoint> target, IEnumerable<ArMms.MmsFcResolvedPoint> points)
    {
        foreach (var point in points)
        {
            if (target.Any(existing => SameReference(existing.MmsReference, point.MmsReference)))
                continue;
            target.Add(point);
        }
    }

    private static DynamicReportCommandBoundObservation BuildObservation(FocusTracker tracker, DateTimeOffset endedAt)
    {
        var kind = Classify(tracker.BaselineValue, tracker.CurrentValue, tracker.Transitions);
        return new DynamicReportCommandBoundObservation
        {
            ExactControlStatus = tracker.ExactControlStatus,
            Reference = tracker.Point.UserReference,
            MmsReference = tracker.Point.MmsReference,
            LogicalNode = tracker.Point.LogicalNode,
            FunctionalConstraint = tracker.Point.FunctionalConstraint,
            BaselineValue = tracker.BaselineValue,
            FinalValue = tracker.CurrentValue,
            TransitionCount = tracker.Transitions.Count,
            Kind = kind,
            ObservedActiveMilliseconds = ComputeObservedActiveMilliseconds(tracker.BaselineValue, tracker.CurrentValue, tracker.Transitions, endedAt),
            Transitions = tracker.Transitions.ToArray()
        };
    }

    private static int EligibilityScore(DynamicReportCommandBoundObservation item)
    {
        var score = item.ExactControlStatus ? 1000 : 0;
        score += item.Kind switch
        {
            DynamicReportStimulusEligibilityKind.PersistentOrLatched => 500,
            DynamicReportStimulusEligibilityKind.MomentaryOrPulse => 350,
            DynamicReportStimulusEligibilityKind.TransitionObserved => 200,
            _ => 0
        };
        var lnClass = ExtractLogicalNodeClass(item.LogicalNode);
        score += lnClass switch
        {
            "XCBR" => 120,
            "CSWI" => 100,
            "XSWI" => 90,
            "GGIO" => 50,
            _ => 0
        };
        return score;
    }

    private static DynamicReportCommandBoundObservation CloneWithRank(DynamicReportCommandBoundObservation source, int rank)
        => new()
        {
            Rank = rank,
            ExactControlStatus = source.ExactControlStatus,
            Reference = source.Reference,
            MmsReference = source.MmsReference,
            LogicalNode = source.LogicalNode,
            FunctionalConstraint = source.FunctionalConstraint,
            BaselineValue = source.BaselineValue,
            FinalValue = source.FinalValue,
            TransitionCount = source.TransitionCount,
            Kind = source.Kind,
            ObservedActiveMilliseconds = source.ObservedActiveMilliseconds,
            Transitions = source.Transitions
        };

    private static double? ComputeObservedActiveMilliseconds(
        string baseline,
        string final,
        IReadOnlyList<DynamicReportCommandBoundTransition> transitions,
        DateTimeOffset endedAt)
    {
        if (transitions.Count == 0)
            return null;
        var departure = transitions.FirstOrDefault(item => SameValue(item.BeforeValue, baseline) && !SameValue(item.AfterValue, baseline)) ?? transitions[0];
        var returned = transitions.FirstOrDefault(item => item.ObservedAtUtc >= departure.ObservedAtUtc && SameValue(item.AfterValue, baseline));
        var end = returned?.ObservedAtUtc ?? endedAt;
        var milliseconds = (end - departure.ObservedAtUtc).TotalMilliseconds;
        return milliseconds < 0 ? null : milliseconds;
    }

    private static bool SameReference(string? left, string? right)
        => string.Equals(NormalizeReference(left), NormalizeReference(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReference(string? value)
        => (value ?? string.Empty).Trim().Replace('.', '$');

    private static bool SameValue(string? left, string? right)
        => string.Equals(NormalizeValue(left), NormalizeValue(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string FormatMilliseconds(double? value)
        => value.HasValue ? value.Value.ToString("0.###") : "-";

    private static DynamicReportCommandBoundStimulusWitnessResult Blocked(
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

    private static DynamicReportCommandBoundStimulusWitnessResult Failed(
        string summary,
        IReadOnlyList<string> evidence,
        ArMms.MmsDynamicReportIedIdentity? identity,
        ArMms.MmsDynamicReportQualificationProfile? profile,
        bool associationHealthy,
        SignalDefinition? commandedSignal = null,
        int baselineCount = 0,
        int focusCount = 0,
        int cycles = 0,
        int readFailures = 0)
        => new()
        {
            Summary = summary + " Production automatic dynamic reporting remains OFF.",
            Identity = identity,
            InputProfile = profile,
            BaselineCaptured = baselineCount > 0,
            CommandCaptured = commandedSignal is not null,
            AssociationHealthy = associationHealthy,
            CommandSignalReference = commandedSignal?.ObjectReference ?? string.Empty,
            ControlStatusReference = commandedSignal?.ControlStatusReference ?? string.Empty,
            ControlModelText = commandedSignal?.ControlModelText ?? string.Empty,
            PreCommandBaselineCount = baselineCount,
            FocusCandidateCount = focusCount,
            SampleCycles = cycles,
            ReadFailures = readFailures,
            EvidenceLines = evidence.ToArray()
        };

    private sealed class FocusTracker
    {
        public FocusTracker(ArMms.MmsFcResolvedPoint point, string baselineValue, bool exactControlStatus)
        {
            Point = point;
            BaselineValue = baselineValue;
            CurrentValue = baselineValue;
            ExactControlStatus = exactControlStatus;
        }

        public ArMms.MmsFcResolvedPoint Point { get; }
        public string BaselineValue { get; }
        public string CurrentValue { get; set; }
        public bool ExactControlStatus { get; }
        public List<DynamicReportCommandBoundTransition> Transitions { get; } = new();
    }
}
