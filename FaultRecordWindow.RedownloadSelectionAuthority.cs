using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// One pointer-selection authority for the fault-record grid. Clicking either the SELECT
/// checkbox or anywhere on a transferable record row toggles the same selection exactly once.
/// Downloaded records use the staged-overwrite selection set; first-time records keep the
/// existing model IsSelected flag. Visual checkbox state is committed after the input event so
/// WPF's native CheckBox mouse-state transition cannot repaint over the operator's tick.
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

        // Suppress the native checkbox/DataGrid toggle so one pointer action means exactly
        // one selection change. Repaint after input processing; doing this synchronously in
        // PreviewMouseDown lets the CheckBox template's pressed-state transition erase the
        // visible tick even though the selection count already changed.
        e.Handled = true;
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                window.ConfigureRecordRow(row);
                window.UpdateSmartSelectionUi();
            }));
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
