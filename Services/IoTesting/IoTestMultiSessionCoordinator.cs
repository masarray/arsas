using System.ComponentModel;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// P2/P3 evidence-session authority. Each FAT IED owns one isolated
/// <see cref="IoTestSessionController"/> so several connected IEDs can capture evidence
/// at the same time without sharing a journal, live-point index, connection generation,
/// transition evaluator, auto-capture state, or Recapture transaction.
///
/// The controller supplied by MainWindow remains the primary leaf for backwards
/// compatibility. Additional leaves are created lazily through the configured sibling
/// factory and receive runtime events through <see cref="EnqueueAdditional"/>; the primary
/// leaf continues to receive its existing MainWindow event route. This deliberately avoids
/// double-delivery to the primary journal.
/// </summary>
public sealed class IoTestMultiSessionCoordinator : ObservableObject, IDisposable
{
    private readonly object _sync = new();
    private readonly IoTestProject _project;
    private readonly IoTestSessionController _primaryController;
    private readonly Dictionary<IoTestIedPlan, IoTestSessionController> _controllers = new();
    private Func<IoTestSessionController>? _siblingFactory;
    private IoTestIedPlan? _selectedIed;
    private IoTestIedPlan? _primaryIed;
    private bool _disposed;

    public IoTestMultiSessionCoordinator(
        IoTestProject project,
        IoTestSessionController primaryController)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _primaryController = primaryController ?? throw new ArgumentNullException(nameof(primaryController));
        _primaryController.PropertyChanged += Child_PropertyChanged;
    }

    public IoTestProject Project => _project;
    public IoTestSessionController PrimaryController => _primaryController;
    public IoTestIedPlan? SelectedIed => _selectedIed;

    private IoTestSessionController? SelectedController
    {
        get
        {
            lock (_sync)
                return _selectedIed != null && _controllers.TryGetValue(_selectedIed, out var controller)
                    ? controller
                    : null;
        }
    }

    public int ActiveSessionCount => ControllerSnapshot().Count(controller => controller.IsSessionActive);
    public bool HasActiveSessions => ActiveSessionCount > 0;

    // Session-facing properties are deliberately selected-context projections. This keeps
    // existing FAT UI bindings intuitive: when IED A is running and the operator selects
    // IED B, B still shows Start FAT and remains editable. Workspace-close/export code uses
    // HasActiveSessions/ActiveSessionCount for the global safety boundary.
    public bool IsSelectedSessionActive => SelectedController?.IsSessionActive == true;
    public bool IsSessionActive => IsSelectedSessionActive;
    public bool CanStart => _selectedIed != null && !IsSelectedSessionActive;
    public bool CanPause => SelectedController?.CanPause == true;
    public bool CanResume => SelectedController?.CanResume == true;
    public bool CanStop => SelectedController?.CanStop == true;
    public bool CanSelectIed => true;
    public bool CanEditPlan => !IsSelectedSessionActive;

    public IoTestSessionState State => SelectedController?.State ?? IoTestSessionState.Idle;
    public IoTestIedPlan? ActiveIed => IsSelectedSessionActive ? SelectedController?.ActiveIed : null;
    public Guid SessionId => SelectedController?.SessionId ?? Guid.Empty;
    public DateTimeOffset? StartedAtUtc => SelectedController?.StartedAtUtc;
    public DateTimeOffset? CompletedAtUtc => SelectedController?.CompletedAtUtc;
    public string JournalPath => SelectedController?.JournalPath ?? string.Empty;
    public long EvidenceRecordCount => SelectedController?.EvidenceRecordCount ?? 0;
    public string LastJournalHash => SelectedController?.LastJournalHash ?? string.Empty;
    public string JournalIntegrityText => SelectedController?.JournalIntegrityText ?? "No evidence journal open";
    public string StateText => SelectedController?.StateText ?? "READY";
    public string ActiveIedText => ActiveIed == null
        ? "No active IED session in the selected context"
        : $"{ActiveIed.IedName} · {ActiveIed.IpAddress}";
    public string ProgressText => SelectedController?.ProgressText ?? "0 / 0 complete";
    public string StatusText
    {
        get
        {
            var selected = SelectedController;
            if (selected != null)
                return selected.StatusText;
            if (ActiveSessionCount > 0)
                return $"{ActiveSessionCount} other IED FAT evidence session(s) are running independently.";
            return "Select an imported IED and start its FAT session.";
        }
    }

    /// <summary>
    /// Supplies the MainWindow-owned leaf factory after the FAT window has an Owner.
    /// The factory must create a fresh IoTestSessionController for the same project and
    /// evidence directory; no controller instance may be shared between IEDs.
    /// </summary>
    public void ConfigureSiblingFactory(Func<IoTestSessionController> factory)
    {
        ThrowIfDisposed();
        _siblingFactory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public void SelectContext(IoTestIedPlan? ied)
    {
        ThrowIfDisposed();
        if (ReferenceEquals(_selectedIed, ied))
            return;
        _selectedIed = ied;
        RaiseProjectionProperties();
    }

    public bool IsIedSessionActive(IoTestIedPlan? ied)
    {
        if (ied == null)
            return false;
        lock (_sync)
            return _controllers.TryGetValue(ied, out var controller) && controller.IsSessionActive;
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

        SelectContext(ied);
        IoTestSessionController controller;
        try
        {
            controller = GetOrCreateController(ied);
        }
        catch (InvalidOperationException ex)
        {
            return IoTestSessionActionResult.Failure(ex.Message);
        }

        var result = controller.Start(ied, captureScope);
        RaiseProjectionProperties();
        return result;
    }

    public IoTestSessionActionResult Pause(string reason = "Paused by operator")
        => SelectedAction(
            controller => controller.Pause(reason),
            "No FAT evidence session is active for the selected IED.");

    public IoTestSessionActionResult Resume()
        => SelectedAction(
            controller => controller.Resume(),
            "No paused or interrupted FAT evidence session belongs to the selected IED.");

    public IoTestSessionActionResult Stop(string reason = "Stopped by operator")
        => SelectedAction(
            controller => controller.Stop(reason),
            "No FAT evidence session is active for the selected IED.");

    public IoTestSessionActionResult StopAll(string reason = "Stopped by operator")
    {
        ThrowIfDisposed();
        var failures = new List<string>();
        foreach (var controller in ControllerSnapshot().Where(controller => controller.IsSessionActive))
        {
            var result = controller.Stop(reason);
            if (!result.Succeeded)
                failures.Add(result.Message);
        }
        RaiseProjectionProperties();
        return failures.Count == 0
            ? IoTestSessionActionResult.Success(reason)
            : IoTestSessionActionResult.Failure(string.Join(" ", failures.Distinct(StringComparer.Ordinal)));
    }

    public IoTestSessionActionResult ResetForCleanRetest()
        => SelectedAction(
            controller => controller.ResetForCleanRetest(),
            "Select an IED with an existing FAT evidence context before creating a clean retest.");

    public IoTestSessionActionResult CaptureOperatorSnapshot(IoTestPointPlan? point, FatValueSlot slot)
    {
        ThrowIfDisposed();
        if (point == null)
            return IoTestSessionActionResult.Failure("Select a FAT row before capturing Value 1 or Value 2.");

        var controller = ResolveOwningActiveController(new[] { point }, out var failure);
        if (controller == null)
            return IoTestSessionActionResult.Failure(failure);

        var result = controller.CaptureOperatorSnapshot(point, slot);
        RaiseProjectionProperties();
        return result;
    }

    public IoTestSessionActionResult RecaptureValues(
        IReadOnlyCollection<IoTestPointPlan> points,
        FatValueSlot slot)
        => BatchPointAction(
            points,
            (controller, batch) => controller.RecaptureValues(batch, slot),
            "Select one or more rows from one active IED FAT session before using Recapture.");

    public IoTestSessionActionResult BeginPairRecapture(IReadOnlyCollection<IoTestPointPlan> points)
        => BatchPointAction(
            points,
            (controller, batch) => controller.BeginPairRecapture(batch),
            "Select one or more rows from one active IED FAT session before staging Value 1 & Value 2 Recapture.");

    public IoTestSessionActionResult CancelPairRecapture(IReadOnlyCollection<IoTestPointPlan> points)
        => BatchPointAction(
            points,
            (controller, batch) => controller.CancelPairRecapture(batch),
            "Select one or more rows from one active IED FAT session before cancelling staged Recapture.");

    /// <summary>
    /// Runtime event route for additional P2 leaves only. MainWindow already routes every
    /// event to PrimaryController, therefore including it here would duplicate primary
    /// evidence. Each sibling leaf still enforces its own exact DeviceId boundary.
    /// </summary>
    public void EnqueueAdditional(Iec61850EventEntry entry)
    {
        if (entry == null || _disposed)
            return;

        foreach (var controller in ControllerSnapshot())
        {
            if (!ReferenceEquals(controller, _primaryController))
                controller.Enqueue(entry);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        var controllers = ControllerSnapshot();
        foreach (var controller in controllers)
            controller.PropertyChanged -= Child_PropertyChanged;

        foreach (var controller in controllers)
            controller.Dispose();

        lock (_sync)
            _controllers.Clear();
    }

    private IoTestSessionActionResult BatchPointAction(
        IReadOnlyCollection<IoTestPointPlan>? points,
        Func<IoTestSessionController, IReadOnlyCollection<IoTestPointPlan>, IoTestSessionActionResult> action,
        string missingMessage)
    {
        ThrowIfDisposed();
        var batch = points?.Where(point => point != null).Distinct().ToArray() ?? Array.Empty<IoTestPointPlan>();
        if (batch.Length == 0)
            return IoTestSessionActionResult.Failure(missingMessage);

        var controller = ResolveOwningActiveController(batch, out var failure);
        if (controller == null)
            return IoTestSessionActionResult.Failure(failure);

        var result = action(controller, batch);
        RaiseProjectionProperties();
        return result;
    }

    private IoTestSessionController? ResolveOwningActiveController(
        IReadOnlyCollection<IoTestPointPlan> points,
        out string failure)
    {
        failure = string.Empty;
        if (points.Count == 0)
        {
            failure = "Select at least one FAT row.";
            return null;
        }

        var owners = _project.Ieds
            .Where(ied => points.Any(ied.TestPoints.Contains))
            .Distinct()
            .ToArray();
        if (owners.Length != 1 || points.Any(point => !owners[0].TestPoints.Contains(point)))
        {
            failure = "Bulk Recapture must contain rows from exactly one IED. No evidence changed.";
            return null;
        }

        lock (_sync)
        {
            if (!_controllers.TryGetValue(owners[0], out var controller) || !controller.IsSessionActive)
            {
                failure = $"Start the {owners[0].IedName} FAT session before using Recapture.";
                return null;
            }
            return controller;
        }
    }

    private IoTestSessionController GetOrCreateController(IoTestIedPlan ied)
    {
        lock (_sync)
        {
            if (_controllers.TryGetValue(ied, out var existing))
                return existing;

            IoTestSessionController controller;
            if (_primaryIed == null)
            {
                controller = _primaryController;
                _primaryIed = ied;
            }
            else
            {
                if (_siblingFactory == null)
                {
                    throw new InvalidOperationException(
                        "Parallel FAT evidence is not attached to the Engineering runtime yet. Reopen the FAT workspace and retry.");
                }

                controller = _siblingFactory()
                    ?? throw new InvalidOperationException("The parallel FAT evidence controller factory returned no controller.");
                if (ReferenceEquals(controller, _primaryController) || _controllers.Values.Contains(controller))
                {
                    controller.Dispose();
                    throw new InvalidOperationException(
                        "Parallel FAT evidence requires one isolated session controller per IED.");
                }
                controller.PropertyChanged += Child_PropertyChanged;
            }

            _controllers.Add(ied, controller);
            return controller;
        }
    }

    private IoTestSessionActionResult SelectedAction(
        Func<IoTestSessionController, IoTestSessionActionResult> action,
        string missingMessage)
    {
        ThrowIfDisposed();
        var controller = SelectedController;
        if (controller == null)
            return IoTestSessionActionResult.Failure(missingMessage);
        var result = action(controller);
        RaiseProjectionProperties();
        return result;
    }

    private List<IoTestSessionController> ControllerSnapshot()
    {
        lock (_sync)
        {
            var result = _controllers.Values.Distinct().ToList();
            if (!result.Contains(_primaryController))
                result.Add(_primaryController);
            return result;
        }
    }

    private void Child_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        => RaiseProjectionProperties();

    private void RaiseProjectionProperties()
    {
        Raise(nameof(SelectedIed));
        Raise(nameof(ActiveSessionCount));
        Raise(nameof(HasActiveSessions));
        Raise(nameof(IsSessionActive));
        Raise(nameof(IsSelectedSessionActive));
        Raise(nameof(CanStart));
        Raise(nameof(CanPause));
        Raise(nameof(CanResume));
        Raise(nameof(CanStop));
        Raise(nameof(CanSelectIed));
        Raise(nameof(CanEditPlan));
        Raise(nameof(State));
        Raise(nameof(ActiveIed));
        Raise(nameof(SessionId));
        Raise(nameof(StartedAtUtc));
        Raise(nameof(CompletedAtUtc));
        Raise(nameof(StatusText));
        Raise(nameof(JournalPath));
        Raise(nameof(EvidenceRecordCount));
        Raise(nameof(LastJournalHash));
        Raise(nameof(JournalIntegrityText));
        Raise(nameof(StateText));
        Raise(nameof(ActiveIedText));
        Raise(nameof(ProgressText));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(IoTestMultiSessionCoordinator));
    }
}
