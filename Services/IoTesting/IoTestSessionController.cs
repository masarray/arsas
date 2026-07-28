using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed class IoTestSessionController : ObservableObject, IDisposable
{
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
    private readonly ConcurrentQueue<QueuedSnapshot> _pendingSnapshots = new();
    private readonly Dictionary<string, List<IoTestPointPlan>> _activePointIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Iec61850MonitorPoint> _activeLivePoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sessionPointIds = new(StringComparer.OrdinalIgnoreCase);

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
            var complete = ActiveIed.TestPoints.Count(point => _sessionPointIds.Contains(point.TestPointId) && point.Runtime.IsComplete);
            var passed = ActiveIed.TestPoints.Count(point => _sessionPointIds.Contains(point.TestPointId) && point.Runtime.State == IoTestPointState.Passed);
            var review = ActiveIed.TestPoints.Count(point => _sessionPointIds.Contains(point.TestPointId) && point.Runtime.State == IoTestPointState.Review);
            return $"{complete} / {_sessionPointIds.Count} complete · {passed} PASS · {review} review";
        }
    }

    public IoTestSessionActionResult Start(IoTestIedPlan? ied)
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

        var bindings = BuildSessionBindings(ied, device);
        if (bindings.Count == 0)
            return IoTestSessionActionResult.Failure("No enabled IO-list signal has a live monitor point for this IED.");

        CleanupSessionResources(disposeJournal: true);
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
        while (_pendingSnapshots.TryDequeue(out _)) { }

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
            AppendRequired(SessionEvent("session_started", "FAT session started with a live baseline for every enabled bound point."));

            foreach (var binding in bindings)
            {
                var observation = CreateObservation(binding.Point, binding.LivePoint, _connectionGeneration, DateTimeOffset.UtcNow);
                _evaluator.StartAttempt(binding.Point, observation);
                AppendRequired(PointEvent("baseline", binding.Point, observation, null, binding.Point.Runtime.StatusReason));
            }
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
        StatusText = $"FAT session running for {ied.IedName}. Waiting for OFF → ON → OFF transitions.";
        RaiseProgress();
        return IoTestSessionActionResult.Success(StatusText);
    }

    public IoTestSessionActionResult Pause(string reason = "Paused by operator")
    {
        ThrowIfDisposed();
        if (!CanPause)
            return IoTestSessionActionResult.Failure("No running FAT session is available to pause.");

        State = IoTestSessionState.Paused;
        while (_pendingSnapshots.TryDequeue(out _)) { }
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

        _connectionGeneration++;
        while (_pendingSnapshots.TryDequeue(out _)) { }
        if (!AppendOrFault(SessionEvent("session_resumed", "Session resumed; current values are treated as a new baseline image.")))
            return IoTestSessionActionResult.Failure(StatusText);

        foreach (var point in ActiveIed.TestPoints.Where(point => _sessionPointIds.Contains(point.TestPointId)))
        {
            if (!_activeLivePoints.TryGetValue(point.TestPointId, out var livePoint))
                continue;
            var observation = CreateObservation(point, livePoint, _connectionGeneration, DateTimeOffset.UtcNow);
            var evaluation = _evaluator.Observe(point, observation);
            if (!AppendOrFault(PointEvent("resume_baseline", point, observation, evaluation.Evidence, evaluation.Reason)))
                return IoTestSessionActionResult.Failure(StatusText);
        }

        State = IoTestSessionState.Running;
        StatusText = $"FAT session resumed for {ActiveIed.IedName}.";
        RaiseProgress();
        CompleteIfAllPointsFinished();
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

    public void Enqueue(Iec61850EventEntry entry)
    {
        if (entry == null || _disposed)
            return;

        var activeDevice = _activeDevice;
        if (activeDevice == null ||
            !entry.DeviceId.Equals(activeDevice.DeviceId, StringComparison.OrdinalIgnoreCase))
            return;

        _pendingSnapshots.Enqueue(new QueuedSnapshot(
            entry.DeviceId,
            entry.IecReference,
            entry.OldValue,
            entry.NewValue,
            entry.Quality,
            entry.DeviceTimestamp,
            entry.SourceMode,
            entry.Reason,
            entry.Sequence,
            DateTimeOffset.UtcNow));
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
        while (_pendingSnapshots.TryDequeue(out _)) { }
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
        try
        {
            while (_pendingSnapshots.TryDequeue(out var snapshot))
                ProcessSnapshot(snapshot);
        }
        finally
        {
            Interlocked.Exchange(ref _drainScheduled, 0);
            if (!_pendingSnapshots.IsEmpty && State == IoTestSessionState.Running)
                ScheduleDrain();
        }
    }

    private void ProcessSnapshot(QueuedSnapshot snapshot)
    {
        if (State != IoTestSessionState.Running)
            return;

        var key = PointKey(snapshot.DeviceId, snapshot.Reference);
        if (!_activePointIndex.TryGetValue(key, out var points))
            return;

        foreach (var point in points.Distinct())
        {
            var normalized = IoTestValueNormalizer.Normalize(point, snapshot.Value);
            var observation = new IoTestObservation(
                normalized,
                snapshot.Value,
                snapshot.CapturedAtUtc,
                IoTestValueNormalizer.ParseIedTimestamp(snapshot.DeviceTimestamp),
                snapshot.Quality,
                snapshot.SourceMode,
                snapshot.Sequence,
                _connectionGeneration);
            var evaluation = _evaluator.Observe(point, observation);

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

        RaiseProgress();
        CompleteIfAllPointsFinished();
    }

    private void CompleteIfAllPointsFinished()
    {
        if (State != IoTestSessionState.Running || ActiveIed == null || _sessionPointIds.Count == 0)
            return;
        var allComplete = ActiveIed.TestPoints
            .Where(point => _sessionPointIds.Contains(point.TestPointId))
            .All(point => point.Runtime.IsComplete);
        if (!allComplete)
            return;

        var passed = ActiveIed.TestPoints.Count(point => _sessionPointIds.Contains(point.TestPointId) && point.Runtime.State == IoTestPointState.Passed);
        var review = ActiveIed.TestPoints.Count(point => _sessionPointIds.Contains(point.TestPointId) && point.Runtime.State == IoTestPointState.Review);
        if (!AppendOrFault(SessionEvent("session_completed", $"All {_sessionPointIds.Count} session points completed: {passed} PASS, {review} review.")))
            return;
        CompletedAtUtc = DateTimeOffset.UtcNow;
        State = IoTestSessionState.Completed;
        StatusText = $"{ActiveIed.IedName} completed: {passed} PASS, {review} review.";
        CleanupSessionResources(disposeJournal: true, keepActiveIed: true);
        VerifySealedJournal();
        RaiseProgress();
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

        State = IoTestSessionState.Interrupted;
        while (_pendingSnapshots.TryDequeue(out _)) { }
        StatusText = $"{ActiveIed.IedName} monitoring was interrupted ({capturedStatus}). Reconnect the IED, then Resume to establish a new baseline.";
        AppendOrFault(SessionEvent("session_interrupted", StatusText));
    }

    private List<SessionBinding> BuildSessionBindings(IoTestIedPlan ied, Iec61850MonitorDevice device)
    {
        var result = new List<SessionBinding>();
        foreach (var point in ied.TestPoints.Where(point => point.TestEnabled && point.ImportReady && point.IsLiveBound))
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
            IoTestValueNormalizer.Normalize(point, livePoint.Value),
            livePoint.Value,
            capturedAtUtc,
            IoTestValueNormalizer.ParseIedTimestamp(livePoint.DeviceTimestamp),
            livePoint.Quality,
            livePoint.SourceMode,
            livePoint.Sequence,
            connectionGeneration);

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

    private void AppendRequired(IoTestJournalEntry entry)
    {
        if (!AppendOrFault(entry))
            throw new InvalidOperationException(StatusText);
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
        while (_pendingSnapshots.TryDequeue(out _)) { }
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
