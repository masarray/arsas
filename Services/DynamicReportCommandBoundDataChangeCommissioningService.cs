using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

internal sealed class DynamicReportCommandBoundA3Transition
{
    public int Index { get; init; }
    public string MemberReference { get; init; } = string.Empty;
    public string PointReference { get; init; } = string.Empty;
    public string BeforeValue { get; init; } = string.Empty;
    public string AfterValue { get; init; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; init; }
}

internal sealed class DynamicReportCommandBoundA3WitnessResult
{
    public bool BaselineCaptured { get; init; }
    public bool CommandCaptured { get; init; }
    public bool CommandBoundTransitionProven { get; init; }
    public bool AssociationHealthy { get; init; }
    public string CommandSignalReference { get; init; } = string.Empty;
    public string ControlStatusReference { get; init; } = string.Empty;
    public string RequestedValue { get; init; } = string.Empty;
    public string CommandSource { get; init; } = string.Empty;
    public DateTimeOffset? CommandObservedAtUtc { get; init; }
    public int SampleCycles { get; init; }
    public int ReadFailures { get; init; }
    public IReadOnlyList<DynamicReportCommandBoundA3Transition> Transitions { get; init; } = Array.Empty<DynamicReportCommandBoundA3Transition>();
    public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();
    public string Summary { get; init; } = string.Empty;
}

internal sealed class DynamicReportCommandBoundA3CommissioningResult
{
    public bool IsSuccess { get; init; }
    public bool IsBlocked { get; init; }
    public bool CommandBoundReportCorrelationProven { get; init; }
    public bool NativeControlAcceptanceProven { get; init; }
    public bool ReportAfterCommandProven { get; init; }
    public DateTimeOffset? NativeControlAcceptedAtUtc { get; init; }
    public IReadOnlyList<int> CorrelatedIndexes { get; init; } = Array.Empty<int>();
    public IReadOnlyList<string> CorrelatedMemberReferences { get; init; } = Array.Empty<string>();
    public DynamicReportSpontaneousDataChangeCommissioningResult CoreResult { get; init; } = new();
    public DynamicReportCommandBoundA3WitnessResult Witness { get; init; } = new();
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();
}

internal sealed record DynamicReportCommandBoundA3EligibleTarget(
    SignalDefinition Signal,
    ArMms.MmsFcResolvedPoint ExactStatusPoint,
    IReadOnlyList<ArMms.MmsFcResolvedPoint> QualifiedFocusPoints,
    IReadOnlyList<int> QualifiedIndexes);

/// <summary>
/// G2.6-P1 deterministic A3 wrapper.
///
/// The reporting path remains the existing G2.5-A one-URCB dchg-only / NO-GI transaction.
/// A second isolated MMS association is read-only and is used only to prove that the exact
/// pre-existing ARSAS control command caused a transition on a member that belongs to the
/// exact G2.4-proven DataSet envelope. The command itself remains owned by the existing
/// Iec61850MonitorRuntime control path; this service observes the runtime request plus the
/// later successful native-control diagnostic and never calls ExecuteControlAsync.
///
/// PASS therefore requires all of the following in one bounded armed window:
/// - exact InformationReportProven identity/profile and G2.4 RCB/member sequence;
/// - at least one ARSAS control object whose A2.1 focus chain intersects that exact sequence;
/// - core dchg-only activation/report/cleanup success with GI disabled;
/// - one exact runtime-observed ARSAS command after the witness baseline is ready;
/// - later successful native control-result/wire evidence for that exact request;
/// - a post-command MMS transition on a qualified command-focus member;
/// - the dchg InformationReport was received strictly after the captured command and includes the same DataSet index.
///
/// This service never saves or advances the qualification profile and cannot set
/// ProductionEligible. Production automatic dynamic reporting remains a later gate.
/// </summary>
internal sealed class DynamicReportCommandBoundDataChangeCommissioningService
{
    internal const string ReadyMarker = "G2.6-P1 A3 READY — ISSUE ONE ARSAS COMMAND";
    internal const string CommandCapturedMarker = "G2.6-P1 A3 COMMAND CAPTURED";
    internal const string TransitionMarker = "G2.6-P1 A3 COMMAND-BOUND TRANSITION";
    internal const string NativeAcceptedMarker = "G2.6-P1 A3 NATIVE CONTROL ACCEPTED";
    internal static readonly TimeSpan AuxiliaryAssociationTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan CommandWaitWindow = TimeSpan.FromSeconds(45);
    internal static readonly TimeSpan CommandTransitionWindow = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan PostTransitionSettleWindow = TimeSpan.FromMilliseconds(350);
    internal static readonly TimeSpan InterCycleDelay = TimeSpan.FromMilliseconds(1);

