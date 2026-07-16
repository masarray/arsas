using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private void QueueExplorerSignalGridLayout()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(ConfigureExplorerSignalGridForCompactFit));
    }

    private void ConfigureExplorerSignalGridForCompactFit()
    {
        var signalGrid = FindExplorerVisualChildren<DataGrid>(MainTabs)
            .FirstOrDefault(grid =>
            {
                if (grid.Columns.Count != 6)
                    return false;

                var headers = grid.Columns
                    .Select(column => column.Header?.ToString() ?? string.Empty)
                    .ToArray();

                return headers.SequenceEqual(new[]
                {
                    "Signal",
                    "IEC Telegram",
                    "Value",
                    "Quality",
                    "IED Timestamp",
                    "Acquisition"
                });
            });

        if (signalGrid == null)
            return;

        ScrollViewer.SetHorizontalScrollBarVisibility(signalGrid, ScrollBarVisibility.Disabled);
        signalGrid.CanUserResizeColumns = false;
        signalGrid.FrozenColumnCount = 0;

        var weights = new[] { 1.00, 1.55, 0.82, 0.68, 1.15, 1.05 };
        var minimums = new[] { 90d, 140d, 80d, 70d, 135d, 115d };

        for (var index = 0; index < signalGrid.Columns.Count; index++)
        {
            signalGrid.Columns[index].MinWidth = minimums[index];
            signalGrid.Columns[index].Width = new DataGridLength(weights[index], DataGridLengthUnitType.Star);
        }
    }

    private static IEnumerable<T> FindExplorerVisualChildren<T>(DependencyObject? root)
        where T : DependencyObject
    {
        if (root == null)
            yield break;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
                yield return typed;

            foreach (var descendant in FindExplorerVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
