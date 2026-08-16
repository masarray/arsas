using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private void ExplorerLiveSearch_TextChanged(object sender, TextChangedEventArgs e)
        => ApplyExplorerLiveSearchFilter();

    private void ExplorerLiveSearchClear_Click(object sender, RoutedEventArgs e)
    {
        if (ExplorerLiveSearchBox == null)
            return;

        ExplorerLiveSearchBox.Clear();
        ExplorerLiveSearchBox.Focus();
    }

    private void ApplyExplorerLiveSearchFilter()
    {
        var device = SelectedDevice;
        if (device == null)
            return;

        var view = CollectionViewSource.GetDefaultView(device.Points);
        if (view == null || !view.CanFilter)
            return;

        var query = ExplorerLiveSearchBox?.Text?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            view.Filter = null;
            view.Refresh();
            return;
        }

        view.Filter = item =>
        {
            if (item is not Iec61850MonitorPoint point)
                return false;

            return Contains(point.SignalName, query) ||
                   Contains(point.IecTelegram, query) ||
                   Contains(point.IecReference, query) ||
                   Contains(point.DisplayValue, query) ||
                   Contains(point.Quality, query) ||
                   Contains(point.SourceMode, query) ||
                   Contains(point.Category, query) ||
                   Contains(point.IecDataType, query);
        };
        view.Refresh();
    }

    private static bool Contains(string? value, string query)
        => !string.IsNullOrWhiteSpace(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);
}
