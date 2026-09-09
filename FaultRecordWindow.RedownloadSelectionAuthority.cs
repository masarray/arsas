using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// One pointer-selection authority for the fault-record grid. Clicking the SELECT checkbox or a
/// non-action area of a transferable record row toggles FaultRecordRow.IsSelected exactly once.
/// Embedded row action buttons keep their own click authority and must never be converted into a
/// download-selection toggle. Local Downloaded state only changes transfer semantics from first
/// download to staged atomic replacement.
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
        var source = e.OriginalSource as DependencyObject;
        if (sender is not DataGrid grid ||
            Window.GetWindow(grid) is not FaultRecordWindow window ||
            !ReferenceEquals(grid, window.FaultRecordsGrid) ||
            window.IsBusy ||
            e.ChangedButton != MouseButton.Left ||
            IsIndependentRowAction(source) ||
            !TryResolveTransferRow(source, out var row) ||
            !row.CanSelectForDownload)
        {
            return;
        }

        row.IsSelected = !row.IsSelected;

        // Suppress the native checkbox/DataGrid toggle so one pointer action means exactly
        // one selection change. Repaint after input processing; doing this synchronously in
        // PreviewMouseDown lets the CheckBox template's pressed-state transition erase the
        // visible tick even though the model already changed.
        e.Handled = true;
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                window.ConfigureRecordRow(row);
                window.UpdateSmartSelectionUi();
            }));
    }

    private static bool IsIndependentRowAction(DependencyObject? source)
    {
        // A Button inside a row owns its pointer gesture. In particular the COMTRADE Open button
        // must be allowed to reach Button.Click instead of the preview row-selection authority.
        // CheckBox intentionally remains under the existing one-toggle selection path.
        return FindRedownloadSelectionAncestor<Button>(source) is not null;
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
