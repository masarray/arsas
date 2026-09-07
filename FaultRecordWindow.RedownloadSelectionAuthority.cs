using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ArIED61850Tester;

/// <summary>
/// One pointer-selection authority for the fault-record grid. Clicking either the SELECT
/// checkbox or anywhere on a transferable record row toggles the same visible selection.
/// Downloaded records use the staged-overwrite selection set; first-time records keep the
/// existing model IsSelected flag. Exactly one toggle is performed per click.
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
            !TryResolveTransferRow(e.OriginalSource as DependencyObject, out var row) ||
            row.Record.Files.Count == 0)
        {
            return;
        }

        if (row.LocalState == FaultRecordLocalState.Downloaded)
        {
            var recordId = row.Record.RecordId;
            if (!window._redownloadSelections.Add(recordId))
                window._redownloadSelections.Remove(recordId);
        }
        else
        {
            if (!row.CanSelectForDownload)
                return;
            row.IsSelected = !row.IsSelected;
        }

        // Suppress the legacy CheckBox/DataGrid handlers so a checkbox click and a row-body
        // click both mean exactly one toggle. Reconfigure the realized row immediately so the
        // operator sees the check mark on the same pointer action.
        e.Handled = true;
        window.ConfigureRecordRow(row);
        window.UpdateSmartSelectionUi();
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
