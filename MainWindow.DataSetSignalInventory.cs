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

        foreach (var signal in merge.AddedSignals)
        {
            // The wizard can recover a row after the normal discovery collection has
            // already been registered with MainWindow. Bring that row under the same
            // application lifecycle without changing its user-selection state.
            signal.PropertyChanged -= Signal_PropertyChanged;
            signal.PropertyChanged += Signal_PropertyChanged;
            _signalOwners[signal] = device;
        }

        if (merge.AddedCount == 0 && merge.EnrichedExistingCount == 0)
            return;

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
    }
}
