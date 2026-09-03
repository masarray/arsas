using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester;

public sealed class RemovedFatSignalsWindow : Window
{
    private readonly ObservableCollection<RemovedSignalRow> _rows;
    private readonly ICollectionView _view;
    private readonly TextBox _searchBox;
    private readonly TextBlock _summary;

    public RemovedFatSignalsWindow(IoTestProject project)
        : this(project, canEditPoint: null)
    {
    }

    public RemovedFatSignalsWindow(
        IoTestProject project,
        Func<IoTestPointPlan, bool>? canEditPoint)
    {
        ArgumentNullException.ThrowIfNull(project);
        Title = "Removed Signals - ARSAS FAT";
        Width = 1040;
        Height = 650;
        MinWidth = 780;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(246, 249, 253));
        FontFamily = new FontFamily("Aptos, Segoe UI Variable Text, Segoe UI, Calibri");

        _rows = new ObservableCollection<RemovedSignalRow>(
            project.Ieds
                .SelectMany(ied => ied.TestPoints)
                .Where(point => !point.IsIncludedInFat)
                .OrderBy(point => point.IedName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(point => point.DataSetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(point => point.SourceRow)
                .Select(point => new RemovedSignalRow(
                    point,
                    canEditPoint?.Invoke(point) ?? true)));
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterRow;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headerText = new StackPanel();
        headerText.Children.Add(new TextBlock
        {
            Text = "REMOVED SIGNALS",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(55, 100, 190))
        });
        headerText.Children.Add(new TextBlock
        {
            Text = "Restore signals to the active FAT scope",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 43, 61)),
            Margin = new Thickness(0, 3, 0, 0)
        });
        headerText.Children.Add(new TextBlock
        {
            Text = "Remove and restore never delete source identity or Value 1 / Value 2 evidence. Rows owned by an active IED workflow stay locked independently.",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(99, 116, 139)),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(headerText);
        _summary = new TextBlock
        {
            Text = SummaryText(),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(74, 91, 115)),
            FontSize = 11.5,
            Margin = new Thickness(16, 0, 0, 0)
        };
        Grid.SetColumn(_summary, 1);
        header.Children.Add(_summary);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var searchPanel = new Grid();
        searchPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _searchBox = new TextBox
        {
            MinWidth = 260,
            Height = 34,
            Padding = new Thickness(10, 6, 10, 6),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Search by IED, signal, DataSet, or IEC reference"
        };
        _searchBox.TextChanged += (_, _) => _view.Refresh();
        searchPanel.Children.Add(_searchBox);
        var searchHint = new TextBlock
        {
            Text = "Search removed signals",
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(120, 134, 153)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        Grid.SetColumn(searchHint, 1);
        searchPanel.Children.Add(searchHint);
        Grid.SetRow(searchPanel, 2);
        root.Children.Add(searchPanel);

        var grid = new DataGrid
        {
            ItemsSource = _view,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            RowHeaderWidth = 0,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 230, 240)),
            BorderThickness = new Thickness(1),
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(233, 238, 245)),
            EnableRowVirtualization = true
        };

        var selectionFactory = new FrameworkElementFactory(typeof(CheckBox));
        selectionFactory.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(RemovedSignalRow.IsSelected))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        selectionFactory.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(RemovedSignalRow.CanEdit)));
        selectionFactory.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(RemovedSignalRow.EditToolTip)));
        selectionFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        selectionFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "SELECT",
            Width = 64,
            CellTemplate = new DataTemplate { VisualTree = selectionFactory }
        });
        grid.Columns.Add(TextColumn("IED", nameof(RemovedSignalRow.IedName), 130));
        grid.Columns.Add(TextColumn("SIGNAL", nameof(RemovedSignalRow.SignalName), new DataGridLength(1.1, DataGridLengthUnitType.Star), 160));
        grid.Columns.Add(TextColumn("DATASET", nameof(RemovedSignalRow.DataSetName), new DataGridLength(1.0, DataGridLengthUnitType.Star), 150));
        grid.Columns.Add(TextColumn("IEC REFERENCE", nameof(RemovedSignalRow.Reference), new DataGridLength(1.5, DataGridLengthUnitType.Star), 230));
        grid.Columns.Add(TextColumn("VALUE 1", nameof(RemovedSignalRow.Value1), 105));
        grid.Columns.Add(TextColumn("VALUE 2", nameof(RemovedSignalRow.Value2), 105));
        Grid.SetRow(grid, 4);
        root.Children.Add(grid);

        var actions = new Grid();
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var selectAll = ActionButton("Select All", false);
        selectAll.Click += (_, _) => SetVisibleSelection(true);
        actions.Children.Add(selectAll);

        var deselect = ActionButton("Deselect All", false);
        deselect.Margin = new Thickness(8, 0, 0, 0);
        deselect.Click += (_, _) => SetVisibleSelection(false);
        Grid.SetColumn(deselect, 1);
        actions.Children.Add(deselect);

        var restore = ActionButton("Restore Selected", true);
        restore.Click += RestoreSelected_Click;
        Grid.SetColumn(restore, 3);
        actions.Children.Add(restore);

        var cancel = ActionButton("Cancel", false);
        cancel.Margin = new Thickness(8, 0, 0, 0);
        cancel.Click += (_, _) => Close();
        Grid.SetColumn(cancel, 4);
        actions.Children.Add(cancel);

        Grid.SetRow(actions, 6);
        root.Children.Add(actions);
        Content = root;
    }

    public int RestoredCount { get; private set; }

    private bool FilterRow(object item)
    {
        if (item is not RemovedSignalRow row)
            return false;
        var term = _searchBox?.Text?.Trim() ?? string.Empty;
        if (term.Length == 0)
            return true;
        return row.IedName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               row.SignalName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               row.DataSetName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
               row.Reference.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private void SetVisibleSelection(bool selected)
    {
        foreach (var item in _view.Cast<RemovedSignalRow>().Where(row => row.CanEdit))
            item.IsSelected = selected;
        _summary.Text = SummaryText();
    }

    private void RestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _rows.Where(row => row.CanEdit && row.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show(
                this,
                "Select at least one editable removed signal to restore. Rows owned by an active IED connection/session remain locked.",
                "No editable signal selected",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        foreach (var row in selected)
            row.Point.RestoreToFat();
        RestoredCount = selected.Length;
        DialogResult = true;
        Close();
    }

    private string SummaryText()
    {
        var locked = _rows.Count(row => !row.CanEdit);
        var selected = _rows.Count(row => row.CanEdit && row.IsSelected);
        return locked == 0
            ? $"{_rows.Count} removed · {selected} selected"
            : $"{_rows.Count} removed · {selected} selected · {locked} active-IED locked";
    }

    private static Button ActionButton(string content, bool primary)
        => new()
        {
            Content = content,
            Height = 34,
            MinWidth = primary ? 132 : 98,
            Padding = new Thickness(12, 5, 12, 5),
            FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal
        };

    private static DataGridTextColumn TextColumn(string header, string path, double width)
        => TextColumn(header, path, new DataGridLength(width), width);

    private static DataGridTextColumn TextColumn(string header, string path, DataGridLength width, double minWidth)
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 10.8));
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path),
            Width = width,
            MinWidth = minWidth,
            ElementStyle = style,
            IsReadOnly = true
        };
    }

    private sealed class RemovedSignalRow : INotifyPropertyChanged
    {
        private bool _isSelected;

        public RemovedSignalRow(IoTestPointPlan point, bool canEdit)
        {
            Point = point;
            CanEdit = canEdit;
        }

        public IoTestPointPlan Point { get; }
        public bool CanEdit { get; }
        public string EditToolTip => CanEdit
            ? "Select this signal to restore it to FAT"
            : "This IED's FAT scope is locked by its active connection preparation or evidence session";
        public string IedName => Point.IedName;
        public string SignalName => Point.SignalName;
        public string DataSetName => Point.DataSetName;
        public string Reference => Point.ReportIecReference;
        public string Value1 => Point.Value1Text;
        public string Value2 => Point.Value2Text;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (!CanEdit && value)
                    return;
                if (_isSelected == value)
                    return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
