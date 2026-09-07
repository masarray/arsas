using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ArIED61850Tester;

/// <summary>
/// Gives already-downloaded COMTRADE rows one deterministic selection authority. The legacy
/// row model intentionally keeps Downloaded rows non-selectable for a first-time transfer,
/// while RedownloadUx owns a separate multi-selection set for safe staged overwrite. This
/// class handler intercepts the SELECT cell before the legacy grid handler so one pointer
/// action produces exactly one toggle, including when the legacy CheckBox is disabled or a
/// recycled DataGridRow is being used.
/// </summary>
public partial class FaultRecordWindow
{
    private static readonly bool RedownloadSelectionAuthorityRegistered = RegisterRedownloadSelectionAuthority();

    private static bool RegisterRedownloadSelectionAuthority()
    {
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(RedownloadSelectionAuthority_Down),
            handledEventsToo: true);
        return true;
    }

    private static void RedownloadSelectionAuthority_Down(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid ||
            Window.GetWindow(grid) is not FaultRecordWindow window ||
            !ReferenceEquals(grid, window.FaultRecordsGrid) ||
            window.IsBusy ||
            e.ChangedButton != MouseButton.Left ||
            !TryResolveDownloadedSelectCell(e.OriginalSource as DependencyObject, out var row))
        {
            return;
        }

        var recordId = row.Record.RecordId;
        if (!window._redownloadSelections.Add(recordId))
            window._redownloadSelections.Remove(recordId);

        // Prevent RedownloadUx's older checkbox tunnelling handler from toggling the same
        // record a second time. The overlay remains the sole writer for Downloaded rows.
        e.Handled = true;
        window.ConfigureRecordRow(row);
        window.UpdateSmartSelectionUi();
    }

    private static bool TryResolveDownloadedSelectCell(
        DependencyObject? source,
        out FaultRecordRow row)
    {
        row = null!;
        var cell = FindRedownloadSelectionAncestor<DataGridCell>(source);
        if (cell == null || cell.Column == null || cell.Column.DisplayIndex != 0)
            return false;

        if (cell.DataContext is not FaultRecordRow candidate ||
            candidate.LocalState != FaultRecordLocalState.Downloaded ||
            candidate.Record.Files.Count == 0)
        {
            return false;
        }

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
