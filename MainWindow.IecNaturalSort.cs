using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

/// <summary>
/// Natural IEC sorting is attached to the actual native seven-column grid.
/// The earlier search-module heuristic expected six columns and never ran.
/// This changes only the WPF collection view, not the report/DataSet order.
/// </summary>
public partial class MainWindow
{
    private readonly HashSet<DataGrid> _iecNaturallySortedGrids = new();

    private void ExplorerLiveGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (_iecNaturallySortedGrids.Add(grid))
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(
                ItemsControl.ItemsSourceProperty, typeof(DataGrid));
            descriptor?.AddValueChanged(grid, (_, _) =>
                grid.Dispatcher.BeginInvoke(new Action(() => ApplyExplorerNaturalSort(grid))));
        }
        ApplyExplorerNaturalSort(grid);
    }

    private static void ApplyExplorerNaturalSort(DataGrid grid)
    {
        if (grid.ItemsSource is null) return;
        if (CollectionViewSource.GetDefaultView(grid.ItemsSource) is not ListCollectionView view)
            return;
        if (ReferenceEquals(view.CustomSort, IecNaturalLiveMonitorSort.Instance))
            return;
        view.SortDescriptions.Clear();
        view.CustomSort = IecNaturalLiveMonitorSort.Instance;
    }
}
