using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Pointer-selection authority for the fault-record grid. Downloaded rows use the safe
/// re-download selection set while first-download rows keep FaultRecordRow.IsSelected.
/// The visible check state is repainted after the complete WPF input/layout cycle and after
/// row virtualization/recycling; an explicit check glyph is shown for selected rows so the
/// operator never gets a selected counter with an apparently empty checkbox.
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
        EventManager.RegisterClassHandler(
            typeof(DataGridRow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(RedownloadSelectionRow_Loaded),
            handledEventsToo: true);
    }

    private static void RedownloadSelectionRow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGridRow visualRow ||
            visualRow.DataContext is not FaultRecordRow row ||
            Window.GetWindow(visualRow) is not FaultRecordWindow window)
        {
            return;
        }

        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => window.PaintTransferSelection(row)));
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

        // One pointer action owns exactly one toggle. Repaint only after WPF has completed
        // native CheckBox pressed/click layout; repainting at Input priority was still early
        // enough for the theme to erase the glyph on the physical bench.
        e.Handled = true;
        window.UpdateSmartSelectionUi();
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => window.PaintTransferSelection(row)));
    }

    private void PaintTransferSelection(FaultRecordRow row)
    {
        ConfigureRecordRow(row);

        if (FaultRecordsGrid.ItemContainerGenerator.ContainerFromItem(row) is not DataGridRow visualRow)
            return;

        var checkBox = FindVisualDescendants<CheckBox>(visualRow).FirstOrDefault();
        if (checkBox == null)
            return;

        var selected = row.LocalState == FaultRecordLocalState.Downloaded
            ? _redownloadSelections.Contains(row.Record.RecordId)
            : row.IsSelected;

        checkBox.IsChecked = selected;
        checkBox.Content = selected ? "✓" : string.Empty;
        checkBox.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal;
        checkBox.MinWidth = 28;
        checkBox.HorizontalContentAlignment = HorizontalAlignment.Right;
        checkBox.InvalidateVisual();
        UpdateSmartSelectionUi();
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
