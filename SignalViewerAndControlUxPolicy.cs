using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Presentation-only sizing policy for the two signal viewers. Navigation, IED-card actions,
/// relay icon geometry, and command buttons are defined directly in XAML so their rendered
/// appearance cannot depend on Loaded-event timing or visual-tree traversal.
/// </summary>
internal static class SignalViewerAndControlUxPolicy
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnDataGridLoaded));
    }

    private static void OnDataGridLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not DataGrid grid)
            return;

        var bindingPath = grid.GetBindingExpression(ItemsControl.ItemsSourceProperty)?.ParentBinding.Path?.Path;
        if (!string.Equals(bindingPath, "SelectedDevice.Points", StringComparison.Ordinal) &&
            !string.Equals(bindingPath, "GlobalPoints", StringComparison.Ordinal))
        {
            return;
        }

        grid.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => ApplySignalViewerLayout(grid, bindingPath)));
    }

    private static void ApplySignalViewerLayout(DataGrid grid, string? bindingPath)
    {
        ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(grid, ScrollBarVisibility.Auto);
        grid.CanUserResizeColumns = false;
        grid.ClipToBounds = true;

        if (string.Equals(bindingPath, "SelectedDevice.Points", StringComparison.Ordinal))
        {
            SetColumn(grid, "Signal", 1.05, 120);
            SetColumn(grid, "IEC Telegram", 1.55, 180);
            SetColumn(grid, "Value", 0.72, 90);
            SetColumn(grid, "Quality", 0.62, 82);
            SetColumn(grid, "IED Timestamp", 1.05, 135);
            SetColumn(grid, "Acquisition", 1.00, 130);
            return;
        }

        SetColumn(grid, "IED", 0.62, 90);
        SetColumn(grid, "Signal", 1.05, 145);
        SetColumn(grid, "IEC Telegram", 1.72, 220);
        SetColumn(grid, "Value", 0.68, 86);
        SetColumn(grid, "Quality", 0.72, 88);
        SetColumn(grid, "IED Timestamp", 1.02, 140);
        SetColumn(grid, "Acquisition", 1.00, 135);
    }

    private static void SetColumn(DataGrid grid, string header, double star, double minimum)
    {
        var column = grid.Columns.FirstOrDefault(candidate =>
            string.Equals(candidate.Header?.ToString(), header, StringComparison.Ordinal));
        if (column is null)
            return;

        column.MinWidth = minimum;
        column.Width = new DataGridLength(star, DataGridLengthUnitType.Star);
        column.CanUserResize = false;
    }
}
