using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ArIED61850Tester;

/// <summary>
/// Row-body selection authority for the fault-record grid. Native WPF CheckBoxes own checkbox
/// clicks. This class handles only clicks on the rest of a transferable row and routes downloaded
/// rows through the same notifying selection proxy used by the checkbox and header selection.
/// </summary>
public partial class FaultRecordWindow
{
    [ModuleInitializer]
    internal static void RegisterRedownloadSelectionAuthority()
    {
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(RedownloadSelectionAuthority_Down),
            handledEventsToo: true);
    }

    private static void RedownloadSelectionAuthority_Down(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid ||
            Window.GetWindow(grid) is not FaultRecordWindow window ||
            !ReferenceEquals(grid, window.FaultRecordsGrid) ||
            window.IsBusy ||
            e.ChangedButton != MouseButton.Left ||
            e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        // Native checkbox binding owns checkbox clicks. Row-body clicks are a fast-workflow
        // convenience and must not create a second toggle for the same pointer action.
        if (FindRedownloadSelectionAncestor<CheckBox>(source) != null)
            return;

        if (!TryResolveTransferRow(source, out var row) || row.Record.Files.Count == 0)
            return;

        if (row.LocalState == FaultRecordLocalState.Downloaded)
        {
            window.SetDownloadedTransferSelection(row, !window.IsDownloadedTransferSelected(row));
        }
        else
        {
            if (!row.CanSelectForDownload)
                return;
            row.IsSelected = !row.IsSelected;
        }

        e.Handled = true;
        window.UpdateSmartSelectionUi();
        window.RefreshFaultRecordHeaderSelection();
    }

    private static bool TryResolveTransferRow(DependencyObject? source, out FaultRecordRow row)
    {
        row = null!;
        var dataGridRow = FindRedownloadSelectionAncestor<DataGridRow>(source);
        if (dataGridRow?.DataContext is not FaultRecordRow candidate)
            return false;

        row = candidate;
        return true;
    }

    private static T? FindRedownloadSelectionAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        var current = source;
        while (current != null)
        {
            if (current is T match)
                return match;

            DependencyObject? parent = null;
            try
            {
                parent = VisualTreeHelper.GetParent(current);
            }
            catch (InvalidOperationException)
            {
            }

            if (parent == null && current is FrameworkElement element)
                parent = element.Parent;
            current = parent;
        }

        return null;
    }
}
