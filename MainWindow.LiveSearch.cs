using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

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
            // WPF Filter assignment itself refreshes the CollectionView.
            // The old path refreshed twice even for an empty query on every
            // IED selection, causing avoidable synchronous SG7 UI work.
            if (view.Filter is not null)
                view.Filter = null;
            return;
        }
        using var filterPerf = IedBrowserPerformanceProbe.Measure(
            "Explorer.Filter", device.Name, device.Points.Count);

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
        // CollectionView.Filter setter already performs RefreshOrDefer.
        // A second explicit Refresh doubles the UI enumeration/sort cost.
    }

    private static bool Contains(string? value, string query)
        => !string.IsNullOrWhiteSpace(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);
}