    private readonly DynamicReportQualificationProfileStore _profileStore;

    public DynamicReportCommandBoundDataChangeCommissioningService(
        DynamicReportQualificationProfileStore? profileStore = null)
    {
        _profileStore = profileStore ?? new DynamicReportQualificationProfileStore();
    }

    public async Task<DynamicReportCommandBoundA3CommissioningResult> RunAsync(
        Iec61850MonitorRuntime runtime,
        Iec61850MonitorDevice device,
        IReadOnlyList<SignalDefinition> fullModelSignals,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fullModelSignals);

        var evidence = new List<string>
        {
            "G2.6-P1 A3 contract: exact existing ARSAS command -> accepted native MMS control result -> read-only command-bound qualified-member transition -> post-command dchg InformationReport on the same DataSet index -> mandatory G2.5-A cleanup.",
            "G2.6-P1 A3 control safety: this service never calls ExecuteControlAsync and never writes SBO/SBOw/Operate/Cancel; command authority remains the existing Iec61850MonitorRuntime path. Request diagnostics alone cannot prove PASS.",
            "G2.6-P1 A3 report safety: core path is strict dchg-only with GI=false, integrity=false, qchg=false and dupd=false. The selected valid report receive timestamp must be strictly after the captured command time.",
            "G2.6-P1 A3 profile safety: persisted InformationReportProven evidence is read-only; this service cannot save, advance or mark ProductionEligible."
        };

        ArMms.MmsDynamicReportIedIdentity identity;
        try
        {
            identity = DynamicReportQualificationIdentity.Build(device, fullModelSignals);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Blocked("A3 identity preflight failed: " + ex.Message, evidence);
        }

        var loaded = await _profileStore.LoadAsync(identity, cancellationToken).ConfigureAwait(false);
        evidence.Add($"G2.6-P1 A3 profile: exists={loaded.Exists}; valid={loaded.IsValid}; state={loaded.Profile?.State.ToString() ?? "-"}; reason={loaded.Reason}");
        if (!loaded.IsValid || loaded.Profile is null ||
            loaded.Profile.State != ArMms.MmsDynamicReportQualificationState.InformationReportProven ||
            loaded.Profile.RcbActivationProof?.IsSuccess != true ||
            loaded.Profile.InformationReportProof?.IsSuccess != true)
        {
            return Blocked("A3 requires the exact identity-compatible InformationReportProven G2.4 profile.", evidence);
        }

        var profile = loaded.Profile;
        var qualifiedReferences = profile.RcbActivationProof.MemberReferences.ToArray();
        if (qualifiedReferences.Length == 0)
            return Blocked("A3 profile contains no exact G2.4 member sequence.", evidence);

        var commandSignals = fullModelSignals
            .Where(signal => signal.IsControlSignal && !string.IsNullOrWhiteSpace(signal.ControlStatusReference))
            .Distinct()
            .ToArray();
        if (commandSignals.Length == 0)
            return Blocked("No live control object exposes ControlStatusReference; A3 will not guess command/status correlation.", evidence);
        if (commandSignals.Any(signal => signal.ControlCommandBusy))
            return Blocked("A control command is already in progress. A3 must be armed before the one test command starts.", evidence);

