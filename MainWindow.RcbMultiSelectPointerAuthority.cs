using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Physical pointer/keyboard authority for RCB export. Preview input is intercepted before
/// the legacy XAML Click handler can run, so the production IED-card RCB action always opens
/// the generic multi-select exporter. Inside that exporter RcbExportRow.IsSelected is the
/// inclusion authority; DataGrid row selection remains navigation/focus only.
/// </summary>
public partial class MainWindow
{
    private static readonly ConditionalWeakTable<DataGrid, RcbSelectionAnchor> RcbSelectionAnchors = new();

    [ModuleInitializer]
    internal static void RegisterRcbMultiSelectPointerAuthority()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(RcbExport_PreviewMouseLeftButtonDown),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(Button),
            UIElement.PreviewKeyDownEvent,
            new KeyEventHandler(RcbExport_PreviewKeyDown),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(RcbMultiGrid_PreviewMouseLeftButtonDown),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            UIElement.PreviewKeyDownEvent,
            new KeyEventHandler(RcbMultiGrid_PreviewKeyDown),
            handledEventsToo: true);
    }

    private static void RcbExport_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || sender is not Button button ||
            Window.GetWindow(button) is not MainWindow window || !IsProductionRcbButton(button))
        {
            return;
        }

        // Preview input happens before Button.Click, therefore the legacy singular XAML
        // handler cannot open at all on this operator action.
        e.Handled = true;
        window.IedEditRcbMulti_Click(button, e);
    }

    private static void RcbExport_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) || sender is not Button button ||
            Window.GetWindow(button) is not MainWindow window || !IsProductionRcbButton(button))
        {
            return;
        }

        e.Handled = true;
        window.IedEditRcbMulti_Click(button, e);
    }

    private static bool IsProductionRcbButton(Button button)
    {
        var toolTip = button.ToolTip?.ToString() ?? string.Empty;
        return button.Tag != null &&
               toolTip.Contains("RCB Export", StringComparison.OrdinalIgnoreCase);
    }

    private static void RcbMultiGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || sender is not DataGrid grid ||
            Window.GetWindow(grid) is not RcbMultiExportWindow ||
            e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (FindRcbAncestor<DataGridColumnHeader>(source) != null ||
            FindRcbAncestor<Button>(source) != null)
        {
            return;
        }

        var visualRow = FindRcbAncestor<DataGridRow>(source);
        if (visualRow?.DataContext is not RcbExportRow row)
            return;

        var rows = grid.Items.Cast<object>()
            .OfType<RcbExportRow>()
            .ToList();
        var targetIndex = rows.IndexOf(row);
        if (targetIndex < 0)
            return;

        ApplyRcbPointerSelection(
            grid,
            rows,
            targetIndex,
            (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
        grid.SelectedItem = row;
        visualRow.IsSelected = true;
        e.Handled = true;
    }

    private static void RcbMultiGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || sender is not DataGrid grid ||
            Window.GetWindow(grid) is not RcbMultiExportWindow ||
            grid.SelectedItem is not RcbExportRow row)
        {
            return;
        }

        var rows = grid.Items.Cast<object>()
            .OfType<RcbExportRow>()
            .ToList();
        var targetIndex = rows.IndexOf(row);
        if (targetIndex < 0)
            return;

        ApplyRcbPointerSelection(
            grid,
            rows,
            targetIndex,
            (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
        e.Handled = true;
    }

    internal static void ApplyRcbSelectionForTest(
        IList<RcbExportRow> rows,
        ref int anchorIndex,
        ref bool anchorValue,
        int targetIndex,
        bool extendRange)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (targetIndex < 0 || targetIndex >= rows.Count)
            throw new ArgumentOutOfRangeException(nameof(targetIndex));

        if (extendRange && anchorIndex >= 0 && anchorIndex < rows.Count)
        {
            var first = Math.Min(anchorIndex, targetIndex);
            var last = Math.Max(anchorIndex, targetIndex);
            for (var index = first; index <= last; index++)
                rows[index].IsSelected = anchorValue;
            return;
        }

        rows[targetIndex].IsSelected = !rows[targetIndex].IsSelected;
        anchorIndex = targetIndex;
        anchorValue = rows[targetIndex].IsSelected;
    }

    private static void ApplyRcbPointerSelection(
        DataGrid grid,
        IList<RcbExportRow> rows,
        int targetIndex,
        bool extendRange)
    {
        var anchor = RcbSelectionAnchors.GetOrCreateValue(grid);
        var anchorIndex = anchor.Index;
        var anchorValue = anchor.Value;
        ApplyRcbSelectionForTest(rows, ref anchorIndex, ref anchorValue, targetIndex, extendRange);
        anchor.Index = anchorIndex;
        anchor.Value = anchorValue;
    }

    private static T? FindRcbAncestor<T>(DependencyObject? source) where T : DependencyObject
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

    private sealed class RcbSelectionAnchor
    {
        public int Index { get; set; } = -1;
        public bool Value { get; set; }
    }
}
