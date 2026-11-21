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
/// One clipboard behavior for all engineering DataGrids, including
/// virtualized windows. A separate, scoped adapter supports the IED Browser
/// property ListView without changing its navigation and write-selection logic.
/// Does not run on telemetry refresh, allocate row visuals, or touch IEC sessions.
/// </summary>
public static class GridClipboardBehavior
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
            Grids.TryGetValue(grid, out _))
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
            InstallExcelSelectionVisual(grid);
        }

        grid.PreviewKeyDown += GridKeyDown;
        // Track mouse hits even when a row or grid supplies an action menu.
        // A file row's Download action must never shadow its Copy capability.
        grid.PreviewMouseLeftButtonDown += (_, _) =>
        {
            state.HitColumn = null;
            state.HitItem = null;
        };
        grid.PreviewMouseRightButtonDown += (_, mouse) =>
        {
            var cell = Ancestor<DataGridCell>(mouse.OriginalSource as DependencyObject);
            state.HitColumn = cell?.Column;
            state.HitItem = cell?.DataContext;
        };
        if (grid.ContextMenu is { } existing)
        {
            AppendCopyActions(grid, existing);
            return;
        }
        var menu = new ContextMenu();
        PopulateCopyMenu(menu, grid, null);
        grid.ContextMenu = menu;
    }

    // One menu factory for normal grid menus AND pre-existing specialized
    // menus (FAT Recapture, IED file Download), without replacing actions
    // or duplicating per-grid copy logic.
    private static readonly object CopyMenuTag = new();

    public static void AppendCopyActions(
        DataGrid grid, ContextMenu menu, object? exactRow = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(menu);
        if (menu.Items.OfType<MenuItem>().Any(item =>
                ReferenceEquals(item.Tag, CopyMenuTag)))
            return;
        var copy = new MenuItem { Header = "Copy", Tag = CopyMenuTag };
        PopulateCopyMenu(copy, grid, exactRow);
        if (menu.Items.Count != 0) menu.Items.Add(new Separator());
        menu.Items.Add(copy);
    }

    private static void PopulateCopyMenu(
        ItemsControl target, DataGrid grid, object? exactRow)
    {
        void Execute(string mode)
        {
            var state = Grids.TryGetValue(grid, out var installed)
                ? installed : new GridState();
            CopyGrid(grid, state, mode, exactRow);
        }

        target.Items.Add(Item("Copy cell value", () => Execute("cell")));
        if (exactRow is not null)
        {
            // Recycled file-row menus bind an exact observed file identity.
            // Do not copy a different row merely because SelectedItem moved.
            target.Items.Add(Item("Copy this row", () => Execute("context-row")));
            target.Items.Add(Item("Copy visible table", () => Execute("table")));
            return;
        }
        target.Items.Add(Item("Copy selected cells / rows", () =>
            Execute("selection"), "Ctrl+C"));
        target.Items.Add(Item("Copy selection with headers", () =>
            Execute("selection-headers")));
        target.Items.Add(Item("Copy selected full rows", () =>
            Execute("rows")));
        target.Items.Add(new Separator());
        target.Items.Add(Item("Copy visible table", () => Execute("table")));
    }

    // Both ARSAS themes make selected DataGridCells transparent. The WPF
    // selection was present, but users could not SEE Excel-style blocks.
    // Apply a derived cell style only on explicit spreadsheet grids; command
    // and identity grids retain their existing row/action styles.
    private static readonly Brush SelectedCellFill =
        FrozenBrush(Color.FromRgb(0xD8, 0xE9, 0xFF));
    private static readonly Brush SelectedCellBorder =
        FrozenBrush(Color.FromRgb(0x69, 0x9F, 0xDB));

    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static void InstallExcelSelectionVisual(DataGrid grid)
    {
        var baseStyle = grid.CellStyle ??
            grid.TryFindResource(typeof(DataGridCell)) as Style;
        var style = new Style(typeof(DataGridCell), baseStyle);
        var selected = new Trigger
        {
            Property = DataGridCell.IsSelectedProperty,
            Value = true
        };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, SelectedCellFill));
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, SelectedCellBorder));
        selected.Setters.Add(new Setter(Control.BorderThicknessProperty,
            new Thickness(1)));
        style.Triggers.Add(selected);
        grid.CellStyle = style;
    }

    private static string ColumnHeader(DataGridColumn column)
    {
        if (column.Header is string label) return label;
        // Filter headers are composite Grids, not string captions. Look only
        // at the small header logical tree during an operator copy action;
        // never realize or walk hidden/virtualized data rows.
        if (column.Header is DependencyObject root)
        {
            var queue = new Queue<DependencyObject>();
            queue.Enqueue(root);
            for (var inspected = 0; queue.Count != 0 && inspected < 128; inspected++)
            {
                var current = queue.Dequeue();
                if (current is TextBlock block && !string.IsNullOrWhiteSpace(block.Text))
                    return block.Text.Trim();
                foreach (var child in LogicalTreeHelper.GetChildren(current)
                    .OfType<DependencyObject>())
                    queue.Enqueue(child);
            }
        }
        if (!string.IsNullOrWhiteSpace(column.SortMemberPath))
            return column.SortMemberPath;
        if (column is DataGridBoundColumn { Binding: Binding binding } &&
            !string.IsNullOrWhiteSpace(binding.Path?.Path))
            return binding.Path.Path.Split('.').Last();
        return string.Empty;
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

    private static void CopyGrid(
        DataGrid grid, GridState state, string mode, object? exactRow = null)
    {
        var columns = VisibleColumns(grid);
        if (columns.Count == 0) return;
        if (mode == "cell")
        {
            var cell = grid.CurrentCell;
            var column = state.HitColumn ?? (cell.IsValid ? cell.Column : null);
            var item = exactRow ?? state.HitItem ?? (cell.IsValid ? cell.Item : null);
            if (column is not null && item is not null &&
                columns.Contains(column) && grid.Items.Contains(item) &&
                !ReferenceEquals(item, CollectionView.NewItemPlaceholder))
                SetClipboard(grid, CellValue(column, item));
            return;
        }

        IReadOnlyList<DataGridColumn> copyColumns = columns;
        IEnumerable<IReadOnlyList<string?>> rows;
        if (mode == "table")
        {
            if (grid.Items.Count > EngineeringClipboardText.MaxRows)
            {
                Inform(grid, "More than 3,000 visible rows. Use Export CSV for large tables.");
                return;
            }
            rows = EnumerateAll(grid, copyColumns);
        }
        else if (mode == "context-row")
        {
            if (exactRow is null || !grid.Items.Contains(exactRow))
                return;
            rows = new[] { (IReadOnlyList<string?>)copyColumns
                .Select(column => CellValue(column, exactRow)).ToArray() };
        }
        else
        {
            // The selection itself is WPF-owned; only reconstruct its sorted
            // viewport order at copy time. SelectedCells supports rectangular
            // Excel-style blocks. Row mode explicitly expands to full columns.
            var selected = new Dictionary<object, HashSet<DataGridColumn>>(
                ReferenceEqualityComparer.Instance);
            if (mode is "selection" or "selection-headers")
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
                if (grid.SelectedItems.Count > EngineeringClipboardText.MaxRows)
                {
                    Inform(grid, "More than 3,000 selected rows. Use Export CSV for large tables.");
                    return;
                }
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
                selected[cell.Item] = mode == "rows"
                    ? new HashSet<DataGridColumn>(columns)
                    : new HashSet<DataGridColumn> { cell.Column };
            }
            if (selected.Count == 0) return;
            if (mode is "selection" or "selection-headers")
            {
                var wanted = new HashSet<DataGridColumn>(
                    selected.Values.SelectMany(set => set));
                copyColumns = columns.Where(wanted.Contains).ToArray();
            }
            rows = EnumerateSelected(grid, copyColumns, selected);
        }

        var headers = copyColumns.Select(ColumnHeader).ToArray();
        // Standard Ctrl+C is Excel-like: only selected cells, no headers.
        // Named actions provide deliberate header-preserving table copies.
        if (!EngineeringClipboardText.TryBuild(headers, rows,
                includeHeaders: mode is "table" or "rows" or "selection-headers"
                    or "context-row",
                out var text, out var failure))
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
        list.PreviewMouseLeftButtonDown += (_, _) => state.HitRow = null;
        list.PreviewMouseRightButtonDown += (_, mouse) =>
        {
            state.HitRow = Ancestor<ListViewItem>(mouse.OriginalSource as DependencyObject)
                ?.DataContext as IedBrowserDetailRow;
        };
        var menu = new ContextMenu();
        menu.Items.Add(Item("Copy value", () =>
        {
            var row = state.HitRow is { } clicked && list.Items.Contains(clicked)
                ? clicked : list.SelectedItem as IedBrowserDetailRow;
            if (row is not null)
                SetClipboard(list, row.Value);
        }));
        menu.Items.Add(Item("Copy name and value", () =>
        {
            var row = state.HitRow is { } clicked && list.Items.Contains(clicked)
                ? clicked : list.SelectedItem as IedBrowserDetailRow;
            if (row is not null) CopyDetailRows(list, [row]);
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Copy visible properties", () => CopyDetailRows(
            list, list.Items.OfType<IedBrowserDetailRow>()
                .Where(row => row.Kind == "Property"))));
        // The Browser P0 contract deliberately forbids long provenance hover
        // strips. Reveal Owner attribution ONLY on an explicit context action.
        var ownerInfo = Item("Owner information…", () =>
        {
            var row = state.HitRow is { } clicked && list.Items.Contains(clicked)
                ? clicked : list.SelectedItem as IedBrowserDetailRow;
            if (row is { Kind: "Property", Name: "Owner" })
                Inform(list, row.ObservationTooltip);
        });
        ownerInfo.Visibility = Visibility.Collapsed;
        menu.Items.Add(ownerInfo);
        list.ContextMenuOpening += (_, _) =>
        {
            var row = state.HitRow is { } clicked && list.Items.Contains(clicked)
                ? clicked : list.SelectedItem as IedBrowserDetailRow;
            ownerInfo.Visibility = row is { Kind: "Property", Name: "Owner" }
                ? Visibility.Visible : Visibility.Collapsed;
        };
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
