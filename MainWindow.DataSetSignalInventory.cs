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

        // A static FCDA such as CSWI/XCBR.Pos is dual-role: the control DO must remain a
        // command object, while ARIEC's exact ST primary leaf must also exist as a normal
        // report-backed runtime row. Materialize that status facet before Static DataSet
        // authority selection so control semantics never have to weaken CanPublishToRuntime.
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
                $"Static DataSet dual-role control projection: restored {controlStatusProjection.AddedCount} exact ST status row(s) and linked {controlStatusProjection.LinkedControlCount} exact control object(s); control service leaves remain excluded from Live Signal Values.");
        }
    }
}
