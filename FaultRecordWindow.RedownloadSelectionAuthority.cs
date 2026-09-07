using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ArIED61850Tester;

/// <summary>
/// Makes the SELECT column itself the hit target for an already-downloaded record. The
/// legacy checkbox is initially disabled by its row model and may therefore never become the
/// OriginalSource of the mouse event. A DataGrid class handler runs before the existing
/// instance handler, remembers the initial re-download state, and on mouse-up supplies the
/// toggle only when the existing handler did not. This preserves the safe staged overwrite
/// workflow in RedownloadUx and removes first-click/virtualization races.
/// </summary>
public partial class FaultRecordWindow
{
    private static readonly bool RedownloadSelectionAuthorityRegistered = RegisterRedownloadSelectionAuthority();
    private readonly Dictionary<string, bool> _redownloadPointerInitialState =
        new(StringComparer.OrdinalIgnoreCase);

    private static bool RegisterRedownloadSelectionAuthority()
    {
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(RedownloadSelectionAuthority_Down),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(RedownloadSelectionAuthority_Up),
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
            !TryResolveDownloadedSelectCell(grid, e.OriginalSource as DependencyObject, out var row))
        {
            return;
        }

        window._redownloadPointerInitialState[row.Record.RecordId] =
            window._redownloadSelections.Contains(row.Record.RecordId);
    }

    private static void RedownloadSelectionAuthority_Up(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid ||
            Window.GetWindow(grid) is not FaultRecordWindow window ||
            !ReferenceEquals(grid, window.FaultRecordsGrid) ||
            e.ChangedButton != MouseButton.Left ||
            !TryResolveDownloadedSelectCell(grid, e.OriginalSource as DependencyObject, out var row))
        {
            return;
        }

        var recordId = row.Record.RecordId;
        if (!window._redownloadPointerInitialState.Remove(recordId, out var initialState))
            return;

        var currentState = window._redownloadSelections.Contains(recordId);
        if (currentState == initialState)
        {
            // The older checkbox-only handler never saw this click (normally because the
            // disabled checkbox was not the event source). Supply exactly one toggle here.
            if (!window._redownloadSelections.Add(recordId))
                window._redownloadSelections.Remove(recordId);
        }

        e.Handled = true;
        window.ConfigureRecordRow(row);
        window.UpdateSmartSelectionUi();
    }

    private static bool TryResolveDownloadedSelectCell(
        DataGrid grid,
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
