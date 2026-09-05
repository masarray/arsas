using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class MainWindow
{
    internal void RegisterRecoveredDataSetSignals(
        Iec61850MonitorDevice device,
        Iec61850DataSetSignalInventoryMergeResult merge)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(merge);

        // A controllable static DataSet member is dual-role. Exact SCL/ARIEC CDC and
        // DataObjectReference preserve the command companion independently from scalar
        // feedback resolution; an exact ST/MX PrimaryValueReference may additionally become
        // a normal report-backed runtime row. This separation keeps command discovery broad
        // without weakening CanPublishToRuntime or admitting control service leaves.
        var controlStatusProjection =
            Iec61850StaticControlStatusProjectionService.EnsureProjections(device);

        foreach (var signal in merge.AddedSignals.Concat(controlStatusProjection.AddedSignals))
        {
            // The wizard can recover a row after the normal discovery collection has
            // already been registered with MainWindow. Bring that row under the same
            // application lifecycle without changing its user-selection state.
            signal.PropertyChanged -= Signal_PropertyChanged;
            signal.PropertyChanged += Signal_PropertyChanged;
            _signalOwners[signal] = device;
        }

        if (merge.AddedCount == 0 &&
            merge.EnrichedExistingCount == 0 &&
            controlStatusProjection.AddedCount == 0 &&
            controlStatusProjection.LinkedControlCount == 0)
        {
            return;
        }

        device.RecountSelectedSignals();
        device.RefreshComputed();
        RaiseWorkspaceCounts();

        if (merge.AddedCount > 0)
        {
            AddLog(
                "INFO",
                device.Name,
                $"ARIEC DataSet authority restored {merge.AddedCount} mandatory primary signal(s) to the selection inventory; user selection was not changed.");
        }

        if (controlStatusProjection.AddedCount > 0 || controlStatusProjection.LinkedControlCount > 0)
        {
            AddLog(
                "INFO",
                device.Name,
                $"Static DataSet control projection: materialized {controlStatusProjection.AddedControlCount} exact control companion(s), " +
                $"{controlStatusProjection.AddedRuntimeFeedbackCount} exact runtime feedback row(s), and linked " +
                $"{controlStatusProjection.LinkedControlCount} controllable DataSet member(s). Commands remain gated by live ctlModel; " +
                "Oper/SBO/SBOw/Cancel/ctlVal/ctlModel service leaves remain excluded from Live Signal Values.");
        }
    }
}
