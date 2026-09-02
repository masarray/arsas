using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    /// <summary>
    /// P2 attaches the FAT window's additional per-IED evidence leaves to the same
    /// Engineering IEC 61850 runtime. The existing primary controller keeps its legacy
    /// Runtime_IoTestPointUpdated route; only sibling leaves use EnqueueAdditional so a
    /// primary event can never be journaled twice.
    /// </summary>
    internal void AttachIoTestParallelEvidenceSessions(
        IoTestMultiSessionCoordinator coordinator,
        string evidenceRoot)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceRoot);

        coordinator.ConfigureSiblingFactory(
            () => CreateIoTestSession(coordinator.Project, evidenceRoot));
        _runtime.PointUpdated -= coordinator.EnqueueAdditional;
        _runtime.PointUpdated += coordinator.EnqueueAdditional;
    }

    internal void DetachIoTestParallelEvidenceSessions(IoTestMultiSessionCoordinator coordinator)
    {
        if (coordinator == null)
            return;
        _runtime.PointUpdated -= coordinator.EnqueueAdditional;
    }
}