        await using var witnessSession = new ArMms.MmsClientSession();
        ArMms.MmsDiscoveryResult witnessDiscovery;
        try
        {
            await witnessSession.ConnectAsync(
                device.IpAddress,
                device.Port,
                AuxiliaryAssociationTimeout,
                cancellationToken).ConfigureAwait(false);
            evidence.Add($"G2.6-P1 A3 witness association ready: state={witnessSession.State}; localTcpAddress={TextOrDash(witnessSession.LocalTcpAddress)}; READ-ONLY=true");

            witnessDiscovery = await witnessSession.DiscoverAsync(
                probeReportAttributes: false,
                maxReportAttributeProbes: 0,
                cancellationToken: cancellationToken,
                readDataSetDirectories: false,
                maxDataSetDirectoryReads: 0).ConfigureAwait(false);
            evidence.Add("G2.6-P1 A3 witness discovery: " + witnessDiscovery.Summary);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException)
        {
            evidence.Add($"G2.6-P1 A3 witness preflight exception: {ex.GetType().Name}: {ex.Message}");
            return Blocked("A3 could not establish its isolated read-only MMS witness association.", evidence);
        }

        if (!DynamicReportActivationCommissioningService.TryResolveExactQualifiedMembers(
                witnessDiscovery.IedDirectory,
                qualifiedReferences,
                out var exactQualifiedPoints,
                out var memberReason))
        {
            evidence.Add("G2.6-P1 A3 exact member resolution failed: " + memberReason);
            return Blocked("The exact G2.4-proven member sequence no longer resolves on the live IED.", evidence);
        }

        var eligibleTargets = BuildEligibleCommandTargets(
            witnessDiscovery.IedDirectory,
            commandSignals,
            qualifiedReferences,
            evidence);
        if (eligibleTargets.Count == 0)
        {
            evidence.Add("G2.6-P1 A3 preflight: no command focus chain intersects the exact G2.4 DataSet envelope. No RCB mutation was attempted.");
            return Blocked(
                "No current ARSAS command has a command-bound A2.1 status candidate inside the exact G2.4-proven member envelope. Re-qualify an envelope containing CSWI/XCBR status before A3.",
                evidence);
        }

        evidence.Add("G2.6-P1 A3 eligible commands: " + string.Join(" | ", eligibleTargets.Select(target =>
            $"{target.Signal.ObjectReference} -> status={target.ExactStatusPoint.UserReference}; qualifiedIndexes=[{string.Join(",", target.QualifiedIndexes)}]")));

        var armed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var commandCapture = new TaskCompletionSource<DynamicReportObservedCommandIntent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var nativeCommandAcceptance = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        var witnessReady = 0;

        void RuntimeDiagnosticHandler(DiagnosticEntry entry)
        {
            if (Volatile.Read(ref witnessReady) == 1 &&
                DynamicReportCommandBoundStimulusWitnessServiceV3.TryBuildRuntimeIntent(
                    entry,
                    device,
                    fullModelSignals,
                    out var intent) && intent is not null &&
                eligibleTargets.Any(target => ReferenceEquals(target.Signal, intent.Signal) ||
                                              SameReference(target.Signal.ObjectReference, intent.Signal.ObjectReference)))
            {
                commandCapture.TrySetResult(intent);
            }

            if (!commandCapture.Task.IsCompletedSuccessfully)
                return;

            var captured = commandCapture.Task.Result;
            if (!IsAcceptedNativeControlResultDiagnostic(entry, captured))
                return;

            nativeCommandAcceptance.TrySetResult(ToUtc(entry.Time));
        }

        runtime.Diagnostic += RuntimeDiagnosticHandler;
        using var witnessCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var relay = new RelayProgress(text =>
        {
            if (text.Contains(DynamicReportStimulusWitnessCommissioningService.ArmedMarker, StringComparison.OrdinalIgnoreCase))
            {
                armed.TrySetResult(true);
                progress?.Report("G2.6-P1 A3: dchg-only report path is ARMED with NO GI; capturing the final pre-command read-only baseline…");
                return;
            }
            progress?.Report(text);
        });

        var witnessTask = RunCommandWitnessAsync(
            witnessSession,
            exactQualifiedPoints,
            qualifiedReferences,
            eligibleTargets,
            armed.Task,
            commandCapture.Task,
            ready => Volatile.Write(ref witnessReady, ready ? 1 : 0),
            progress,
            witnessCancellation.Token);

