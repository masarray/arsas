using System;
using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

/// <summary>
/// Numeric-aware sorting is installed on each real grid, not guessed by
/// column counts. Never mutates original RCB, FCDA or control signal order.
/// </summary>
public partial class MainWindow
{
    private readonly HashSet<DataGrid> _iecNaturallySortedGrids = new();
    private bool _globalNaturalSortThresholdQueued;
    private bool _globalNaturalSortLargeMode;

    private void ExplorerLiveGrid_Loaded(object sender, RoutedEventArgs e)
    {
        AttachLivePointViewportPriority(sender as DataGrid);
        AttachIecNaturalGridSort(sender as DataGrid, IecNaturalLiveMonitorSort.Instance);
    }

    private void CommandGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid)
            return;
        InstallCommandGridBehavior(grid);
        AttachIecNaturalGridSort(grid, IecNaturalCommandSort.Instance);
    }

    private void GlobalLiveGrid_Loaded(object sender, RoutedEventArgs e)
    {
        AttachLivePointViewportPriority(sender as DataGrid);
        AttachIecNaturalGridSort(sender as DataGrid, IecNaturalGlobalMonitorSort.Instance);
    }

    private void AttachIecNaturalGridSort(DataGrid? grid, IComparer comparer)
    {
        if (grid is null) return;
        if (_iecNaturallySortedGrids.Add(grid))
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(
                ItemsControl.ItemsSourceProperty, typeof(DataGrid));
            descriptor?.AddValueChanged(grid, (_, _) =>
                grid.Dispatcher.BeginInvoke(new Action(() => ApplyIecNaturalSort(grid, comparer))));

            if (ReferenceEquals(grid, GlobalLiveGrid))
            {
                // Source instance stays stable as device sets grow/shrink.
                // Re-evaluate only when crossing the 2048-point threshold;
                // not for each of the next 32,900 incoming point additions.
                _globalNaturalSortLargeMode = GlobalPoints.Count > 2048;
                GlobalPoints.CollectionChanged += (_, _) =>
                {
                    if ((GlobalPoints.Count > 2048) == _globalNaturalSortLargeMode ||
                        _globalNaturalSortThresholdQueued)
                        return;
                    _globalNaturalSortThresholdQueued = true;
                    grid.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        _globalNaturalSortThresholdQueued = false;
                        _globalNaturalSortLargeMode = GlobalPoints.Count > 2048;
                        ApplyIecNaturalSort(grid, comparer);
                    }));
                };
            }
        }
        ApplyIecNaturalSort(grid, comparer);
    }

    private static void ApplyIecNaturalSort(DataGrid grid, IComparer comparer)
    {
        if (grid.ItemsSource is null) return;
        // WPF CustomSort performs O(N log N) work synchronously on the Dispatcher.
        // Both process grids can hold SG7-size models (Global may contain 4 IEDs).
        // Preserve stable source order for large lists, without changing IEC
        // DataSet/FCDA order. Command Dock ALWAYS retains its verified ordering.
        if (CollectionViewSource.GetDefaultView(grid.ItemsSource) is not ListCollectionView view)
            return;
        if ((grid.Name is "ExplorerLiveValuesGrid" or "GlobalLiveGrid") &&
            grid.ItemsSource is ICollection { Count: > 2048 })
        {
            // A live collection may grow past the threshold AFTER a previous
            // small-grid CustomSort was installed. Remove only our own sorter.
            if (ReferenceEquals(view.CustomSort, comparer))
                view.CustomSort = null;
            return;
        }
        if (ReferenceEquals(view.CustomSort, comparer)) return;
        view.SortDescriptions.Clear();
        view.CustomSort = comparer;
    }
}
