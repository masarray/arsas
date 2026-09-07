using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private IoTestMultiSessionCoordinator? _activeIoTestMultiSessionCoordinator;

    /// <summary>
    /// P2 attaches the FAT window's additional per-IED evidence leaves to the same
    /// Engineering IEC 61850 runtime. Physical-relay P0 no longer gives FAT a second raw
    /// PointUpdated stream: all primary/sibling leaves consume the already-coalesced
    /// Engineering process image after UiFlushTimer_Tick instead.
    /// </summary>
    internal void AttachIoTestParallelEvidenceSessions(
        IoTestMultiSessionCoordinator coordinator,
        string evidenceRoot)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceRoot);

        ClearFatCommissioningControllers();
        RegisterFatCommissioningController(coordinator.PrimaryController);
        coordinator.ConfigureSiblingFactory(() =>
        {
            var controller = CreateIoTestSession(coordinator.Project, evidenceRoot);
            RegisterFatCommissioningController(controller);
            return controller;
        });

        _activeIoTestMultiSessionCoordinator = coordinator;

        // Legacy direct route intentionally disabled for FAT P0 because it observes raw
        // frames before Engineering's process image is settled:
        // _runtime.PointUpdated += Runtime_IoTestAdditionalPointUpdated;
        _runtime.PointUpdated -= Runtime_IoTestAdditionalPointUpdated;
        AttachIoFatSharedProcessEvidenceRoute(coordinator);
    }

    internal void DetachIoTestParallelEvidenceSessions(IoTestMultiSessionCoordinator coordinator)
    {
        if (coordinator == null)
            return;

        DetachIoFatSharedProcessEvidenceRoute(coordinator);
        if (ReferenceEquals(_activeIoTestMultiSessionCoordinator, coordinator))
            _activeIoTestMultiSessionCoordinator = null;
        _runtime.PointUpdated -= Runtime_IoTestAdditionalPointUpdated;
        ClearFatCommissioningControllers();
    }

    // Retained as a compatibility implementation for older tests/source branches. The P0
    // relay-bench route above deliberately does not subscribe this raw callback.
    private void Runtime_IoTestAdditionalPointUpdated(Iec61850PointSnapshot snapshot)
    {
        var coordinator = _activeIoTestMultiSessionCoordinator;
        if (coordinator == null)
            return;

        var point = snapshot.Point;
        coordinator.EnqueueAdditional(new Iec61850EventEntry
        {
            Sequence = Interlocked.Increment(ref _ioTestObservationSequence),
            DeviceId = point.DeviceId,
            PointKey = point.PointKey,
            DeviceTimestamp = snapshot.DeviceTimestamp,
            DeviceName = point.DeviceName,
            IpAddress = point.IpAddress,
            SignalName = point.SignalName,
            IecReference = point.IecReference,
            OldValue = snapshot.PreviousValue,
            NewValue = snapshot.Value,
            Quality = snapshot.Quality,
            SourceMode = snapshot.SourceMode,
            Reason = snapshot.Reason
        });
    }
}