        DynamicReportSpontaneousDataChangeCommissioningResult coreResult;
        try
        {
            var coreService = new DynamicReportSpontaneousDataChangeCommissioningService(_profileStore);
            coreResult = await coreService.RunAsync(
                device,
                fullModelSignals,
                relay,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref witnessReady, 0);
            runtime.Diagnostic -= RuntimeDiagnosticHandler;
            if (!armed.Task.IsCompleted)
                witnessCancellation.Cancel();
        }

        DynamicReportCommandBoundA3WitnessResult witnessResult;
        try
        {
            witnessResult = await witnessTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            witnessResult = new DynamicReportCommandBoundA3WitnessResult
            {
                Summary = "A3 command witness was cancelled because the core report transaction never reached ARMED.",
                EvidenceLines = ["G2.6-P1 A3 witness: core path did not reach ARMED; no command-bound conclusion is possible."]
            };
        }

        evidence.AddRange(coreResult.EvidenceLines.Select(line => "CORE/" + line));
        evidence.AddRange(witnessResult.EvidenceLines.Select(line => "WITNESS/" + line));

        var nativeControlAccepted = nativeCommandAcceptance.Task.IsCompletedSuccessfully;
        var nativeAcceptedAtUtc = nativeControlAccepted ? nativeCommandAcceptance.Task.Result : (DateTimeOffset?)null;
        if (nativeControlAccepted)
            evidence.Add($"{NativeAcceptedMarker}: object={witnessResult.CommandSignalReference}; requested={witnessResult.RequestedValue}; acceptedAt={nativeAcceptedAtUtc:O}; source=Iec61850MonitorRuntime successful native-control diagnostic.");
        else if (witnessResult.CommandCaptured)
            evidence.Add("G2.6-P1 A3 native control acceptance: NOT PROVEN. A request diagnostic alone is insufficient; rejected/NotSent/ambiguous control cannot satisfy PASS.");

        var reportAfterCommand = witnessResult.CommandObservedAtUtc.HasValue &&
                                 coreResult.ReportReceivedAtUtc.HasValue &&
                                 coreResult.ReportReceivedAtUtc.Value > witnessResult.CommandObservedAtUtc.Value;
        evidence.Add($"G2.6-P1 A3 report ordering: commandAt={witnessResult.CommandObservedAtUtc?.ToString("O") ?? "-"}; reportReceivedAt={coreResult.ReportReceivedAtUtc?.ToString("O") ?? "-"}; strictlyAfterCommand={reportAfterCommand}.");

        var changedIndexes = witnessResult.Transitions
            .Select(transition => transition.Index)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();
        var correlatedIndexes = CorrelateIndexes(coreResult.IncludedIndexes, changedIndexes);
        var correlatedMembers = correlatedIndexes
            .Where(index => index >= 0 && index < qualifiedReferences.Length)
            .Select(index => qualifiedReferences[index])
            .ToArray();

        var correlation = coreResult.SpontaneousDataChangeProven &&
                          witnessResult.CommandCaptured &&
                          nativeControlAccepted &&
                          witnessResult.CommandBoundTransitionProven &&
                          reportAfterCommand &&
                          correlatedIndexes.Length > 0;
        var success = coreResult.IsSuccess && correlation;

