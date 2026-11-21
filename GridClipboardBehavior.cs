using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// One clipboard behavior for read-only engineering DataGrids, including
/// virtualized windows. A separate, scoped adapter supports the IED Browser
/// property ListView without changing its navigation and write-selection logic.
/// Does not run on telemetry refresh, allocate row visuals, or touch IEC sessions.
/// </summary>
internal static class GridClipboardBehavior
{
    public static readonly DependencyProperty ExcelCellsProperty =
        DependencyProperty.RegisterAttached("ExcelCells", typeof(bool),
            typeof(GridClipboardBehavior), new PropertyMetadata(false));

    public static bool GetExcelCells(DependencyObject target)
        => (bool)target.GetValue(ExcelCellsProperty);

    public static void SetExcelCells(DependencyObject target, bool value)
        => target.SetValue(ExcelCellsProperty, value);

    private sealed class GridState
    {
        public object? HitItem;
        public DataGridColumn? HitColumn;
    }

    private sealed class BrowserState
    {
        public IedBrowserDetailRow? HitRow;
    }

    private static readonly ConditionalWeakTable<DataGrid, GridState> Grids = new();
    private static readonly ConditionalWeakTable<ListView, BrowserState> BrowserLists = new();
    private static int _installed;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0) return;
        EventManager.RegisterClassHandler(typeof(DataGrid),
            FrameworkElement.LoadedEvent, new RoutedEventHandler(GridLoaded), true);
        EventManager.RegisterClassHandler(typeof(ListView),
            FrameworkElement.LoadedEvent, new RoutedEventHandler(BrowserLoaded), true);
    }

    private static void GridLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid || !ReferenceEquals(e.OriginalSource, grid) ||
            !grid.IsReadOnly || Grids.TryGetValue(grid, out _))
            return;

        var state = new GridState();
        Grids.Add(grid, state);
        grid.ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader;
        if (GetExcelCells(grid))
        {
            // Opt-in only: a click in tree/file/RCB/command grids must continue
            // selecting the exact engineering object, not just a visual cell.
            grid.SelectionMode = DataGridSelectionMode.Extended;
            grid.SelectionUnit = DataGridSelectionUnit.CellOrRowHeader;
        }

        grid.PreviewKeyDown += GridKeyDown;
        if (grid.ContextMenu is not null) return; // Preserve specialized actions.

        grid.PreviewMouseRightButtonDown += (_, mouse) =>
        {
            var cell = Ancestor<DataGridCell>(mouse.OriginalSource as DependencyObject);
            state.HitColumn = cell?.Column;
            state.HitItem = cell?.DataContext;
        };
        var menu = new ContextMenu();
        menu.Items.Add(Item("Copy cell value", () => CopyGrid(grid, state, "cell")));
        menu.Items.Add(Item("Copy selected cells / rows", () =>
            CopyGrid(grid, state, "selection"), "Ctrl+C"));
        menu.Items.Add(Item("Copy selected full rows", () =>
            CopyGrid(grid, state, "rows")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Copy visible table", () => CopyGrid(grid, state, "table")));
        grid.ContextMenu = menu;
    }

    private static void GridKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid grid || !IsCopyShortcut(e) ||
            InsideTextEditor(e.OriginalSource as DependencyObject)) return;
        if (!Grids.TryGetValue(grid, out var state)) return;
        e.Handled = true;
        CopyGrid(grid, state, "selection");
    }

    private static bool IsCopyShortcut(KeyEventArgs e)
        => e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control;

    private static bool InsideTextEditor(DependencyObject? source)
        => Ancestor<TextBoxBase>(source) is not null ||
           Ancestor<PasswordBox>(source) is not null;

    private static MenuItem Item(string caption, Action action, string? shortcut = null)
    {
        var item = new MenuItem { Header = caption };
        if (!string.IsNullOrEmpty(shortcut)) item.InputGestureText = shortcut;
        item.Click += (_, _) => action();
        return item;
    }

    private static T? Ancestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T found) return found;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    private static IReadOnlyList<DataGridColumn> VisibleColumns(DataGrid grid)
        => grid.Columns.Where(column => column.Visibility == Visibility.Visible)
            .OrderBy(column => column.DisplayIndex).ToArray();

    private static string CellValue(DataGridColumn column, object item)
    {
        // The DataGrid column is its own WPF clipboard binding authority:
        // respect Binding, StringFormat and ClipboardContentBinding, including
        // templates with explicit bindings. No visual realization/reflection.
        var value = column.OnCopyingCellClipboardContent(item);
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static void CopyGrid(DataGrid grid, GridState state, string mode)
    {
        var columns = VisibleColumns(grid);
        if (columns.Count == 0) return;
        if (mode == "cell")
        {
            var cell = grid.CurrentCell;
            var column = state.HitColumn ?? (cell.IsValid ? cell.Column : null);
            var item = state.HitItem ?? (cell.IsValid ? cell.Item : null);
            if (column is not null && item is not null &&
                columns.Contains(column) && !ReferenceEquals(item, CollectionView.NewItemPlaceholder))
                SetClipboard(grid, CellValue(column, item));
            return;
        }

        IReadOnlyList<DataGridColumn> copyColumns = columns;
        IEnumerable<IReadOnlyList<string?>> rows;
        if (mode == "table")
        {
            rows = EnumerateAll(grid, copyColumns);
        }
        else
        {
            // The selection itself is WPF-owned; only reconstruct its sorted
            // viewport order at copy time. SelectedCells supports rectangular
            // Excel-style blocks. Row mode explicitly expands to full columns.
            var selected = new Dictionary<object, HashSet<DataGridColumn>>(
                ReferenceEqualityComparer.Instance);
            if (mode == "selection")
            {
                foreach (var cell in grid.SelectedCells)
                {
                    if (!cell.IsValid || cell.Item is null) continue;
                    if (!selected.TryGetValue(cell.Item, out var set))
                        selected[cell.Item] = set = new HashSet<DataGridColumn>();
                    set.Add(cell.Column);
                    if (selected.Count > EngineeringClipboardText.MaxRows ||
                        grid.SelectedCells.Count > EngineeringClipboardText.MaxCells)
                    {
                        Inform(grid, "Selection exceeds the safe clipboard limit. Use Export CSV.");
                        return;
                    }
                }
            }
            // Row selection and the explicit Copy Rows command always include
            // each displayed column, even after sorting/filtering.
            if (mode == "rows" || selected.Count == 0)
            {
                foreach (var item in grid.SelectedItems.Cast<object>())
                    selected[item] = new HashSet<DataGridColumn>(columns);
                if (mode == "rows" && selected.Count == 0)
                    foreach (var cell in grid.SelectedCells)
                        if (cell.Item is not null)
                            selected[cell.Item] = new HashSet<DataGridColumn>(columns);
            }
            if (selected.Count == 0 && grid.CurrentCell.IsValid)
            {
                var cell = grid.CurrentCell;
                selected[cell.Item] = new HashSet<DataGridColumn>
                {
                    cell.Column
                };
            }
            if (selected.Count == 0) return;
            if (mode == "selection")
            {
                var wanted = new HashSet<DataGridColumn>(
                    selected.Values.SelectMany(set => set));
                copyColumns = columns.Where(wanted.Contains).ToArray();
            }
            rows = EnumerateSelected(grid, copyColumns, selected);
        }

        var headers = copyColumns.Select(column =>
            column.Header?.ToString() ?? string.Empty).ToArray();
        if (!EngineeringClipboardText.TryBuild(headers, rows,
                includeHeaders: true, out var text, out var failure))
        {
            Inform(grid, failure);
            return;
        }
        SetClipboard(grid, text);
    }

    private static IEnumerable<IReadOnlyList<string?>> EnumerateAll(
        DataGrid grid, IReadOnlyList<DataGridColumn> columns)
    {
        foreach (var item in grid.Items)
        {
            if (item is null || ReferenceEquals(item, CollectionView.NewItemPlaceholder))
                continue;
            yield return columns.Select(column => CellValue(column, item)).ToArray();
        }
    }

    private static IEnumerable<IReadOnlyList<string?>> EnumerateSelected(
        DataGrid grid, IReadOnlyList<DataGridColumn> columns,
        IReadOnlyDictionary<object, HashSet<DataGridColumn>> selection)
    {
        foreach (var item in grid.Items)
        {
            if (item is null || !selection.TryGetValue(item, out var picked))
                continue;
            yield return columns.Select(column =>
                picked.Contains(column) ? CellValue(column, item) : string.Empty).ToArray();
        }
    }

    private static void BrowserLoaded(object sender, RoutedEventArgs e)
    {
        // The IED detail pane is a virtualized ListView, not a DataGrid.
        if (sender is not ListView { Name: "IedBrowserDetailList" } list ||
            !ReferenceEquals(e.OriginalSource, list) ||
            BrowserLists.TryGetValue(list, out _)) return;

        var state = new BrowserState();
        BrowserLists.Add(list, state);
        list.PreviewKeyDown += (_, key) =>
        {
            if (!IsCopyShortcut(key) || InsideTextEditor(key.OriginalSource as DependencyObject))
                return;
            if (list.SelectedItem is IedBrowserDetailRow selected)
            {
                key.Handled = true;
                SetClipboard(list, selected.Value);
            }
        };
        if (list.ContextMenu is not null) return;
        list.PreviewMouseRightButtonDown += (_, mouse) =>
        {
            state.HitRow = Ancestor<ListViewItem>(mouse.OriginalSource as DependencyObject)
                ?.DataContext as IedBrowserDetailRow;
        };
        var menu = new ContextMenu();
        menu.Items.Add(Item("Copy value", () =>
        {
            var row = state.HitRow ?? list.SelectedItem as IedBrowserDetailRow;
            if (row is not null)
                SetClipboard(list, row.Value);
        }));
        menu.Items.Add(Item("Copy name and value", () =>
        {
            var row = state.HitRow ?? list.SelectedItem as IedBrowserDetailRow;
            if (row is not null) CopyDetailRows(list, [row]);
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Copy visible properties", () => CopyDetailRows(
            list, list.Items.OfType<IedBrowserDetailRow>()
                .Where(row => row.Kind == "Property"))));
        list.ContextMenu = menu;
    }

    private static void CopyDetailRows(
        ListView list, IEnumerable<IedBrowserDetailRow> details)
    {
        var rows = details.Select(row => (IReadOnlyList<string?>)
            new string?[] { row.Name, row.Value });
        if (EngineeringClipboardText.TryBuild(["Property", "Value"], rows,
                includeHeaders: true, out var text, out var failure))
            SetClipboard(list, text);
        else
            Inform(list, failure);
    }

    private static void SetClipboard(FrameworkElement owner, string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException)
        {
            Inform(owner, "Windows clipboard is busy or unavailable. No values were changed.");
        }
        catch (InvalidOperationException)
        {
            Inform(owner, "Windows clipboard is unavailable. No values were changed.");
        }
    }

    private static void Inform(FrameworkElement owner, string message)
    {
        if (Window.GetWindow(owner) is { } window)
            MessageBox.Show(window, message, "ARSAS · Copy", MessageBoxButton.OK,
                MessageBoxImage.Information);
        else
            MessageBox.Show(message, "ARSAS · Copy", MessageBoxButton.OK,
                MessageBoxImage.Information);
    }
}
