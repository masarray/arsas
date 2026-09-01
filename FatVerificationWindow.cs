using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Additive FAT v2 workspace. It deliberately does not reuse the legacy ON/OFF grid because
/// analog and fallback DataSet members must retain generic Value 1 / Value 2 semantics.
/// </summary>
public sealed class FatVerificationWindow : Window
{
    private readonly FatSclWorkspaceLaunchResult _launch;
    private readonly TextBox _searchBox = new();
    private readonly TextBlock _summary = new();
    private readonly Button _removedButton = new();
    private readonly DataGrid _grid = new();

    public FatVerificationWindow(FatSclWorkspaceLaunchResult launch)
    {
        _launch = launch ?? throw new ArgumentNullException(nameof(launch));
        Title = "ARSAS FAT v2 — DataSet Verification";
        Width = 1320;
        Height = 780;
        MinWidth = 980;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#F4F7FB");
        Content = BuildLayout();
        RefreshRows();
    }

    private UIElement BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel();
        title.Children.Add(new TextBlock
        {
            Text = "FAT v2 · STATIC DATASET VERIFICATION",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#2563EB")
        });
        title.Children.Add(new TextBlock
        {
            Text = "Value 1 / Value 2 evidence workspace",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#172033"),
            Margin = new Thickness(0, 3, 0, 3)
        });
        title.Children.Add(_summary);
        Grid.SetColumn(title, 0);
        heading.Children.Add(title);

