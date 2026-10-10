using System;
using System.Collections;
using System.ComponentModel;
using System.Windows;
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

    private void ExplorerLiveGrid_Loaded(object sender, RoutedEventArgs e)
        => AttachIecNaturalGridSort(sender as DataGrid, IecNaturalLiveMonitorSort.Instance);

    private void CommandGrid_Loaded(object sender, RoutedEventArgs e)
        => AttachIecNaturalGridSort(sender as DataGrid, IecNaturalCommandSort.Instance);

    private void GlobalLiveGrid_Loaded(object sender, RoutedEventArgs e)
        => AttachIecNaturalGridSort(sender as DataGrid, IecNaturalGlobalMonitorSort.Instance);

    private void AttachIecNaturalGridSort(DataGrid? grid, IComparer comparer)
    {
        if (grid is null) return;
        if (_iecNaturallySortedGrids.Add(grid))
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(
                ItemsControl.ItemsSourceProperty, typeof(DataGrid));
            descriptor?.AddValueChanged(grid, (_, _) =>
                grid.Dispatcher.BeginInvoke(new Action(() => ApplyIecNaturalSort(grid, comparer))));
        }
        ApplyIecNaturalSort(grid, comparer);
    }

    private static void ApplyIecNaturalSort(DataGrid grid, IComparer comparer)
    {
        if (grid.ItemsSource is null) return;
        if (CollectionViewSource.GetDefaultView(grid.ItemsSource) is not ListCollectionView view)
            return;
        if (ReferenceEquals(view.CustomSort, comparer)) return;
        view.SortDescriptions.Clear();
        view.CustomSort = comparer;
    }
}