        string diagnosis;
        if (success)
        {
            diagnosis = $"G2.6-P1 A3 PASS: exact ARSAS command {witnessResult.CommandSignalReference} had successful native control evidence, produced a command-bound transition, and a later dchg InformationReport included the same exact DataSet index(es) [{string.Join(",", correlatedIndexes)}]; monitor/proof-field/fresh-association cleanup all passed.";
        }
        else if (!coreResult.ActivationProven)
        {
            diagnosis = "A3 did not reach a proven dchg-only ARMED state; command/report correlation is inconclusive.";
        }
        else if (!witnessResult.CommandCaptured)
        {
            diagnosis = "A3 report path armed, but no eligible existing ARSAS command was captured after the read-only baseline became ready.";
        }
        else if (!nativeControlAccepted)
        {
            diagnosis = "A3 captured a control request, but successful native MMS control-result evidence for that exact request was not observed. Request intent alone cannot prove command acceptance.";
        }
        else if (!witnessResult.CommandBoundTransitionProven)
        {
            diagnosis = "A3 captured and natively accepted the exact ARSAS command, but no qualified command-focus member changed in the bounded high-speed witness window.";
        }
        else if (!coreResult.SpontaneousDataChangeProven)
        {
            diagnosis = $"A3 captured an accepted command and witnessed qualified DataSet index(es) [{string.Join(",", changedIndexes)}] change, but no valid dchg InformationReport arrived. This isolates the remaining fault to dchg/report emission or receive-path evidence.";
        }
        else if (!reportAfterCommand)
        {
            diagnosis = $"A3 received a valid dchg report at {coreResult.ReportReceivedAtUtc?.ToString("O") ?? "<unknown>"}, but it was not received strictly after the captured command at {witnessResult.CommandObservedAtUtc?.ToString("O") ?? "<unknown>"}. Pre-command report traffic cannot satisfy command-bound A3.";
        }
        else if (correlatedIndexes.Length == 0)
        {
            diagnosis = $"A3 received a valid post-command dchg report, but its included indexes [{string.Join(",", coreResult.IncludedIndexes)}] did not match command-bound changed indexes [{string.Join(",", changedIndexes)}].";
        }
        else
        {
            diagnosis = "A3 command/report correlation did not close every required gate.";
        }

        evidence.Add($"G2.6-P1 A3 combined: coreSuccess={coreResult.IsSuccess}; activation={coreResult.ActivationProven}; dchg={coreResult.SpontaneousDataChangeProven}; cleanup={coreResult.MonitorCleanupSucceeded}/{coreResult.ProofFieldRestoreSucceeded}/{coreResult.FreshCleanupClosureSucceeded}; command={witnessResult.CommandCaptured}; nativeAccepted={nativeControlAccepted}; commandTransition={witnessResult.CommandBoundTransitionProven}; reportAfterCommand={reportAfterCommand}; changed=[{string.Join(",", changedIndexes)}]; reportIncluded=[{string.Join(",", coreResult.IncludedIndexes)}]; correlated=[{string.Join(",", correlatedIndexes)}]; success={success}");
        evidence.Add("G2.6-P1 A3 diagnosis: " + diagnosis);
        evidence.Add("G2.6-P1 A3 state: profile remains InformationReportProven. Production automatic dynamic reporting remains OFF; shadow/regression acceptance is still required before ProductionEligible.");

