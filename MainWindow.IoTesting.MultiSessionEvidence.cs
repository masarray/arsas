using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private IoTestMultiSessionCoordinator? _activeIoTestMultiSessionCoordinator;

    /// <summary>
    /// P2 attaches the FAT window's additional per-IED evidence leaves to the same
    /// Engineering IEC 61850 runtime. The existing primary controller keeps its legacy
    /// Runtime_IoTestPointUpdated route; only sibling leaves use the additional route so a
    /// primary live observation can never be journaled twice. Every isolated controller is
    /// also registered with commissioning recovery so reconnect/auto-resume works per IED.
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
        _runtime.PointUpdated -= Runtime_IoTestAdditionalPointUpdated;
        _runtime.PointUpdated += Runtime_IoTestAdditionalPointUpdated;
    }

    internal void DetachIoTestParallelEvidenceSessions(IoTestMultiSessionCoordinator coordinator)
    {
        if (coordinator == null)
            return;
        if (ReferenceEquals(_activeIoTestMultiSessionCoordinator, coordinator))
            _activeIoTestMultiSessionCoordinator = null;
        _runtime.PointUpdated -= Runtime_IoTestAdditionalPointUpdated;
        ClearFatCommissioningControllers();
    }

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
