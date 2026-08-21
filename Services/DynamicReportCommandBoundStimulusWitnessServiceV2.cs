using System.ComponentModel;
using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

/// <summary>
/// G2.5-A2.1 correction after the first physical run proved that ControlCommandWindow
/// bypasses SignalDefinition.ControlCommandBusy. V2 listens to BOTH existing fast-panel
/// ControlCommandBusy and the observer-only ControlCommandWindow routed-click intent bus.
/// It never mutates or re-issues a control transaction.
/// </summary>
internal sealed class DynamicReportCommandBoundStimulusWitnessServiceV2
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

    internal DynamicReportCommandBoundStimulusWitnessServiceV2(
        DynamicReportQualificationProfileStore? profileStore = null)
    {
        _profileStore = profileStore ?? new DynamicReportQualificationProfileStore();
    }

    internal async Task<DynamicReportCommandBoundStimulusWitnessResult> RunAsync(
        Iec61850MonitorDevice device,
        IReadOnlyList<SignalDefinition> fullModelSignals,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fullModelSignals);

        var evidence = new List<string>
        {
            "G2.5-A2.1 V2 contract: observer-only command capture + HIGH-SPEED MMS witness. Existing control source and wire transaction are untouched.",
            "G2.5-A2.1 V2 capture sources: fast Command Panel ControlCommandBusy OR ControlCommandWindow routed Button.Click intent observed before its existing SendCommand_Click handler.",
            "G2.5-A2.1 V2 performs no RCB attribute access, no RptEna/Resv/DatSet/TrgOps/OptFlds mutation, no GI, no Define/DeleteNamedVariableList, no report monitor and no profile save.",
            $"G2.5-A2.1 V2 bounds: preCommandBaseline<={MaximumPreCommandBaselinePoints}; focusedCandidates<={MaximumFocusCandidates}; waitForCommand={CommandWaitWindow.TotalSeconds:0}s; focusWindow={FocusObservationWindow.TotalSeconds:0}s; settleAfterFirstTransition={PostTransitionSettleWindow.TotalSeconds:0}s."
        };

        ArMms.MmsDynamicReportIedIdentity identity;
        try
        {
            identity = DynamicReportQualificationIdentity.Build(device, fullModelSignals);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Blocked("G2.5-A2.1 V2 identity preflight failed: " + ex.Message, evidence);
        }

        var loaded = await _profileStore.LoadAsync(identity, cancellationToken).ConfigureAwait(false);
        evidence.Add($"G2.5-A2.1 V2 persisted profile: exists={loaded.Exists}; valid={loaded.IsValid}; state={loaded.Profile?.State.ToString() ?? "-"}; reason={loaded.Reason}");
        if (!loaded.IsValid || loaded.Profile is null ||
            loaded.Profile.State != ArMms.MmsDynamicReportQualificationState.InformationReportProven)
        {
            return Blocked(
                "G2.5-A2.1 V2 requires the identity-compatible InformationReportProven G2.4 profile.",
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
            return Failed("No control signal exposes a ControlStatusReference.", evidence, identity, profile, true);

        if (commandSignals.Any(signal => signal.ControlCommandBusy))
            return Blocked("A control command is already in progress. Arm A2.1 before the one test command.", evidence, identity, profile);

        await using var session = new ArMms.MmsClientSession();
        try
        {
            await session.ConnectAsync(device.IpAddress, device.Port, AssociationTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException)
        {
            evidence.Add($"G2.5-A2.1 V2 association failed: {ex.GetType().Name}: {ex.Message}");
            return Failed("The isolated read-only A2.1 V2 MMS association could not be established.", evidence, identity, profile, false);
        }

        evidence.Add($"G2.5-A2.1 V2 association ready: state={session.State}; localTcpAddress={TextOrDash(session.LocalTcpAddress)}; READ-ONLY=true");

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
            evidence.Add($"G2.5-A2.1 V2 discovery failed: {ex.GetType().Name}: {ex.Message}");
            return Failed("A2.1 V2 live discovery failed before command arming.", evidence, identity, profile, session.IsMmsInitiated);
        }

        evidence.Add("G2.5-A2.1 V2 discovery: " + discovery.Summary);
        var signalStatusPoints = DynamicReportCommandBoundStimulusWitnessService.ResolveCommandStatusPoints(
            discovery.IedDirectory,
            commandSignals,
            evidence);
        if (signalStatusPoints.Count == 0)
            return Failed("None of the live ControlStatusReference values resolved to an ST/stVal MMS point.", evidence, identity, profile, session.IsMmsInitiated);

        var preCommandPoints = DynamicReportCommandBoundStimulusWitnessService.BuildPreCommandBaselinePoints(
                discovery.IedDirectory,
                signalStatusPoints.Values)
            .Take(MaximumPreCommandBaselinePoints + 1)
            .ToArray();
        if (preCommandPoints.Length > MaximumPreCommandBaselinePoints)
            return Blocked($"A2.1 V2 baseline exceeds {MaximumPreCommandBaselinePoints} points; refusing silent truncation.", evidence, identity, profile);

        var baseline = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var readFailures = 0;
        foreach (var point in preCommandPoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await session.ReadSingleVariableAsync(point.ToObjectReference(), cancellationToken).ConfigureAwait(false);
            if (!read.IsSuccess || read.Value is null)
            {
                readFailures++;
                evidence.Add($"G2.5-A2.1 V2 pre-command baseline read failed: ref={point.UserReference}; result={read.Message}");
                continue;
            }
            baseline[point.MmsReference] = NormalizeValue(ArMms.MmsDataValueRenderer.ToCompactString(read.Value));
        }
        if (!session.IsMmsInitiated || baseline.Count == 0)
            return Failed("A2.1 V2 could not capture a reliable pre-command baseline.", evidence, identity, profile, session.IsMmsInitiated, baselineCount: baseline.Count, readFailures: readFailures);

        evidence.Add($"G2.5-A2.1 V2 pre-command baseline captured: successful={baseline.Count}/{preCommandPoints.Length}; failures={readFailures}");
        evidence.Add("G2.5-A2.1 V2 resolved command status references: " + string.Join(" | ", signalStatusPoints.Select(pair => $"{pair.Key.ObjectReference} -> {pair.Value.UserReference}")));

        var commandCapture = new TaskCompletionSource<CommandCapture>(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler busyHandler = (sender, args) =>
        {
            if (args.PropertyName == nameof(SignalDefinition.ControlCommandBusy) &&
                sender is SignalDefinition signal && signal.ControlCommandBusy && signalStatusPoints.ContainsKey(signal))
            {
                commandCapture.TrySetResult(new CommandCapture(signal, "FastCommandPanel.ControlCommandBusy", signal.ControlPendingValue, DateTimeOffset.UtcNow));
            }
        };
        foreach (var signal in commandSignals)
            signal.PropertyChanged += busyHandler;

        using var intentSubscription = DynamicReportCommandIntentObservation.Subscribe(intent =>
        {
            if (!ReferenceEquals(intent.Device, device) &&
                !string.Equals(intent.Device.DeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase))
                return;

            var matched = commandSignals.FirstOrDefault(signal =>
                ReferenceEquals(signal, intent.Signal) || SameReference(signal.ObjectReference, intent.Signal.ObjectReference));
            if (matched is null || !signalStatusPoints.ContainsKey(matched))
                return;

            commandCapture.TrySetResult(new CommandCapture(matched, intent.Source, intent.RequestedValue, intent.ObservedAtUtc));
        });

        CommandCapture capture;
        try
        {
            progress?.Report($"{ReadyMarker} — baseline captured. NOW issue exactly ONE already-proven safe OPEN/CLOSE using either normal ARSAS control UI. Do not use an external stimulus.");
            evidence.Add($"{ReadyMarker}: waiting for fast-panel busy OR ControlCommandWindow observer intent; witness does not issue/delay command.");
            capture = await commandCapture.Task.WaitAsync(CommandWaitWindow, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            evidence.Add("G2.5-A2.1 V2 command wait timed out: neither ControlCommandBusy nor ControlCommandWindow observer intent was captured.");
            return Failed("A2.1 V2 timed out before an ARSAS command was captured. No stimulus conclusion is possible.", evidence, identity, profile, session.IsMmsInitiated, baselineCount: baseline.Count, readFailures: readFailures);
        }
        finally
        {
            foreach (var signal in commandSignals)
                signal.PropertyChanged -= busyHandler;
        }

        var commandedSignal = capture.Signal;
        var exactStatus = signalStatusPoints[commandedSignal];
        var focusPoints = DynamicReportCommandBoundStimulusWitnessService.BuildFocusChain(discovery.IedDirectory, exactStatus)
            .Take(MaximumFocusCandidates)
            .ToArray();

        evidence.Add($"{CommandCapturedMarker}: source={capture.Source}; requested={TextOrDash(capture.RequestedValue)}; signal={commandedSignal.ObjectReference}; controlStatus={commandedSignal.ControlStatusReference}; resolvedStatus={exactStatus.UserReference}; controlModel={TextOrDash(commandedSignal.ControlModelText)}; at={capture.ObservedAtUtc:O}");
        evidence.Add("G2.5-A2.1 V2 focused chain: " + string.Join(" | ", focusPoints.Select(point => point.UserReference)));
        progress?.Report($"{CommandCapturedMarker} — {commandedSignal.ObjectReference} via {capture.Source}. High-speed read-only sampling active; do NOT issue another command.");

        var trackers = new List<FocusTracker>();
        foreach (var point in focusPoints)
        {
            if (!baseline.TryGetValue(point.MmsReference, out var baselineValue))
            {
                // Do not invent a post-command baseline: it could erase a short physical pulse.
                evidence.Add($"G2.5-A2.1 V2 focus point excluded because no PRE-command baseline exists: {point.UserReference}");
                continue;
            }
            trackers.Add(new FocusTracker(point, baselineValue, SameReference(point.MmsReference, exactStatus.MmsReference)));
        }
        if (trackers.Count == 0)
            return Failed("Command captured, but no focused point had a trustworthy PRE-command baseline.", evidence, identity, profile, session.IsMmsInitiated, commandedSignal, baseline.Count, 0, 0, readFailures);

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
                if (SameValue(current, tracker.CurrentValue))
                    continue;

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
                evidence.Add($"G2.5-A2.1 V2 transition: exactStatus={tracker.ExactControlStatus}; ref={transition.Reference}; before={transition.BeforeValue}; after={transition.AfterValue}; at={transition.ObservedAtUtc:O}");
            }

            if (!session.IsMmsInitiated)
            {
                evidence.Add("G2.5-A2.1 V2 association left MmsInitiated during focused sampling.");
                break;
            }

            var first = trackers.SelectMany(t => t.Transitions).OrderBy(t => t.ObservedAtUtc).FirstOrDefault();
            if (first is not null && !transitionAnnounced)
            {
                transitionAnnounced = true;
                settleDeadline = DateTimeOffset.UtcNow + PostTransitionSettleWindow;
                evidence.Add($"{TransitionMarker}: first={first.Reference}; {first.BeforeValue}->{first.AfterValue}; settleUntil={settleDeadline:O}");
                progress?.Report($"{TransitionMarker} — {first.Reference}: {first.BeforeValue} → {first.AfterValue}. No more commands; classifying state behavior.");
            }
            if (settleDeadline.HasValue && DateTimeOffset.UtcNow >= settleDeadline.Value)
                break;
            if (InterCycleDelay > TimeSpan.Zero)
                await Task.Delay(InterCycleDelay, cancellationToken).ConfigureAwait(false);
        }

        var endedAt = DateTimeOffset.UtcNow;
        var observations = trackers.Select(t => ToObservation(t, endedAt)).ToArray();
        var eligible = observations
            .Where(o => o.TransitionCount > 0)
            .OrderByDescending(o => o.ExactControlStatus)
            .ThenByDescending(o => o.Kind == DynamicReportStimulusEligibilityKind.PersistentOrLatched)
            .ThenByDescending(o => o.TransitionCount)
            .ThenBy(o => o.Reference, StringComparer.OrdinalIgnoreCase)
            .Select((o, index) => WithRank(o, index + 1))
            .ToArray();

        foreach (var item in eligible)
            evidence.Add($"G2.5-A2.1 V2 ELIGIBLE: rank={item.Rank}; exactStatus={item.ExactControlStatus}; kind={item.Kind}; ref={item.Reference}; baseline={item.BaselineValue}; final={item.FinalValue}; transitions={item.TransitionCount}; activeMs={FormatMs(item.ObservedActiveMilliseconds)}");

        var healthy = session.IsMmsInitiated;
        var success = healthy && eligible.Length > 0;
        evidence.Add($"G2.5-A2.1 V2 combined: success={success}; commandCaptured=True; source={capture.Source}; baseline={baseline.Count}; focus={trackers.Count}; cycles={cycles}; readFailures={readFailures}; eligible={eligible.Length}; associationHealthy={healthy}");
        evidence.Add("G2.5-A2.1 V2 safety: profile remains InformationReportProven; production automatic dynamic reporting remains OFF.");

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
            Summary = success
                ? $"G2.5-A2.1 PASS: exact ARSAS command was captured via {capture.Source} and {eligible.Length} command-bound MMS transition candidate(s) were proven. Use the ranked evidence for narrow A3; production dynamic reporting remains OFF."
                : $"G2.5-A2.1 captured the exact ARSAS command via {capture.Source}, but no focused MMS transition was observed. Do not advance to A3/G2.5-B; production dynamic reporting remains OFF.",
            Identity = identity,
            InputProfile = profile,
            Observations = observations,
            EligibleCandidates = eligible,
            EvidenceLines = evidence.ToArray()
        };
    }

    private static DynamicReportCommandBoundObservation ToObservation(FocusTracker tracker, DateTimeOffset endedAt)
    {
        var kind = tracker.Transitions.Count == 0
            ? DynamicReportStimulusEligibilityKind.None
            : SameValue(tracker.CurrentValue, tracker.BaselineValue) && tracker.Transitions.Count >= 2
                ? DynamicReportStimulusEligibilityKind.MomentaryOrPulse
                : !SameValue(tracker.CurrentValue, tracker.BaselineValue)
                    ? DynamicReportStimulusEligibilityKind.PersistentOrLatched
                    : DynamicReportStimulusEligibilityKind.TransitionObserved;
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
            ObservedActiveMilliseconds = ActiveMilliseconds(tracker, endedAt),
            Transitions = tracker.Transitions.ToArray()
        };
    }

    private static double? ActiveMilliseconds(FocusTracker tracker, DateTimeOffset endedAt)
    {
        if (tracker.Transitions.Count == 0)
            return null;
        var departure = tracker.Transitions.FirstOrDefault(t => SameValue(t.BeforeValue, tracker.BaselineValue) && !SameValue(t.AfterValue, tracker.BaselineValue)) ?? tracker.Transitions[0];
        var returned = tracker.Transitions.FirstOrDefault(t => t.ObservedAtUtc >= departure.ObservedAtUtc && SameValue(t.AfterValue, tracker.BaselineValue));
        return ((returned?.ObservedAtUtc ?? endedAt) - departure.ObservedAtUtc).TotalMilliseconds;
    }

    private static DynamicReportCommandBoundObservation WithRank(DynamicReportCommandBoundObservation source, int rank) => new()
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

    private static string NormalizeValue(string? value) => (value ?? string.Empty).Trim();
    private static bool SameValue(string? a, string? b) => string.Equals(NormalizeValue(a), NormalizeValue(b), StringComparison.OrdinalIgnoreCase);
    private static bool SameReference(string? a, string? b) => string.Equals(NormalizeReference(a), NormalizeReference(b), StringComparison.OrdinalIgnoreCase);
    private static string NormalizeReference(string? value) => (value ?? string.Empty).Trim().Replace('$', '.');
    private static string TextOrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
    private static string FormatMs(double? value) => value.HasValue ? value.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "-";

    private static DynamicReportCommandBoundStimulusWitnessResult Blocked(
        string summary,
        IReadOnlyList<string> evidence,
        ArMms.MmsDynamicReportIedIdentity? identity = null,
        ArMms.MmsDynamicReportQualificationProfile? profile = null) => new()
    {
        IsBlocked = true,
        Summary = summary,
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
        int readFailures = 0) => new()
    {
        Summary = summary,
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

    private sealed record CommandCapture(SignalDefinition Signal, string Source, string RequestedValue, DateTimeOffset ObservedAtUtc);

    private sealed class FocusTracker(ArMms.MmsFcResolvedPoint point, string baselineValue, bool exactControlStatus)
    {
        internal ArMms.MmsFcResolvedPoint Point { get; } = point;
        internal string BaselineValue { get; } = baselineValue;
        internal string CurrentValue { get; set; } = baselineValue;
        internal bool ExactControlStatus { get; } = exactControlStatus;
        internal List<DynamicReportCommandBoundTransition> Transitions { get; } = new();
    }
}