        var sourceBadge = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(10),
            Background = Brushes.White,
            BorderBrush = Brush("#D9E2EF"),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = $"{_launch.SourceFiles.Count} SCL source(s) · {_launch.SourceSetSha256[..12]}",
                FontSize = 12,
                Foreground = Brush("#4B5D73")
            }
        };
        Grid.SetColumn(sourceBadge, 1);
        heading.Children.Add(sourceBadge);
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _searchBox.Height = 34;
        _searchBox.MaxWidth = 560;
        _searchBox.HorizontalAlignment = HorizontalAlignment.Left;
        _searchBox.VerticalContentAlignment = VerticalAlignment.Center;
        _searchBox.Padding = new Thickness(10, 0, 10, 0);
        _searchBox.ToolTip = "Search IED, DataSet, signal, FC, or type";
        _searchBox.TextChanged += (_, _) => RefreshRows();
        Grid.SetColumn(_searchBox, 0);
        toolbar.Children.Add(_searchBox);

        _removedButton.MinWidth = 150;
        _removedButton.Height = 34;
        _removedButton.Padding = new Thickness(12, 0, 12, 0);
        _removedButton.Click += RemovedSignals_Click;
        Grid.SetColumn(_removedButton, 1);
        toolbar.Children.Add(_removedButton);
        Grid.SetRow(toolbar, 1);
        root.Children.Add(toolbar);

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.CanUserAddRows = false;
        _grid.CanUserDeleteRows = false;
        _grid.SelectionMode = DataGridSelectionMode.Single;
        _grid.SelectionUnit = DataGridSelectionUnit.FullRow;
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        _grid.BorderBrush = Brush("#D9E2EF");
        _grid.BorderThickness = new Thickness(1);
        _grid.Background = Brushes.White;
        _grid.RowBackground = Brushes.White;
        _grid.AlternatingRowBackground = Brush("#FAFCFF");
        _grid.RowHeight = 34;
        _grid.ColumnHeaderHeight = 36;
        _grid.PreviewMouseRightButtonDown += Grid_PreviewMouseRightButtonDown;
        _grid.Columns.Add(TextColumn("IED", nameof(FatVerificationSignal.IedName), 125));
        _grid.Columns.Add(TextColumn("DataSet", nameof(FatVerificationSignal.DataSetReference), 210));
        _grid.Columns.Add(TextColumn("#", nameof(FatVerificationSignal.DataSetMemberIndex), 45));
        _grid.Columns.Add(TextColumn("Signal", nameof(FatVerificationSignal.StaticMemberReference), 315));
        _grid.Columns.Add(TextColumn("FC", nameof(FatVerificationSignal.FunctionalConstraint), 55));
        _grid.Columns.Add(TextColumn("Kind", nameof(FatVerificationSignal.SignalKind), 85));
        _grid.Columns.Add(TextColumn("Value 1", "Value1Evidence.RawValue", 150));
        _grid.Columns.Add(TextColumn("Value 2", "Value2Evidence.RawValue", 150));
        var remove = new MenuItem { Header = "Remove from FAT" };
        remove.Click += RemoveSelected_Click;
        _grid.ContextMenu = new ContextMenu { Items = { remove } };

        Grid.SetRow(_grid, 2);
        root.Children.Add(_grid);
        return root;
    }

    private static DataGridTextColumn TextColumn(string header, string path, double width)
        => new()
        {
            Header = header,
            Binding = new Binding(path),
            Width = new DataGridLength(width)
        };

    private void RefreshRows()
    {
        var query = _searchBox.Text.Trim();
        IEnumerable<FatVerificationSignal> rows = _launch.Project.IncludedSignals;
        if (query.Length > 0)
        {
            rows = rows.Where(signal =>
                Contains(signal.IedName, query) ||
                Contains(signal.AccessPointName, query) ||
                Contains(signal.DataSetReference, query) ||
                Contains(signal.StaticMemberReference, query) ||
                Contains(signal.RuntimeReference, query) ||
                Contains(signal.FunctionalConstraint, query) ||
                Contains(signal.DataType, query) ||
                Contains(signal.SignalKind.ToString(), query));
        }

        _grid.ItemsSource = rows.ToArray();
        var all = _launch.Project.Signals;
        _summary.Text = $"{_launch.Project.IncludedSignals.Count} included · {_launch.Project.RemovedSignals.Count} removed · " +
                        $"{all.Count(signal => signal.SignalKind == FatSignalKind.Discrete)} digital · " +
                        $"{all.Count(signal => signal.SignalKind == FatSignalKind.Analog)} analog · " +
                        $"{all.Count(signal => signal.SignalKind == FatSignalKind.Other)} other";
        _summary.FontSize = 12.5;
        _summary.Foreground = Brush("#64748B");
        _removedButton.Content = $"Removed Signals ({_launch.Project.RemovedSignals.Count})";
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_grid.SelectedItem is not FatVerificationSignal signal)
            return;
        _launch.Project.RemoveSignal(signal.SignalId);
        RefreshRows();
    }

    private void RemovedSignals_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new FatRemovedSignalsWindow(_launch.Project) { Owner = this };
        dialog.ShowDialog();
        RefreshRows();
    }

    private void Grid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row != null)
            _grid.SelectedItem = row.Item;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T result)
                return result;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static bool Contains(string? value, string query)
        => (value ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase);

    private static Brush Brush(string value)
        => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
}

internal sealed class FatRemovedSignalsWindow : Window
{
    private sealed class RemovedSignalRow
    {
        public required FatVerificationSignal Signal { get; init; }
        public bool IsSelected { get; set; }
        public string IedName => Signal.IedName;
        public string DataSetReference => Signal.DataSetReference;
        public int DataSetMemberIndex => Signal.DataSetMemberIndex;
        public string StaticMemberReference => Signal.StaticMemberReference;
        public FatSignalKind SignalKind => Signal.SignalKind;
        public string SignalId => Signal.SignalId;
    }

    private readonly FatVerificationProject _project;
    private readonly List<RemovedSignalRow> _rows;
    private readonly TextBox _search = new();
    private readonly DataGrid _grid = new();

