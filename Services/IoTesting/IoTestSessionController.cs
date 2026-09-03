using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed class IoTestSessionController : ObservableObject, IDisposable
{
    private const int MaxSnapshotsPerDrain = 64;
    private const int DrainBudgetMilliseconds = 4;
    private static readonly string CurrentApplicationVersion =
        typeof(IoTestSessionController).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(IoTestSessionController).Assembly.GetName().Version?.ToString()
        ?? "unknown";
    private readonly IoTestProject _project;
    private readonly Func<string, Iec61850MonitorDevice?> _deviceResolver;
    private readonly Action<Action> _dispatch;
    private readonly Func<IoTestProject, IoTestIedPlan, Guid, DateTimeOffset, IIoTestEvidenceJournal> _journalFactory;
    private readonly IoTestTransitionEvaluator _evaluator;
    private readonly IoTestRollingCaptureCoordinator _captureCoordinator;
    private readonly FatAutoCaptureCoordinator _autoCaptureCoordinator = new();
    // Only the newest observation per live point is needed by the UI transition
    // evaluator. Coalescing prevents a fast report/poll stream from building an
    // unbounded dispatcher backlog while the operator scrolls the FAT grid.
    private readonly ConcurrentDictionary<string, QueuedSnapshot> _pendingSnapshots = new(StringComparer.OrdinalIgnoreCase);
    // Actual transitions are never coalesced: FAT evidence must retain a fast
    // OFF→ON→OFF sequence even when all three samples arrive in one UI frame.
    private readonly ConcurrentQueue<QueuedSnapshot> _pendingEdgeSnapshots = new();
    private readonly Dictionary<string, List<IoTestPointPlan>> _activePointIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Iec61850MonitorPoint> _activeLivePoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sessionPointIds = new(StringComparer.OrdinalIgnoreCase);
    // Value 1 & Value 2 bulk Recapture is deliberately two-stage. The staged Value 1
    // remains audit history only until Value 2 is captured successfully; current pointers
    // therefore keep the old complete pair authoritative during the condition change.
    private readonly Dictionary<string, FatValueEvidence> _pendingPairValue1 = new(StringComparer.OrdinalIgnoreCase);

    private IoTestSessionState _state = IoTestSessionState.Idle;
    private IoTestIedPlan? _activeIed;
    private Iec61850MonitorDevice? _activeDevice;
    private Guid _sessionId;
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset? _completedAtUtc;
    private string _statusText = "Select an imported IED and start its FAT session.";
    private string _journalPath = string.Empty;
    private long _connectionGeneration = 1;
    private int _drainScheduled;
    private IIoTestEvidenceJournal? _journal;
    private bool _disposed;
    private long _evidenceRecordCount;
    private string _lastJournalHash = string.Empty;
    private string _journalIntegrityText = "No evidence journal open";

    public IoTestSessionController(
        IoTestProject project,
        Func<string, Iec61850MonitorDevice?> deviceResolver,
        Action<Action> dispatch,
        string journalRootDirectory,
        IoTestTransitionEvaluator? evaluator = null,
        Func<IoTestProject, IoTestIedPlan, Guid, DateTimeOffset, IIoTestEvidenceJournal>? journalFactory = null)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _deviceResolver = deviceResolver ?? throw new ArgumentNullException(nameof(deviceResolver));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        ArgumentException.ThrowIfNullOrWhiteSpace(journalRootDirectory);
        _evaluator = evaluator ?? new IoTestTransitionEvaluator();
        _captureCoordinator = new IoTestRollingCaptureCoordinator(_evaluator);
        _journalFactory = journalFactory ?? ((testProject, ied, sessionId, startedAt) =>
            IoTestEvidenceJournal.Create(journalRootDirectory, testProject, ied, sessionId, startedAt));
    }

    public IoTestProject Project => _project;
    public IoTestSessionState State { get => _state; private set { if (Set(ref _state, value)) RaiseStateProperties(); } }
    public IoTestIedPlan? ActiveIed { get => _activeIed; private set { if (Set(ref _activeIed, value)) RaiseStateProperties(); } }
    public Guid SessionId { get => _sessionId; private set => Set(ref _sessionId, value); }
    public DateTimeOffset? StartedAtUtc { get => _startedAtUtc; private set => Set(ref _startedAtUtc, value); }
    public DateTimeOffset? CompletedAtUtc { get => _completedAtUtc; private set => Set(ref _completedAtUtc, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value ?? string.Empty); }
    public string JournalPath { get => _journalPath; private set => Set(ref _journalPath, value ?? string.Empty); }
    public long EvidenceRecordCount { get => _evidenceRecordCount; private set => Set(ref _evidenceRecordCount, value); }
    public string LastJournalHash { get => _lastJournalHash; private set => Set(ref _lastJournalHash, value ?? string.Empty); }
    public string JournalIntegrityText { get => _journalIntegrityText; private set => Set(ref _journalIntegrityText, value ?? string.Empty); }
    public bool IsSessionActive => State is IoTestSessionState.Running or IoTestSessionState.Paused or IoTestSessionState.Interrupted;
    public bool CanStart => !IsSessionActive;
    public bool CanPause => State == IoTestSessionState.Running;
    public bool CanResume => State is IoTestSessionState.Paused or IoTestSessionState.Interrupted;
    public bool CanStop => IsSessionActive;
    public bool CanSelectIed => !IsSessionActive;
    public bool CanEditPlan => !IsSessionActive;
    public string StateText => State switch
    {
        IoTestSessionState.Idle => "READY",
        IoTestSessionState.Running => "RUNNING",
        IoTestSessionState.Paused => "PAUSED",
        IoTestSessionState.Interrupted => "INTERRUPTED",
        IoTestSessionState.Completed => "COMPLETED",
        IoTestSessionState.Stopped => "STOPPED",
        IoTestSessionState.Faulted => "EVIDENCE FAULT",
        _ => State.ToString().ToUpperInvariant()
    };
    public string ActiveIedText => ActiveIed == null ? "No active IED session" : $"{ActiveIed.IedName} · {ActiveIed.IpAddress}";
    public string ProgressText
    {
        get
        {
            if (ActiveIed == null || _sessionPointIds.Count == 0)
                return "0 / 0 complete";
            var scoped = ActiveIed.TestPoints.Where(point => _sessionPointIds.Contains(point.TestPointId)).ToList();
            var complete = scoped.Count(point => point.IsFatEvidenceComplete);
            var passed = scoped.Count(point => point.CaptureMode == FatCaptureMode.AutomaticTransition && point.Runtime.State == IoTestPointState.Passed);
            var review = scoped.Count(point => point.CaptureMode == FatCaptureMode.AutomaticTransition && point.Runtime.State == IoTestPointState.Review);
            return $"{complete} / {_sessionPointIds.Count} complete · {passed} PASS · {review} review";
        }
    }

    public IoTestSessionActionResult Start(IoTestIedPlan? ied)
        => Start(ied, captureScope: null);

    public IoTestSessionActionResult Start(
        IoTestIedPlan? ied,
        IReadOnlyCollection<IoTestPointPlan>? captureScope)
    {
        ThrowIfDisposed();
        if (ied == null)
            return IoTestSessionActionResult.Failure("Select an imported IED first.");
        if (!CanStart)
            return IoTestSessionActionResult.Failure("Stop the active FAT session before starting another IED.");

        var device = ResolveDevice(ied);
        if (device == null)
            return IoTestSessionActionResult.Failure($"{ied.IedName} is not loaded in the ARSAS engineering workspace.");
        if (!device.IsConnected || !device.IsMonitoring)
            return IoTestSessionActionResult.Failure($"{ied.IedName} must be connected and monitoring before FAT evidence can start.");

        List<IoTestPointPlan> expectedPoints;
        if (captureScope == null)
        {
            expectedPoints = ied.TestPoints
                .Where(point => point.WorkspaceSelected && point.IsIncludedInFat && point.TestEnabled && point.ImportReady)
                .ToList();
        }
        else
        {
            var knownPoints = new HashSet<IoTestPointPlan>(ied.TestPoints);
            var invalidScope = captureScope
                .Where(point => !knownPoints.Contains(point) || !point.WorkspaceSelected || !point.IsIncludedInFat || !point.TestEnabled || !point.ImportReady)
                .Distinct()
                .ToList();
            if (invalidScope.Count > 0)
            {
                return IoTestSessionActionResult.Failure(
                    "The requested FAT capture scope contains a row that is not part of this IED/shared workspace, was removed by the operator, has TEST disabled, or is not import-ready.");
            }

            expectedPoints = captureScope.Distinct().ToList();
        }

        if (expectedPoints.Count == 0)
            return IoTestSessionActionResult.Failure("No import-ready operator-selected signal is available in the requested FAT capture scope.");

        var bindings = BuildSessionBindings(expectedPoints, device);
        if (bindings.Count != expectedPoints.Count)
        {
            var missing = expectedPoints.Count - bindings.Count;
            return IoTestSessionActionResult.Failure(
                $"{missing} of {expectedPoints.Count} requested FAT signal(s) do not have one unique live monitor point. Resolve their binding or change the operator selection before starting FAT.");
        }

        CleanupSessionResources(disposeJournal: true);
        _captureCoordinator.Clear();
        _autoCaptureCoordinator.Clear();
        _pendingPairValue1.Clear();
        SessionId = Guid.NewGuid();
        StartedAtUtc = DateTimeOffset.UtcNow;
        CompletedAtUtc = null;
        _connectionGeneration = 1;
        ActiveIed = ied;
        _activeDevice = device;
        _activeDevice.PropertyChanged += ActiveDevice_PropertyChanged;
        _activePointIndex.Clear();
        _activeLivePoints.Clear();
        _sessionPointIds.Clear();
        ClearPendingSnapshots();

        foreach (var binding in bindings)
        {
            _sessionPointIds.Add(binding.Point.TestPointId);
            AddPointIndex(device.DeviceId, binding.LivePoint.IecReference, binding.Point);
            if (!string.IsNullOrWhiteSpace(binding.Point.LiveSignalReference))
                AddPointIndex(device.DeviceId, binding.Point.LiveSignalReference, binding.Point);
            _activeLivePoints[binding.Point.TestPointId] = binding.LivePoint;
        }

        try
        {
            _journal = _journalFactory(_project, ied, SessionId, StartedAtUtc.Value)
                ?? throw new InvalidOperationException("Evidence journal factory returned no journal.");
            JournalPath = _journal.FilePath;
            EvidenceRecordCount = 0;
            LastJournalHash = string.Empty;
            JournalIntegrityText = "Evidence journal open · hash chain initialized";

            var startupEntries = new List<IoTestJournalEntry>((bindings.Count * 2) + 1)
            {
                SessionEvent("session_started", $"FAT session started with an explicit capture scope of {bindings.Count} operator-selected point(s).")
            };
            var autoDecisions = new List<(IoTestPointPlan Point, FatAutoCaptureDecision Decision)>();
            foreach (var binding in bindings)
            {
                var observation = CreateObservation(binding.Point, binding.LivePoint, _connectionGeneration, DateTimeOffset.UtcNow);
                if (binding.Point.CaptureMode == FatCaptureMode.OperatorSnapshot)
                {
                    var genericObservation = observation with { NormalizedState = null };
                    binding.Point.Runtime.ApplyObservation(genericObservation);
                    startupEntries.Add(PointEvent(
                        "operator_snapshot_baseline",
                        binding.Point,
                        genericObservation,
                        null,
                        "Live value established for automatic/manual Value 1 / Value 2 capture."));
                    var decision = _autoCaptureCoordinator.Observe(binding.Point, genericObservation);
                    autoDecisions.Add((binding.Point, decision));
                    if (decision.Evidence != null)
                        startupEntries.Add(FatValueEvent(binding.Point, decision.Evidence));
                    continue;
                }

                var evaluation = _captureCoordinator.Start(binding.Point, observation);
                startupEntries.Add(PointEvent("baseline", binding.Point, observation, evaluation.Evidence, evaluation.Reason));
                var automaticDecision = _autoCaptureCoordinator.Observe(binding.Point, observation);
                autoDecisions.Add((binding.Point, automaticDecision));
                if (automaticDecision.Evidence != null)
                    startupEntries.Add(FatValueEvent(binding.Point, automaticDecision.Evidence));
            }
            AppendBatchRequired(startupEntries);
            foreach (var (point, decision) in autoDecisions)
                PromoteAutoCaptureDecision(point, decision);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            CleanupSessionResources(disposeJournal: true);
            ActiveIed = null;
            State = IoTestSessionState.Faulted;
            StatusText = $"Evidence journal could not be started: {ex.Message}";
            return IoTestSessionActionResult.Failure(StatusText);
        }

        State = IoTestSessionState.Running;
        StatusText = $"FAT capture running for {ied.IedName}. Value 1 / Value 2 auto-capture is armed; manual Capture and multi-row Recapture remain operator overrides until Stop.";
        RaiseProgress();
        UpdateRunningCompletionStatus();
        return IoTestSessionActionResult.Success(StatusText);
    }

    public IoTestSessionActionResult Pause(string reason = "Paused by operator")
    {
        ThrowIfDisposed();
        if (!CanPause)
            return IoTestSessionActionResult.Failure("No running FAT session is available to pause.");

        State = IoTestSessionState.Paused;
        ClearPendingSnapshots();
        StatusText = reason;
        if (!AppendOrFault(SessionEvent("session_paused", reason)))
            return IoTestSessionActionResult.Failure(StatusText);
        return IoTestSessionActionResult.Success(reason);
    }

    public IoTestSessionActionResult Resume()
    {
        ThrowIfDisposed();
        if (!CanResume || ActiveIed == null || _activeDevice == null)
            return IoTestSessionActionResult.Failure("No paused or interrupted FAT session is available to resume.");
        if (!_activeDevice.IsConnected || !_activeDevice.IsMonitoring)
            return IoTestSessionActionResult.Failure($"{ActiveIed.IedName} is not connected and monitoring yet.");

        var sessionPoints = ActiveIed.TestPoints
            .Where(point => _sessionPointIds.Contains(point.TestPointId))
            .ToList();
        var refreshedBindings = BuildSessionBindings(sessionPoints, _activeDevice);
        if (refreshedBindings.Count != _sessionPointIds.Count)
            return IoTestSessionActionResult.Failure($"{ActiveIed.IedName} does not yet have all {_sessionPointIds.Count} session points live after reconnect.");

        _activePointIndex.Clear();
        _activeLivePoints.Clear();
        foreach (var binding in refreshedBindings)
        {
            AddPointIndex(_activeDevice.DeviceId, binding.LivePoint.IecReference, binding.Point);
            if (!string.IsNullOrWhiteSpace(binding.Point.LiveSignalReference))
                AddPointIndex(_activeDevice.DeviceId, binding.Point.LiveSignalReference, binding.Point);
            _activeLivePoints[binding.Point.TestPointId] = binding.LivePoint;
        }

        _connectionGeneration++;
        _pendingPairValue1.Clear();
        _autoCaptureCoordinator.Clear();
        ClearPendingSnapshots();
        if (!AppendOrFault(SessionEvent("session_resumed", "Session resumed after rebinding current live points; current values are treated as a new baseline image. Any staged pair Recapture was cancelled.")))
            return IoTestSessionActionResult.Failure(StatusText);

        foreach (var binding in refreshedBindings)
        {
            var observation = CreateObservation(binding.Point, binding.LivePoint, _connectionGeneration, DateTimeOffset.UtcNow);
            if (binding.Point.CaptureMode == FatCaptureMode.OperatorSnapshot)
            {
                var genericObservation = observation with { NormalizedState = null };
                binding.Point.Runtime.ApplyObservation(genericObservation);
                if (!AppendOrFault(PointEvent(
                        "operator_snapshot_resume_baseline",
                        binding.Point,
                        genericObservation,
                        null,
                        "Live value refreshed after reconnect; existing Value 1 / Value 2 evidence is preserved.")))
                    return IoTestSessionActionResult.Failure(StatusText);
                if (!ApplyAutoCapture(binding.Point, genericObservation))
                    return IoTestSessionActionResult.Failure(StatusText);
                continue;
            }

            var evaluation = _captureCoordinator.Observe(binding.Point, observation);
            if (!AppendOrFault(PointEvent("resume_baseline", binding.Point, observation, evaluation.Evidence, evaluation.Reason)))
                return IoTestSessionActionResult.Failure(StatusText);
            if (!ApplyAutoCapture(binding.Point, observation))
                return IoTestSessionActionResult.Failure(StatusText);
        }

        State = IoTestSessionState.Running;
        StatusText = $"FAT capture resumed for {ActiveIed.IedName}; current evidence is preserved and auto-capture continues only for incomplete slots.";
        RaiseProgress();
        UpdateRunningCompletionStatus();
        return IoTestSessionActionResult.Success(StatusText);
    }

    public IoTestSessionActionResult Stop(string reason = "Stopped by operator")
    {
        ThrowIfDisposed();
        if (!CanStop)
            return IoTestSessionActionResult.Failure("No active FAT session is available to stop.");

        if (!AppendOrFault(SessionEvent("session_stopped", reason)))
            return IoTestSessionActionResult.Failure(StatusText);
        CompletedAtUtc = DateTimeOffset.UtcNow;
        State = IoTestSessionState.Stopped;
        StatusText = reason;
        CleanupSessionResources(disposeJournal: true, keepActiveIed: true);
        if (!VerifySealedJournal())
            return IoTestSessionActionResult.Failure(StatusText);
        RaiseProgress();
        return IoTestSessionActionResult.Success(reason);
    }

    public IoTestSessionActionResult ResetForCleanRetest()
    {
        ThrowIfDisposed();
        if (IsSessionActive)
            return IoTestSessionActionResult.Failure("Stop the active FAT session before creating a clean retest session.");

        CleanupSessionResources(disposeJournal: true);
        SessionId = Guid.Empty;
        StartedAtUtc = null;
        CompletedAtUtc = null;
        JournalPath = string.Empty;
        EvidenceRecordCount = 0;
        LastJournalHash = string.Empty;
        JournalIntegrityText = "No evidence journal open";
        State = IoTestSessionState.Idle;
        StatusText = "Clean FAT retest ready. No evidence has been captured in the new session yet.";
        RaiseProgress();
        return IoTestSessionActionResult.Success(StatusText);
    }

    public IoTestSessionActionResult CaptureOperatorSnapshot(IoTestPointPlan? point, FatValueSlot slot)
    {
        ThrowIfDisposed();
        if (State != IoTestSessionState.Running || ActiveIed == null)
            return IoTestSessionActionResult.Failure("Start the IED FAT session before capturing Value 1 or Value 2.");
        if (point == null || !ActiveIed.TestPoints.Contains(point))
            return IoTestSessionActionResult.Failure("The selected FAT row is not part of the active IED session.");
        if (!_sessionPointIds.Contains(point.TestPointId))
            return IoTestSessionActionResult.Failure("The selected FAT row is not part of the active capture scope.");
        if (!point.WorkspaceSelected || !point.IsIncludedInFat || !point.TestEnabled || !point.ImportReady)
            return IoTestSessionActionResult.Failure("The FAT row must remain in the shared workspace, included, TEST-enabled, and import-ready to capture evidence.");
        if (point.CaptureMode != FatCaptureMode.OperatorSnapshot)
            return IoTestSessionActionResult.Failure("Use right-click Recapture for automatic-transition rows.");
        if (!_activeLivePoints.TryGetValue(point.TestPointId, out var livePoint))
            return IoTestSessionActionResult.Failure("The FAT row does not have one active live point to snapshot.");

        try
        {
            var observation = CreateLiveValueObservation(livePoint);
            var evidence = FatOperatorSnapshotCaptureService.CreateEvidence(slot, observation);

            // Audit history is authoritative and append-only. Only after the journal write
            // succeeds may the replaceable current Value 1 / Value 2 pointer be promoted.
            if (!AppendOrFault(FatValueEvent(point, evidence)))
                return IoTestSessionActionResult.Failure(StatusText);

            point.Runtime.SetFatValueEvidence(evidence);
            point.Runtime.AutoCaptureStage = point.IsFatEvidenceComplete
                ? FatAutoCaptureStage.Complete
                : point.HasValue1Evidence ? FatAutoCaptureStage.WaitingChange : FatAutoCaptureStage.WaitingValue1;
            _autoCaptureCoordinator.Clear(point);
            point.Runtime.StatusReason = point.IsFatEvidenceComplete
                ? "Value 1 and Value 2 captured. Current evidence is complete; either value may be recaptured while the session remains running."
                : $"{(slot == FatValueSlot.Value1 ? "Value 1" : "Value 2")} captured; capture the remaining value to complete current evidence.";
            RaiseProgress();
            UpdateRunningCompletionStatus();
            return IoTestSessionActionResult.Success(
                $"{point.SignalName}: {(slot == FatValueSlot.Value1 ? "Value 1" : "Value 2")} captured as '{evidence.RawValue}'.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return IoTestSessionActionResult.Failure(ex.Message);
        }
    }

    public IoTestSessionActionResult RecaptureValues(
        IReadOnlyCollection<IoTestPointPlan> points,
        FatValueSlot slot)
    {
        ThrowIfDisposed();
        if (!TryPrepareRecapture(points, out var bindings, out var failure))
            return IoTestSessionActionResult.Failure(failure);

        if (slot == FatValueSlot.Value1 && bindings.Any(binding => _pendingPairValue1.ContainsKey(binding.Point.TestPointId)))
        {
            return IoTestSessionActionResult.Failure(
                "A Value 1 & Value 2 pair Recapture is already staged for one or more selected rows. Complete it with Recapture Value 2 or cancel the staged pair first.");
        }

        var captures = new List<(IoTestPointPlan Point, FatValueEvidence Evidence, FatValueEvidence? StagedValue1)>();
        try
        {
            foreach (var binding in bindings)
            {
                var evidence = FatOperatorSnapshotCaptureService.CreateEvidence(
                    slot,
                    CreateLiveValueObservation(binding.LivePoint),
                    FatEvidenceCaptureKind.OperatorRecapture);
                _pendingPairValue1.TryGetValue(binding.Point.TestPointId, out var stagedValue1);
                if (slot == FatValueSlot.Value2 && stagedValue1 != null && stagedValue1.ConnectionGeneration != _connectionGeneration)
                {
                    return IoTestSessionActionResult.Failure(
                        "A staged Value 1 belongs to an earlier IED connection generation. Cancel it and start Value 1 & Value 2 Recapture again.");
                }
                captures.Add((binding.Point, evidence, slot == FatValueSlot.Value2 ? stagedValue1 : null));
            }

            AppendBatchRequired(captures.Select(item => FatValueEvent(
                item.Point,
                item.Evidence,
                item.StagedValue1 != null ? "fat_value_recapture_pair_commit" : "fat_value_recapture")).ToList());
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return IoTestSessionActionResult.Failure(ex.Message);
        }

        foreach (var capture in captures)
        {
            if (capture.StagedValue1 != null)
            {
                capture.Point.Runtime.SetFatValueEvidence(capture.StagedValue1);
                _pendingPairValue1.Remove(capture.Point.TestPointId);
            }
            capture.Point.Runtime.SetFatValueEvidence(capture.Evidence);
            capture.Point.Runtime.AutoCaptureStage = capture.Point.IsFatEvidenceComplete
                ? FatAutoCaptureStage.Complete
                : capture.Point.HasValue1Evidence ? FatAutoCaptureStage.WaitingChange : FatAutoCaptureStage.WaitingValue1;
            capture.Point.Runtime.StatusReason = capture.StagedValue1 != null
                ? "Operator pair Recapture completed; staged Value 1 and current Value 2 are now the authoritative pair."
                : $"Operator recaptured {slot}; the other current slot was preserved.";
            FatCurrentEvidenceAssessmentService.Apply(capture.Point);
            _autoCaptureCoordinator.Clear(capture.Point);
        }

        StatusText = captures.Any(item => item.StagedValue1 != null)
            ? $"{captures.Count} signal(s) · Value 1 & Value 2 recaptured · {ActiveIed!.IedName}"
            : $"{captures.Count} signal(s) · {slot} recaptured · {ActiveIed!.IedName}";
        RaiseProgress();
        UpdateRunningCompletionStatus();
        return IoTestSessionActionResult.Success(StatusText);
    }

    public IoTestSessionActionResult BeginPairRecapture(IReadOnlyCollection<IoTestPointPlan> points)
    {
        ThrowIfDisposed();
        if (!TryPrepareRecapture(points, out var bindings, out var failure))
            return IoTestSessionActionResult.Failure(failure);
        if (bindings.Any(binding => _pendingPairValue1.ContainsKey(binding.Point.TestPointId)))
            return IoTestSessionActionResult.Failure("A Value 1 & Value 2 pair Recapture is already staged for one or more selected rows.");

        var staged = new List<(IoTestPointPlan Point, FatValueEvidence Evidence)>();
        try
        {
            foreach (var binding in bindings)
            {
                staged.Add((
                    binding.Point,
                    FatOperatorSnapshotCaptureService.CreateEvidence(
                        FatValueSlot.Value1,
                        CreateLiveValueObservation(binding.LivePoint),
                        FatEvidenceCaptureKind.OperatorRecapture)));
            }

            AppendBatchRequired(staged.Select(item => FatValueEvent(
                item.Point,
                item.Evidence,
                "fat_value_recapture_pair_staged",
                "Operator staged a new Value 1 for transactional Value 1 & Value 2 Recapture; current pointers remain unchanged until Value 2 is captured.")).ToList());
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return IoTestSessionActionResult.Failure(ex.Message);
        }

        foreach (var item in staged)
        {
            _pendingPairValue1[item.Point.TestPointId] = item.Evidence;
            item.Point.Runtime.StatusReason = "New Value 1 staged for pair Recapture; change the test condition, then Recapture Value 2. Existing current pair remains authoritative.";
        }

        StatusText = $"{staged.Count} signal(s) · Value 1 staged · change condition, then Recapture Value 2 · {ActiveIed!.IedName}";
        RaiseProgress();
        return IoTestSessionActionResult.Success(StatusText);
    }

    public IoTestSessionActionResult CancelPairRecapture(IReadOnlyCollection<IoTestPointPlan> points)
    {
        ThrowIfDisposed();
        var requested = points?.Where(point => point != null).Distinct().ToList() ?? new List<IoTestPointPlan>();
        var staged = requested.Where(point => _pendingPairValue1.ContainsKey(point.TestPointId)).ToList();
        if (staged.Count == 0)
            return IoTestSessionActionResult.Failure("No staged Value 1 & Value 2 Recapture exists for the selected rows.");
        if (State != IoTestSessionState.Running || ActiveIed == null)
            return IoTestSessionActionResult.Failure("The owning FAT session is not running.");

        if (!AppendOrFault(SessionEvent(
                "fat_value_recapture_pair_cancelled",
                $"Operator cancelled staged pair Recapture for {staged.Count} row(s): {string.Join(", ", staged.Select(point => point.TestPointId))}.")))
            return IoTestSessionActionResult.Failure(StatusText);

        foreach (var point in staged)
        {
            _pendingPairValue1.Remove(point.TestPointId);
            point.Runtime.StatusReason = "Staged pair Recapture cancelled; previous current evidence remains unchanged.";
        }
        StatusText = $"{staged.Count} signal(s) · staged pair Recapture cancelled · {ActiveIed.IedName}";
        return IoTestSessionActionResult.Success(StatusText);
    }

    public void Enqueue(Iec61850EventEntry entry)
    {
        if (entry == null || _disposed || State != IoTestSessionState.Running)
            return;

        var activeDevice = _activeDevice;
        if (activeDevice == null ||
            !entry.DeviceId.Equals(activeDevice.DeviceId, StringComparison.OrdinalIgnoreCase))
            return;

        var queued = new QueuedSnapshot(
            entry.DeviceId,
            entry.IecReference,
            entry.OldValue,
            entry.NewValue,
            entry.Quality,
            entry.DeviceTimestamp,
            entry.SourceMode,
            entry.Reason,
            entry.Sequence,
            DateTimeOffset.UtcNow);
        var pendingKey = PointKey(entry.DeviceId, entry.IecReference);
        var preservesTransitionEvidence =
            _activePointIndex.TryGetValue(pendingKey, out var mappedPoints) &&
            mappedPoints.Any(point => point.CaptureMode == FatCaptureMode.AutomaticTransition);
        if (preservesTransitionEvidence &&
            !Iec61850MonitorPoint.AreSemanticallyEquivalent(entry.OldValue, entry.NewValue))
        {
            _pendingSnapshots.TryRemove(pendingKey, out _);
            _pendingEdgeSnapshots.Enqueue(queued);
        }
        else
        {
            _pendingSnapshots.AddOrUpdate(
                pendingKey,
                queued,
                (_, current) => queued.Sequence >= current.Sequence ? queued : current);
        }
        ScheduleDrain();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        if (CanStop)
            Stop("Workspace closed; FAT session stopped.");
        _disposed = true;
        CleanupSessionResources(disposeJournal: true);
        ClearPendingSnapshots();
    }

    private void ScheduleDrain()
    {
        if (Interlocked.Exchange(ref _drainScheduled, 1) != 0)
            return;
        try
        {
            _dispatch(DrainPendingSnapshots);
        }
        catch
        {
            Interlocked.Exchange(ref _drainScheduled, 0);
            throw;
        }
    }

    private void DrainPendingSnapshots()
    {
        var stopwatch = Stopwatch.StartNew();
        var processed = 0;
        try
        {
            while (processed < MaxSnapshotsPerDrain &&
                   stopwatch.ElapsedMilliseconds < DrainBudgetMilliseconds)
            {
                QueuedSnapshot? snapshot;
                if (!_pendingEdgeSnapshots.TryDequeue(out snapshot))
                {
                    var pendingKey = _pendingSnapshots.Keys.FirstOrDefault();
                    if (pendingKey == null || !_pendingSnapshots.TryRemove(pendingKey, out snapshot))
                        break;
                }
                ProcessSnapshot(snapshot);
                processed++;
                if (State != IoTestSessionState.Running)
                    break;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _drainScheduled, 0);
            if ((!_pendingEdgeSnapshots.IsEmpty || !_pendingSnapshots.IsEmpty) && State == IoTestSessionState.Running)
                ScheduleDrain();
        }
    }

    private void ClearPendingSnapshots()
    {
        _pendingSnapshots.Clear();
        while (_pendingEdgeSnapshots.TryDequeue(out _)) { }
    }

    private void ProcessSnapshot(QueuedSnapshot snapshot)
    {
        if (State != IoTestSessionState.Running)
            return;

        var key = PointKey(snapshot.DeviceId, snapshot.Reference);
        if (!_activePointIndex.TryGetValue(key, out var points))
            return;

        var progressChanged = false;
        foreach (var point in points.Distinct())
        {
            var normalized = point.CaptureMode == FatCaptureMode.AutomaticTransition
                ? IoTestValueNormalizer.Normalize(point, snapshot.Value)
                : null;
            var observation = new IoTestObservation(
                normalized,
                snapshot.Value,
                snapshot.CapturedAtUtc,
                IoTestValueNormalizer.ParseIedTimestamp(snapshot.DeviceTimestamp),
                snapshot.Quality,
                snapshot.SourceMode,
                snapshot.Sequence,
                _connectionGeneration);

            if (point.CaptureMode == FatCaptureMode.OperatorSnapshot)
            {
                point.Runtime.ApplyObservation(observation);
            }
            else
            {
                var evaluation = _captureCoordinator.Observe(point, observation);
                progressChanged |= evaluation.StateChanged || evaluation.Evidence != null;

                if (evaluation.Evidence != null)
                {
                    var eventType = evaluation.Evidence.Verdict == IoEvidenceVerdict.Rejected
                        ? "transition_rejected"
                        : evaluation.Evidence.Transition == IoEvidenceTransition.On
                            ? "on_evidence"
                            : "off_evidence";
                    if (!AppendOrFault(PointEvent(eventType, point, observation, evaluation.Evidence, evaluation.Reason, snapshot.Reason, snapshot.PreviousValue)))
                        return;
                }
                else if (evaluation.StateChanged)
                {
                    if (!AppendOrFault(PointEvent(
                            "baseline_state",
                            point,
                            observation,
                            null,
                            evaluation.Reason,
                            snapshot.Reason,
                            snapshot.PreviousValue)))
                        return;
                }
                else if (normalized == null)
                {
                    if (!AppendOrFault(PointEvent(
                            "unresolved_edge",
                            point,
                            observation,
                            null,
                            "A live edge was observed but its digital state could not be normalized.",
                            snapshot.Reason,
                            snapshot.PreviousValue)))
                        return;
                }
            }

            var previousStage = point.Runtime.AutoCaptureStage;
            var previousComplete = point.IsFatEvidenceComplete;
            if (!ApplyAutoCapture(point, observation))
                return;
            progressChanged |= previousStage != point.Runtime.AutoCaptureStage || previousComplete != point.IsFatEvidenceComplete;
        }

        if (progressChanged)
        {
            RaiseProgress();
            UpdateRunningCompletionStatus();
        }
    }

    private bool ApplyAutoCapture(IoTestPointPlan point, IoTestObservation observation)
    {
        var decision = _autoCaptureCoordinator.Observe(point, observation);
        if (decision.Evidence != null && !AppendOrFault(FatValueEvent(point, decision.Evidence)))
            return false;
        PromoteAutoCaptureDecision(point, decision);
        return true;
    }

    private static void PromoteAutoCaptureDecision(IoTestPointPlan point, FatAutoCaptureDecision decision)
    {
        point.Runtime.AutoCaptureStage = decision.Stage;
        if (decision.Evidence != null)
            point.Runtime.SetFatValueEvidence(decision.Evidence);
        if (point.CaptureMode == FatCaptureMode.OperatorSnapshot || decision.Evidence != null)
            point.Runtime.StatusReason = decision.Message;

        FatCurrentEvidenceAssessmentService.Apply(point);
    }

    private void UpdateRunningCompletionStatus()
    {
        if (State != IoTestSessionState.Running || ActiveIed == null || _sessionPointIds.Count == 0)
            return;

        var scoped = ActiveIed.TestPoints
            .Where(point => _sessionPointIds.Contains(point.TestPointId))
            .ToList();
        var complete = scoped.Count(point => point.IsFatEvidenceComplete);
        if (complete != scoped.Count)
            return;

        var automatic = scoped.Where(point => point.CaptureMode == FatCaptureMode.AutomaticTransition).ToList();
        var passed = automatic.Count(point => point.Runtime.State == IoTestPointState.Passed);
        var review = automatic.Count(point => point.Runtime.State == IoTestPointState.Review);
        var failed = automatic.Count(point => point.Runtime.State == IoTestPointState.Failed);
        var snapshots = scoped.Count - automatic.Count;
        StatusText =
            $"{ActiveIed.IedName} current evidence complete: {passed} PASS, {review} review, {failed} fail, {snapshots} snapshot row(s) complete. " +
            "Auto-capture is locked at the current pair; explicit Recapture is required to replace Value 1 or Value 2 until Stop.";
    }

    private void ActiveDevice_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(Iec61850MonitorDevice.IsConnected) or nameof(Iec61850MonitorDevice.IsMonitoring) or nameof(Iec61850MonitorDevice.Status)) ||
            sender is not Iec61850MonitorDevice device ||
            (device.IsConnected && device.IsMonitoring))
            return;

        var capturedStatus = device.Status;
        _dispatch(() => InterruptForConnectionLoss(capturedStatus));
    }

    private void InterruptForConnectionLoss(string capturedStatus)
    {
        if (State != IoTestSessionState.Running || ActiveIed == null)
            return;

        var stagedCount = _pendingPairValue1.Count;
        _pendingPairValue1.Clear();
        _autoCaptureCoordinator.Clear();
        State = IoTestSessionState.Interrupted;
        ClearPendingSnapshots();
        StatusText = $"{ActiveIed.IedName} monitoring was interrupted ({capturedStatus}). Reconnect the IED, then Resume to establish a new baseline." +
                     (stagedCount == 0 ? string.Empty : $" {stagedCount} staged pair Recapture item(s) were cancelled; current pointers were unchanged.");
        AppendOrFault(SessionEvent("session_interrupted", StatusText));
    }

    private bool TryPrepareRecapture(
        IReadOnlyCollection<IoTestPointPlan>? points,
        out List<SessionBinding> bindings,
        out string failure)
    {
        bindings = new List<SessionBinding>();
        failure = string.Empty;
        if (State != IoTestSessionState.Running || ActiveIed == null)
        {
            failure = "Start this IED FAT session before using Recapture.";
            return false;
        }

        var requested = points?.Where(point => point != null).Distinct().ToList() ?? new List<IoTestPointPlan>();
        if (requested.Count == 0)
        {
            failure = "Select one or more FAT rows before using Recapture.";
            return false;
        }

        foreach (var point in requested)
        {
            if (!ActiveIed.TestPoints.Contains(point) || !_sessionPointIds.Contains(point.TestPointId))
            {
                failure = $"{point.SignalName} is not part of the active IED capture scope. No evidence changed.";
                return false;
            }
            if (!point.WorkspaceSelected || !point.IsIncludedInFat || !point.TestEnabled || !point.ImportReady)
            {
                failure = $"{point.SignalName} is not eligible for Recapture because it left the shared/included/TEST/import-ready FAT scope. No evidence changed.";
                return false;
            }
            if (!_activeLivePoints.TryGetValue(point.TestPointId, out var livePoint))
            {
                failure = $"{point.SignalName} does not have one active live point. No evidence changed.";
                return false;
            }
            bindings.Add(new SessionBinding(point, livePoint));
        }
        return true;
    }

    private static List<SessionBinding> BuildSessionBindings(
        IReadOnlyCollection<IoTestPointPlan> captureScope,
        Iec61850MonitorDevice device)
    {
        var result = new List<SessionBinding>();
        foreach (var point in captureScope.Where(point => point.WorkspaceSelected && point.IsIncludedInFat && point.IsLiveBound))
        {
            var expected = IoTestLiveBindingService.NormalizeReference(
                string.IsNullOrWhiteSpace(point.LiveSignalReference) ? point.ObjectReference : point.LiveSignalReference);
            var liveCandidates = device.Points
                .Where(live => IoTestLiveBindingService.NormalizeReference(live.IecReference)
                    .Equals(expected, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (liveCandidates.Count == 1)
                result.Add(new SessionBinding(point, liveCandidates[0]));
        }
        return result;
    }

    private Iec61850MonitorDevice? ResolveDevice(IoTestIedPlan ied)
    {
        if (!string.IsNullOrWhiteSpace(ied.LiveDeviceId))
        {
            var byId = _deviceResolver(ied.LiveDeviceId);
            if (byId != null)
                return byId;
        }
        return _deviceResolver(ied.IedName) ?? _deviceResolver(ied.IpAddress);
    }

    private static IoTestObservation CreateObservation(
        IoTestPointPlan point,
        Iec61850MonitorPoint livePoint,
        long connectionGeneration,
        DateTimeOffset capturedAtUtc)
        => new(
            point.CaptureMode == FatCaptureMode.AutomaticTransition
                ? IoTestValueNormalizer.Normalize(point, livePoint.Value)
                : null,
            livePoint.Value,
            capturedAtUtc,
            IoTestValueNormalizer.ParseIedTimestamp(livePoint.DeviceTimestamp),
            livePoint.Quality,
            livePoint.SourceMode,
            livePoint.Sequence,
            connectionGeneration);

    private FatLiveValueObservation CreateLiveValueObservation(Iec61850MonitorPoint livePoint)
        => new(
            livePoint.Value,
            DateTimeOffset.UtcNow,
            IoTestValueNormalizer.ParseIedTimestamp(livePoint.DeviceTimestamp),
            livePoint.Quality,
            livePoint.SourceMode,
            livePoint.Sequence,
            _connectionGeneration);

    private void AddPointIndex(string deviceId, string reference, IoTestPointPlan point)
    {
        var key = PointKey(deviceId, reference);
        if (!_activePointIndex.TryGetValue(key, out var points))
        {
            points = new List<IoTestPointPlan>();
            _activePointIndex[key] = points;
        }
        if (!points.Contains(point))
            points.Add(point);
    }

    private static string PointKey(string deviceId, string reference)
        => $"{deviceId}|{IoTestLiveBindingService.NormalizeReference(reference)}";

    private IoTestJournalEntry SessionEvent(string eventType, string reason) => new()
    {
        EventType = eventType,
        RecordedAtUtc = DateTimeOffset.UtcNow,
        ProjectId = _project.ProjectId,
        SessionId = SessionId,
        IedName = ActiveIed?.IedName ?? string.Empty,
        IpAddress = ActiveIed?.IpAddress ?? string.Empty,
        SourceWorkbookName = _project.SourceWorkbookName,
        SourceWorkbookSha256 = _project.SourceWorkbookSha256,
        ApplicationVersion = CurrentApplicationVersion,
        Operator = Environment.UserName,
        Workstation = Environment.MachineName,
        ConnectionGeneration = _connectionGeneration,
        Reason = reason
    };

    private IoTestJournalEntry PointEvent(
        string eventType,
        IoTestPointPlan point,
        IoTestObservation observation,
        IoTestTransitionEvidence? evidence,
        string reason,
        string reportReason = "",
        string previousRawValue = "") => new()
    {
        EventType = eventType,
        RecordedAtUtc = evidence?.CapturedAt ?? observation.CapturedAt,
        ProjectId = _project.ProjectId,
        SessionId = SessionId,
        IedName = point.IedName,
        IpAddress = point.IpAddress,
        SourceWorkbookName = _project.SourceWorkbookName,
        SourceWorkbookSha256 = _project.SourceWorkbookSha256,
        ApplicationVersion = CurrentApplicationVersion,
        Operator = Environment.UserName,
        Workstation = Environment.MachineName,
        TestPointId = point.TestPointId,
        SignalName = point.SignalName,
        ObjectReference = string.IsNullOrWhiteSpace(point.LiveSignalReference) ? point.ObjectReference : point.LiveSignalReference,
        Attempt = point.Runtime.Attempt,
        Transition = evidence?.Transition.ToString() ?? IoEvidenceTransition.Baseline.ToString(),
        PreviousValue = string.IsNullOrWhiteSpace(previousRawValue)
            ? evidence?.PreviousState?.ToString() ?? point.Runtime.LastObservedState?.ToString() ?? string.Empty
            : previousRawValue,
        ObservedValue = evidence?.RawValue ?? observation.RawValue,
        NormalizedState = observation.NormalizedState,
        IedTimestamp = evidence?.IedTimestamp ?? observation.IedTimestamp,
        Quality = evidence?.Quality ?? observation.Quality,
        AcquisitionSource = evidence?.AcquisitionSource ?? observation.AcquisitionSource,
        ReportReason = reportReason,
        PointSequence = evidence?.Sequence ?? observation.Sequence,
        ConnectionGeneration = evidence?.ConnectionGeneration ?? observation.ConnectionGeneration,
        Verdict = evidence?.Verdict.ToString() ?? "Baseline",
        Reason = evidence?.VerdictReason ?? reason
    };

    private IoTestJournalEntry FatValueEvent(
        IoTestPointPlan point,
        FatValueEvidence evidence,
        string? eventType = null,
        string? reason = null)
    {
        var automatic = evidence.CaptureKind == FatEvidenceCaptureKind.AutomaticValue;
        var recapture = evidence.CaptureKind == FatEvidenceCaptureKind.OperatorRecapture;
        return new IoTestJournalEntry
        {
            EventType = eventType ?? (automatic ? "fat_value_auto" : recapture ? "fat_value_recapture" : "fat_value_snapshot"),
            RecordedAtUtc = evidence.CapturedAt,
            ProjectId = _project.ProjectId,
            SessionId = SessionId,
            IedName = point.IedName,
            IpAddress = point.IpAddress,
            SourceWorkbookName = _project.SourceWorkbookName,
            SourceWorkbookSha256 = _project.SourceWorkbookSha256,
            ApplicationVersion = CurrentApplicationVersion,
            Operator = Environment.UserName,
            Workstation = Environment.MachineName,
            TestPointId = point.TestPointId,
            SignalName = point.SignalName,
            ObjectReference = string.IsNullOrWhiteSpace(point.LiveSignalReference) ? point.ObjectReference : point.LiveSignalReference,
            Attempt = point.Runtime.Attempt,
            Transition = evidence.Slot.ToString(),
            ObservedValue = evidence.RawValue,
            IedTimestamp = evidence.IedTimestamp,
            Quality = evidence.Quality,
            AcquisitionSource = evidence.AcquisitionSource,
            PointSequence = evidence.Sequence,
            ConnectionGeneration = evidence.ConnectionGeneration,
            Verdict = "Accepted",
            Reason = reason ?? (automatic
                ? $"Auto Capture accepted {evidence.Slot} from the stable live IEC 61850 value."
                : recapture
                    ? $"Operator recaptured {evidence.Slot} from the current live IEC 61850 value."
                    : $"Operator captured {evidence.Slot} from the current live IEC 61850 value."),
            EvidenceKind = automatic ? "fat-value-auto" : recapture ? "fat-value-recapture" : "fat-value-snapshot"
        };
    }

    private void AppendRequired(IoTestJournalEntry entry)
    {
        if (!AppendOrFault(entry))
            throw new InvalidOperationException(StatusText);
    }

    private void AppendBatchRequired(IReadOnlyCollection<IoTestJournalEntry> entries)
    {
        try
        {
            var journal = _journal ?? throw new InvalidOperationException("No evidence journal is open for the active FAT session.");
            var envelopes = journal.AppendBatch(entries);
            if (envelopes.Count == 0)
                throw new InvalidOperationException("Evidence journal batch produced no records.");
            var envelope = envelopes[^1];
            EvidenceRecordCount = envelope.JournalSequence;
            LastJournalHash = envelope.Hash;
            JournalIntegrityText = $"Evidence journal open · {EvidenceRecordCount} record(s) · SHA-256 {ShortHash(LastJournalHash)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException or InvalidOperationException)
        {
            State = IoTestSessionState.Faulted;
            StatusText = $"Evidence journal write failed; FAT capture stopped: {ex.Message}";
            CleanupSessionResources(disposeJournal: true, keepActiveIed: true);
            throw new InvalidOperationException(StatusText, ex);
        }
    }

    private bool AppendOrFault(IoTestJournalEntry entry)
    {
        try
        {
            var journal = _journal ?? throw new InvalidOperationException("No evidence journal is open for the active FAT session.");
            var envelope = journal.Append(entry);
            EvidenceRecordCount = envelope.JournalSequence;
            LastJournalHash = envelope.Hash;
            JournalIntegrityText = $"Evidence journal open · {EvidenceRecordCount} record(s) · SHA-256 {ShortHash(LastJournalHash)}";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException or InvalidOperationException)
        {
            State = IoTestSessionState.Faulted;
            StatusText = $"Evidence journal write failed; FAT capture stopped: {ex.Message}";
            CleanupSessionResources(disposeJournal: true, keepActiveIed: true);
            return false;
        }
    }

    private bool VerifySealedJournal()
    {
        var verification = IoTestEvidenceJournal.Verify(JournalPath);
        if (!verification.IsValid)
        {
            State = IoTestSessionState.Faulted;
            StatusText = $"Evidence journal integrity verification failed: {verification.Error}";
            JournalIntegrityText = "Evidence journal verification failed";
            return false;
        }

        EvidenceRecordCount = verification.RecordCount;
        LastJournalHash = verification.LastHash;
        JournalIntegrityText = $"Verified · {verification.RecordCount} record(s) · SHA-256 {ShortHash(verification.LastHash)}";
        return true;
    }

    private static string ShortHash(string hash)
        => string.IsNullOrWhiteSpace(hash) ? "-" : hash[..Math.Min(16, hash.Length)] + "…";

    private void CleanupSessionResources(bool disposeJournal, bool keepActiveIed = false)
    {
        if (_activeDevice != null)
            _activeDevice.PropertyChanged -= ActiveDevice_PropertyChanged;
        _activeDevice = null;
        _activePointIndex.Clear();
        _activeLivePoints.Clear();
        _captureCoordinator.Clear();
        _autoCaptureCoordinator.Clear();
        _pendingPairValue1.Clear();
        ClearPendingSnapshots();
        if (disposeJournal)
        {
            _journal?.Dispose();
            _journal = null;
        }
        if (!keepActiveIed)
        {
            ActiveIed = null;
            _sessionPointIds.Clear();
        }
    }

    private void RaiseStateProperties()
    {
        Raise(nameof(IsSessionActive));
        Raise(nameof(CanStart));
        Raise(nameof(CanPause));
        Raise(nameof(CanResume));
        Raise(nameof(CanStop));
        Raise(nameof(CanSelectIed));
        Raise(nameof(CanEditPlan));
        Raise(nameof(StateText));
        Raise(nameof(ActiveIedText));
        Raise(nameof(ProgressText));
    }

    private void RaiseProgress()
    {
        Raise(nameof(ProgressText));
        Raise(nameof(EvidenceRecordCount));
        Raise(nameof(LastJournalHash));
        Raise(nameof(JournalIntegrityText));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record SessionBinding(IoTestPointPlan Point, Iec61850MonitorPoint LivePoint);

    private sealed record QueuedSnapshot(
        string DeviceId,
        string Reference,
        string PreviousValue,
        string Value,
        string Quality,
        string DeviceTimestamp,
        string SourceMode,
        string Reason,
        long Sequence,
        DateTimeOffset CapturedAtUtc);
}
