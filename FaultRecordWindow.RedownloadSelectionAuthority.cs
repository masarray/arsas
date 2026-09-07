using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ArIED61850Tester;

/// <summary>
/// Row-body selection authority for the fault-record grid. The real CheckBox remains owned
/// by RedownloadUx so WPF renders one normal check mark; this class handles only clicks on
/// the rest of a transferable row. Downloaded rows use the safe staged-overwrite selection
/// set while first-download rows keep FaultRecordRow.IsSelected.
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

        // The checkbox itself has one production owner in RedownloadUx. Do not toggle it
        // here as well; two PreviewMouseDown authorities caused the physical-bench state to
        // be repainted inconsistently and led to the extra check-glyph workaround.
        if (FindRedownloadSelectionAncestor<CheckBox>(source) != null)
            return;

        if (!TryResolveTransferRow(source, out var row) || row.Record.Files.Count == 0)
            return;

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

        // Row click owns exactly one toggle. ConfigureRecordRow writes the selected state to
        // the normal WPF CheckBox; no synthetic Content/glyph is injected into the control.
        e.Handled = true;
        window.ConfigureRecordRow(row);
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