    public FatRemovedSignalsWindow(FatVerificationProject project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _rows = project.RemovedSignals.Select(signal => new RemovedSignalRow { Signal = signal }).ToList();
        Title = "Removed Signals";
        Width = 940;
        Height = 560;
        MinWidth = 760;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildLayout();
        RefreshRows();
    }

    private UIElement BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        _search.Width = 380;
        _search.Height = 32;
        _search.VerticalContentAlignment = VerticalAlignment.Center;
        _search.Padding = new Thickness(8, 0, 8, 0);
        _search.ToolTip = "Search removed signals";
        _search.TextChanged += (_, _) => RefreshRows();
        DockPanel.SetDock(_search, Dock.Left);
        top.Children.Add(_search);
        Grid.SetRow(top, 0);
        root.Children.Add(top);

        _grid.AutoGenerateColumns = false;
        _grid.CanUserAddRows = false;
        _grid.CanUserDeleteRows = false;
        _grid.SelectionMode = DataGridSelectionMode.Single;
        _grid.SelectionUnit = DataGridSelectionUnit.FullRow;
        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "✓",
            Binding = new Binding(nameof(RemovedSignalRow.IsSelected)) { Mode = BindingMode.TwoWay },
            Width = 42
        });
        _grid.Columns.Add(Column("IED", nameof(RemovedSignalRow.IedName), 120));
        _grid.Columns.Add(Column("DataSet", nameof(RemovedSignalRow.DataSetReference), 210));
        _grid.Columns.Add(Column("#", nameof(RemovedSignalRow.DataSetMemberIndex), 45));
        _grid.Columns.Add(Column("Signal", nameof(RemovedSignalRow.StaticMemberReference), 360));
        _grid.Columns.Add(Column("Kind", nameof(RemovedSignalRow.SignalKind), 80));
        var restoreOne = new MenuItem { Header = "Restore to FAT" };
        restoreOne.Click += RestoreOne_Click;
        _grid.ContextMenu = new ContextMenu { Items = { restoreOne } };
        Grid.SetRow(_grid, 1);
        root.Children.Add(_grid);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        actions.Children.Add(Button("Select All", (_, _) => SetVisibleSelection(true)));
        actions.Children.Add(Button("Deselect All", (_, _) => SetVisibleSelection(false)));
        actions.Children.Add(Button("Restore Selected", RestoreSelected_Click, true));
        actions.Children.Add(Button("Cancel", (_, _) => Close()));
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);
        return root;
    }

    private static DataGridTextColumn Column(string header, string path, double width)
        => new() { Header = header, Binding = new Binding(path), Width = new DataGridLength(width) };

    private static Button Button(string text, RoutedEventHandler click, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 100,
            Height = 32,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(8, 0, 0, 0),
            FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal
        };
        button.Click += click;
        return button;
    }

    private IEnumerable<RemovedSignalRow> VisibleRows()
    {
        var query = _search.Text.Trim();
        return query.Length == 0
            ? _rows
            : _rows.Where(row =>
                Contains(row.IedName, query) ||
                Contains(row.DataSetReference, query) ||
                Contains(row.StaticMemberReference, query) ||
                Contains(row.SignalKind.ToString(), query));
    }

    private void RefreshRows() => _grid.ItemsSource = VisibleRows().ToArray();

    private void SetVisibleSelection(bool selected)
    {
        foreach (var row in VisibleRows())
            row.IsSelected = selected;
        _grid.Items.Refresh();
    }

    private void RestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        var ids = _rows.Where(row => row.IsSelected).Select(row => row.SignalId).ToArray();
        if (ids.Length == 0)
            return;
        _project.RestoreSignals(ids);
        DialogResult = true;
    }

    private void RestoreOne_Click(object sender, RoutedEventArgs e)
    {
        if (_grid.SelectedItem is not RemovedSignalRow row)
            return;
        _project.RestoreSignal(row.SignalId);
        _rows.Remove(row);
        RefreshRows();
    }

    private static bool Contains(string? value, string query)
        => (value ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase);
}