        return new DynamicReportCommandBoundA3CommissioningResult
        {
            IsSuccess = success,
            CommandBoundReportCorrelationProven = correlation,
            NativeControlAcceptanceProven = nativeControlAccepted,
            ReportAfterCommandProven = reportAfterCommand,
            NativeControlAcceptedAtUtc = nativeAcceptedAtUtc,
            CorrelatedIndexes = correlatedIndexes,
            CorrelatedMemberReferences = correlatedMembers,
            CoreResult = coreResult,
            Witness = witnessResult,
            Summary = diagnosis + " Profile remains InformationReportProven; production dynamic reporting remains OFF.",
            EvidenceLines = evidence.ToArray()
        };
    }

    internal static IReadOnlyList<DynamicReportCommandBoundA3EligibleTarget> BuildEligibleCommandTargets(
        ArMms.MmsIedModelDirectory directory,
        IReadOnlyList<SignalDefinition> commandSignals,
        IReadOnlyList<string> qualifiedReferences,
        ICollection<string>? evidence = null)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(commandSignals);
        ArgumentNullException.ThrowIfNull(qualifiedReferences);

        var qualifiedIndex = qualifiedReferences
            .Select((reference, index) => new { Key = NormalizeMms(reference), Index = index })
            .Where(item => item.Key.Length > 0)
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);

        var statusPoints = DynamicReportCommandBoundStimulusWitnessService.ResolveCommandStatusPoints(
            directory,
            commandSignals,
            evidence);
        var result = new List<DynamicReportCommandBoundA3EligibleTarget>();

        foreach (var pair in statusPoints)
        {
            var qualifiedFocus = DynamicReportCommandBoundStimulusWitnessService
                .BuildFocusChain(directory, pair.Value)
                .Where(point => qualifiedIndex.ContainsKey(NormalizeMms(point.MmsReference)))
                .GroupBy(point => NormalizeMms(point.MmsReference), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
            if (qualifiedFocus.Length == 0)
                continue;

            var indexes = qualifiedFocus
                .Select(point => qualifiedIndex[NormalizeMms(point.MmsReference)])
                .Distinct()
                .OrderBy(index => index)
                .ToArray();
            result.Add(new DynamicReportCommandBoundA3EligibleTarget(pair.Key, pair.Value, qualifiedFocus, indexes));
        }

        return result
            .OrderBy(target => target.Signal.ObjectReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static int[] CorrelateIndexes(
        IEnumerable<int> reportIncludedIndexes,
        IEnumerable<int> commandBoundChangedIndexes)
    {
        ArgumentNullException.ThrowIfNull(reportIncludedIndexes);
        ArgumentNullException.ThrowIfNull(commandBoundChangedIndexes);
        return reportIncludedIndexes
            .Intersect(commandBoundChangedIndexes)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();
    }

    private static bool IsAcceptedNativeControlResultDiagnostic(
        DiagnosticEntry entry,
        DynamicReportObservedCommandIntent command)
    {
        if (!entry.Level.Equals("INFO", StringComparison.OrdinalIgnoreCase))
            return false;

        var message = entry.Message ?? string.Empty;
        if (!message.StartsWith("Control ", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!message.Contains($": {command.Signal.ObjectReference};", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!message.Contains($"requested={command.RequestedValue};", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!message.Contains("wire=", StringComparison.OrdinalIgnoreCase))
            return false;
        if (message.Contains("NOT SENT TO IED", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("no response captured", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("no wire evidence returned", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static DateTimeOffset ToUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
            return new DateTimeOffset(value);
        if (value.Kind == DateTimeKind.Local)
            return new DateTimeOffset(value).ToUniversalTime();
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Local)).ToUniversalTime();
    }

    private static async Task<DynamicReportCommandBoundA3WitnessResult> RunCommandWitnessAsync(
        ArMms.MmsClientSession session,
        IReadOnlyList<ArMms.MmsFcResolvedPoint> exactQualifiedPoints,
        IReadOnlyList<string> qualifiedReferences,
        IReadOnlyList<DynamicReportCommandBoundA3EligibleTarget> eligibleTargets,
        Task armedSignal,
        Task<DynamicReportObservedCommandIntent> commandSignal,
        Action<bool> setReady,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var evidence = new List<string>();
        try
        {
            await armedSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
            var baseline = await ReadValuesAsync(session, exactQualifiedPoints, cancellationToken).ConfigureAwait(false);
            if (!baseline.IsSuccess || !session.IsMmsInitiated)
            {
                evidence.Add("A3 final pre-command baseline failed: " + baseline.Message);
                return WitnessFailure("A3 could not capture a complete final pre-command qualified-member baseline.", evidence, session.IsMmsInitiated, baseline.ReadFailures);
            }

            evidence.Add("A3 final pre-command baseline: " + string.Join(" | ", qualifiedReferences.Select((reference, index) => $"[{index}] {reference}={baseline.Values[index]}")));
            evidence.Add("A3 eligible command objects: " + string.Join(" | ", eligibleTargets.Select(target => target.Signal.ObjectReference)));
            setReady(true);
            progress?.Report($"{ReadyMarker} — issue exactly ONE already-proven safe OPEN/CLOSE using normal ARSAS control. Eligible object(s): {string.Join(", ", eligibleTargets.Select(target => target.Signal.ObjectReference))}. Do not issue an external/manual stimulus.");

            DynamicReportObservedCommandIntent command;
            try
            {
                command = await commandSignal.WaitAsync(CommandWaitWindow, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                evidence.Add("A3 command wait timed out after the witness baseline was ready.");
                return WitnessFailure("No eligible existing ARSAS command was captured in the bounded A3 command window.", evidence, session.IsMmsInitiated, baseline: baseline.Values);
            }
            finally
            {
                setReady(false);
            }

            var target = eligibleTargets.First(item => ReferenceEquals(item.Signal, command.Signal) ||
                                                       SameReference(item.Signal.ObjectReference, command.Signal.ObjectReference));
            evidence.Add($"{CommandCapturedMarker}: object={command.Signal.ObjectReference}; requested={command.RequestedValue}; status={command.Signal.ControlStatusReference}; source={command.Source}; at={command.ObservedAtUtc:O}; qualifiedFocus=[{string.Join(",", target.QualifiedIndexes)}]");
            progress?.Report($"{CommandCapturedMarker} — {command.Signal.ObjectReference} requested={command.RequestedValue}. High-speed read-only sampling is active; do NOT issue another command.");

            var focus = target.QualifiedFocusPoints
                .Select(point => new
                {
                    Point = point,
                    Index = Array.FindIndex(qualifiedReferences.ToArray(), reference => SameMms(reference, point.MmsReference))
                })
                .Where(item => item.Index >= 0)
                .ToArray();
            if (focus.Length == 0)
                return WitnessFailure("Captured command lost its qualified A2.1 focus intersection before sampling.", evidence, session.IsMmsInitiated, baseline: baseline.Values, command: command);

            var deadline = DateTimeOffset.UtcNow + CommandTransitionWindow;
            DateTimeOffset? settleDeadline = null;
            var cycles = 0;
            var failures = baseline.ReadFailures;
            var transitions = new List<DynamicReportCommandBoundA3Transition>();
            var currentValues = baseline.Values.ToArray();

            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                cycles++;
                foreach (var item in focus)
                {
                    var read = await session.ReadSingleVariableAsync(item.Point.ToObjectReference(), cancellationToken).ConfigureAwait(false);
                    if (!read.IsSuccess || read.Value is null)
                    {
                        failures++;
                        continue;
                    }

                    var current = NormalizeValue(ArMms.MmsDataValueRenderer.ToCompactString(read.Value));
                    if (string.Equals(currentValues[item.Index], current, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var transition = new DynamicReportCommandBoundA3Transition
                    {
                        Index = item.Index,
                        MemberReference = qualifiedReferences[item.Index],
                        PointReference = item.Point.UserReference,
                        BeforeValue = currentValues[item.Index],
                        AfterValue = current,
                        ObservedAtUtc = DateTimeOffset.UtcNow
                    };
                    currentValues[item.Index] = current;
                    transitions.Add(transition);
                    evidence.Add($"{TransitionMarker}: index={transition.Index}; member={transition.MemberReference}; point={transition.PointReference}; before={transition.BeforeValue}; after={transition.AfterValue}; commandAt={command.ObservedAtUtc:O}; observedAt={transition.ObservedAtUtc:O}; deltaMs={(transition.ObservedAtUtc - command.ObservedAtUtc).TotalMilliseconds:0.###}");
                    settleDeadline ??= transition.ObservedAtUtc + PostTransitionSettleWindow;
                }

                if (!session.IsMmsInitiated)
                    break;
                if (settleDeadline.HasValue && DateTimeOffset.UtcNow >= settleDeadline.Value)
                    break;
                if (InterCycleDelay > TimeSpan.Zero)
                    await Task.Delay(InterCycleDelay, cancellationToken).ConfigureAwait(false);
            }

            var postCommand = transitions
                .Where(transition => transition.ObservedAtUtc >= command.ObservedAtUtc)
                .ToArray();
            var proven = postCommand.Length > 0 && session.IsMmsInitiated;
            evidence.Add($"A3 witness result: commandCaptured=true; transitions={transitions.Count}; postCommand={postCommand.Length}; cycles={cycles}; readFailures={failures}; associationHealthy={session.IsMmsInitiated}; proven={proven}");

            return new DynamicReportCommandBoundA3WitnessResult
            {
                BaselineCaptured = true,
                CommandCaptured = true,
                CommandBoundTransitionProven = proven,
                AssociationHealthy = session.IsMmsInitiated,
                CommandSignalReference = command.Signal.ObjectReference,
                ControlStatusReference = command.Signal.ControlStatusReference,
                RequestedValue = command.RequestedValue,
                CommandSource = command.Source,
                CommandObservedAtUtc = command.ObservedAtUtc,
                SampleCycles = cycles,
                ReadFailures = failures,
                Transitions = postCommand,
                EvidenceLines = evidence.ToArray(),
                Summary = proven
                    ? $"A3 witnessed {postCommand.Length} qualified command-bound transition(s) after the exact existing ARSAS command."
                    : "A3 captured the exact ARSAS command but did not witness a qualified post-command transition."
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException)
        {
            evidence.Add($"A3 witness exception: {ex.GetType().Name}: {ex.Message}");
            return WitnessFailure("A3 read-only command witness failed before a conclusive transition proof.", evidence, session.IsMmsInitiated);
        }
        finally
        {
            setReady(false);
        }
    }

    private static async Task<ReadBatch> ReadValuesAsync(
        ArMms.MmsClientSession session,
        IReadOnlyList<ArMms.MmsFcResolvedPoint> points,
        CancellationToken cancellationToken)
    {
        var values = new string[points.Count];
        var failures = 0;
        for (var index = 0; index < points.Count; index++)
        {
            var read = await session.ReadSingleVariableAsync(points[index].ToObjectReference(), cancellationToken).ConfigureAwait(false);
            if (!read.IsSuccess || read.Value is null)
            {
                failures++;
                values[index] = "<read-failed>";
                continue;
            }
            values[index] = NormalizeValue(ArMms.MmsDataValueRenderer.ToCompactString(read.Value));
        }

        return new ReadBatch
        {
            IsSuccess = failures == 0,
            ReadFailures = failures,
            Values = values,
            Message = failures == 0 ? "all reads succeeded" : $"{failures} of {points.Count} reads failed"
        };
    }

    private static DynamicReportCommandBoundA3WitnessResult WitnessFailure(
        string summary,
        IReadOnlyList<string> evidence,
        bool associationHealthy,
        int readFailures = 0,
        IReadOnlyList<string>? baseline = null,
        DynamicReportObservedCommandIntent? command = null)
        => new()
        {
            BaselineCaptured = baseline is { Count: > 0 },
            CommandCaptured = command is not null,
            AssociationHealthy = associationHealthy,
            CommandSignalReference = command?.Signal.ObjectReference ?? string.Empty,
            ControlStatusReference = command?.Signal.ControlStatusReference ?? string.Empty,
            RequestedValue = command?.RequestedValue ?? string.Empty,
            CommandSource = command?.Source ?? string.Empty,
            CommandObservedAtUtc = command?.ObservedAtUtc,
            ReadFailures = readFailures,
            Summary = summary,
            EvidenceLines = evidence.ToArray()
        };

    private static DynamicReportCommandBoundA3CommissioningResult Blocked(
        string summary,
        IReadOnlyList<string> evidence)
        => new()
        {
            IsBlocked = true,
            Summary = summary + " Production automatic dynamic reporting remains OFF.",
            EvidenceLines = evidence.ToArray()
        };

    private static string NormalizeMms(string? reference)
        => ArMms.MmsFcReferenceNormalizer.NormalizeMmsReference(reference ?? string.Empty);

    private static bool SameMms(string? left, string? right)
        => NormalizeMms(left).Equals(NormalizeMms(right), StringComparison.OrdinalIgnoreCase);

    private static bool SameReference(string? left, string? right)
        => string.Equals((left ?? string.Empty).Trim().Replace('.', '$'), (right ?? string.Empty).Trim().Replace('.', '$'), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private sealed class RelayProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    private sealed class ReadBatch
    {
        public bool IsSuccess { get; init; }
        public int ReadFailures { get; init; }
        public IReadOnlyList<string> Values { get; init; } = Array.Empty<string>();
        public string Message { get; init; } = string.Empty;
    }
}