using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private readonly LivePointViewportPriorityTracker _livePointViewport = new();
    private readonly HashSet<DataGrid> _viewportPriorityAttachedGrids = new();

    // Dictionary is owned by the WPF dispatcher. Changes to the ObservableCollection
    // invalidate it: never scan/group every connected IED on every 200-ms flush.
    private readonly Dictionary<string, Iec61850MonitorDevice> _uiPointOwners =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _uiPointOwnersDirty = true;

    private IReadOnlyDictionary<string, Iec61850MonitorDevice> UiPointOwners()
    {
        if (!_uiPointOwnersDirty) return _uiPointOwners;
        _uiPointOwners.Clear();
        foreach (var device in Devices)
            _uiPointOwners.TryAdd(device.DeviceId, device);
        _uiPointOwnersDirty = false;
        return _uiPointOwners;
    }

    private void AttachLivePointViewportPriority(DataGrid? grid)
    {
        if (grid is null || !_viewportPriorityAttachedGrids.Add(grid)) return;
        grid.LoadingRow += LivePointViewport_LoadingRow;
        grid.UnloadingRow += LivePointViewport_UnloadingRow;
        grid.Unloaded += LivePointViewport_Unloaded;
    }

    private LivePointViewportKind LivePointViewportType(DataGrid grid)
        => ReferenceEquals(grid, GlobalLiveGrid)
            ? LivePointViewportKind.Global : LivePointViewportKind.Explorer;

    private void LivePointViewport_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (sender is not DataGrid grid ||
            e.Row.Item is not Iec61850MonitorPoint point) return;
        var key = CanonicalPointSnapshotAuthority.UiQueueKey(point);
        if (!_livePointViewport.Realize(LivePointViewportType(grid), key)) return;

        // When a user scrolls into a long-existing backlog, prioritize only
        // this newly realized row; do not re-enumerate 25k pending IED values.
        if (_pendingPointSnapshots.ContainsKey(key))
            _priorityPointUiKeys.Enqueue(key);
    }

    private void LivePointViewport_UnloadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (sender is not DataGrid grid ||
            e.Row.Item is not Iec61850MonitorPoint point) return;
        _livePointViewport.Unrealize(LivePointViewportType(grid),
            CanonicalPointSnapshotAuthority.UiQueueKey(point));
    }

    private void LivePointViewport_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is DataGrid grid && ReferenceEquals(e.OriginalSource, grid))
            _livePointViewport.Clear(LivePointViewportType(grid));
    }

    // Every fourth *accepted* batch slot is reserved for the older background
    // FIFO. This prevents a continuous visible Report storm from starving
    // other IEDs, while newly visible cells take the first three slots.
    private bool TryDequeueUiPointKey(int processedPoints, out string key)
    {
        if ((processedPoints & 3) == 3 &&
            _pendingPointUiKeys.TryDequeue(out key))
            return true;
        if (_priorityPointUiKeys.TryDequeue(out key))
            return true;
        return _pendingPointUiKeys.TryDequeue(out key);
    }
}
