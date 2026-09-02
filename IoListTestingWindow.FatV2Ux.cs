using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester;

public partial class IoListTestingWindow
{
    private DataGrid? _fatSignalsGrid;
    private Button? _removedSignalsButton;
    private ICollectionView? _fatSignalsView;
    private bool _fatV2UxInstalled;
    private readonly BooleanToVisibilityConverter _fatBooleanVisibility = new();

    private void InstallFatV2WorkspaceUx()
    {
        if (!_fatV2UxInstalled)
        {
            _fatSignalsGrid = FindVisualDescendant<DataGrid>(this);
            if (_fatSignalsGrid != null)
            {
                ConfigureFatV2Columns(_fatSignalsGrid);
                _fatSignalsGrid.PreviewMouseRightButtonDown += FatSignalsGrid_PreviewMouseRightButtonDown;
                _fatSignalsGrid.ContextMenu = BuildFatContextMenu();
            }

            UpdateFatWorkspaceLabels();
            InstallRemovedSignalsButton();
            _fatV2UxInstalled = true;
        }

        RefreshFatV2WorkspaceUx(refreshRows: true);
    }

    private void RefreshFatV2WorkspaceUx(bool refreshRows = false)
    {
        if (_fatSignalsGrid != null)
        {
            var view = CollectionViewSource.GetDefaultView(_fatSignalsGrid.ItemsSource);
            if (view != null && !ReferenceEquals(view, _fatSignalsView))
            {
                _fatSignalsView = view;
                view.Filter = item => item is IoTestPointPlan point && point.IsIncludedInFat;
                refreshRows = true;
            }
            if (refreshRows)
                view?.Refresh();
        }

        if (_removedSignalsButton != null)
        {
            _removedSignalsButton.Content = Project.RemovedSignalCount == 0
                ? "Removed Signals"
                : $"Removed Signals ({Project.RemovedSignalCount})";
            _removedSignalsButton.IsEnabled = CanEditPlan && Project.RemovedSignalCount > 0;
        }

        Raise(nameof(ProjectSummary));
        Raise(nameof(SelectedIedSummary));
        Raise(nameof(SelectedProgressText));
        Raise(nameof(SelectedEvidenceCount));
    }

    private void ConfigureFatV2Columns(DataGrid grid)
    {
        grid.Columns.Clear();

        var enabledFactory = new FrameworkElementFactory(typeof(CheckBox));
        enabledFactory.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(IoTestPointPlan.TestEnabled))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        enabledFactory.SetBinding(UIElement.IsEnabledProperty, new Binding("DataContext.CanEditPlan")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
        });
        enabledFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        enabledFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "TEST",
            Width = 48,
            CellTemplate = new DataTemplate { VisualTree = enabledFactory }
        });

        grid.Columns.Add(TextColumn("SIGNAL", nameof(IoTestPointPlan.SignalName), new DataGridLength(1.45, DataGridLengthUnitType.Star), 180, 300));
        grid.Columns.Add(TextColumn("IEC REFERENCE", nameof(IoTestPointPlan.ReportIecReference), new DataGridLength(1.8, DataGridLengthUnitType.Star), 220, 360, monospace: true));
        grid.Columns.Add(TextColumn("TYPE", nameof(IoTestPointPlan.SignalKind), 82));
        grid.Columns.Add(CreateLiveValueColumn());
        grid.Columns.Add(CreateValueColumn(FatValueSlot.Value1));
        grid.Columns.Add(CreateValueColumn(FatValueSlot.Value2));
        grid.Columns.Add(TextColumn("STATUS", nameof(IoTestPointPlan.FatStatusText), 112));
        grid.Columns.Add(TextColumn("RESULT", nameof(IoTestPointPlan.FatResultText), 96));
    }

    private static DataGridTextColumn TextColumn(
        string header,
        string path,
        double width,
        bool monospace = false)
        => TextColumn(header, path, new DataGridLength(width), width, width, monospace);

    private static DataGridTextColumn TextColumn(
        string header,
        string path,
        DataGridLength width,
        double minWidth,
        double maxWidth,
        bool monospace = false)
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 11.2));
        style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(68, 84, 107))));
        if (monospace)
            style.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily("Cascadia Mono, Consolas")));

        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path),
            Width = width,
            MinWidth = minWidth,
            MaxWidth = maxWidth,
            ElementStyle = style,
            IsReadOnly = true
        };
    }

    private static DataGridTemplateColumn CreateLiveValueColumn()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);

        var value = new FrameworkElementFactory(typeof(TextBlock));
        value.SetBinding(TextBlock.TextProperty, new Binding("Runtime.CurrentValue"));
        value.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        value.SetValue(TextBlock.FontSizeProperty, 12.0);
        value.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(30, 60, 100)));
        panel.AppendChild(value);

        var quality = new FrameworkElementFactory(typeof(TextBlock));
        quality.SetBinding(TextBlock.TextProperty, new Binding("Runtime.CurrentQuality"));
        quality.SetValue(TextBlock.FontSizeProperty, 9.5);
        quality.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(105, 121, 143)));
        panel.AppendChild(quality);

        return new DataGridTemplateColumn
        {
            Header = "LIVE VALUE",
            Width = 104,
            CellTemplate = new DataTemplate { VisualTree = panel }
        };
    }

    private DataGridTemplateColumn CreateValueColumn(FatValueSlot slot)
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
        panel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1));

        var value = new FrameworkElementFactory(typeof(TextBlock));
        value.SetBinding(TextBlock.TextProperty, new Binding(slot == FatValueSlot.Value1
            ? nameof(IoTestPointPlan.Value1Text)
            : nameof(IoTestPointPlan.Value2Text)));
        value.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        value.SetValue(TextBlock.FontSizeProperty, 11.4);
        value.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        value.SetBinding(FrameworkElement.ToolTipProperty, new Binding(slot == FatValueSlot.Value1
            ? nameof(IoTestPointPlan.Value1EvidenceToolTip)
            : nameof(IoTestPointPlan.Value2EvidenceToolTip)));
        panel.AppendChild(value);

        var timestamp = new FrameworkElementFactory(typeof(TextBlock));
        timestamp.SetBinding(TextBlock.TextProperty, new Binding(slot == FatValueSlot.Value1
            ? nameof(IoTestPointPlan.Value1RelayTimestampText)
            : nameof(IoTestPointPlan.Value2RelayTimestampText)));
        timestamp.SetValue(TextBlock.FontSizeProperty, 8.8);
        timestamp.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Cascadia Mono, Consolas"));
        timestamp.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(112, 126, 145)));
        panel.AppendChild(timestamp);

        var capture = new FrameworkElementFactory(typeof(Button));
        capture.SetValue(ContentControl.ContentProperty, "✓ Capture");
        capture.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 0));
        capture.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        capture.SetValue(Control.PaddingProperty, new Thickness(7, 2, 7, 2));
        capture.SetValue(Control.FontSizeProperty, 9.5);
        capture.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(IoTestPointPlan.IsOperatorSnapshot))
        {
            Converter = _fatBooleanVisibility
        });
        capture.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(IoTestPointPlan.CanCaptureOperatorSnapshot)));
        capture.AddHandler(Button.ClickEvent, slot == FatValueSlot.Value1
            ? new RoutedEventHandler(CaptureValue1_Click)
            : new RoutedEventHandler(CaptureValue2_Click));
        panel.AppendChild(capture);

        return new DataGridTemplateColumn
        {
            Header = slot == FatValueSlot.Value1 ? "VALUE 1" : "VALUE 2",
            Width = 142,
            CellTemplate = new DataTemplate { VisualTree = panel }
        };
    }

    private void CaptureValue1_Click(object sender, RoutedEventArgs e)
        => CaptureOperatorValue(sender, FatValueSlot.Value1);

    private void CaptureValue2_Click(object sender, RoutedEventArgs e)
        => CaptureOperatorValue(sender, FatValueSlot.Value2);

    private void CaptureOperatorValue(object sender, FatValueSlot slot)
    {
        var point = (sender as FrameworkElement)?.DataContext as IoTestPointPlan;
        var result = Session.CaptureOperatorSnapshot(point, slot);
        ShowActionResult(result, $"{(slot == FatValueSlot.Value1 ? "Value 1" : "Value 2")} capture failed");
        if (!result.Succeeded)
            return;

        Storage?.ScheduleSave();
        RaiseSelectedIedContextProperties();
        RefreshFatV2WorkspaceUx();
    }

    private ContextMenu BuildFatContextMenu()
    {
        var menu = new ContextMenu();
        var remove = new MenuItem { Header = "Remove from FAT" };
        remove.Click += RemoveSelectedFromFat_Click;
        menu.Items.Add(remove);
        return menu;
    }

    private void FatSignalsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_fatSignalsGrid == null)
            return;
        var row = FindVisualAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is IoTestPointPlan point)
        {
            _fatSignalsGrid.SelectedItem = point;
            row.IsSelected = true;
        }
    }

    private void RemoveSelectedFromFat_Click(object sender, RoutedEventArgs e)
    {
        if (_fatSignalsGrid?.SelectedItem is not IoTestPointPlan point)
            return;
        if (!CanEditPlan)
        {
            MessageBox.Show(
                this,
                "Stop the active FAT session or connection preparation before changing FAT scope.",
                "FAT scope is locked",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        point.RemoveFromFat();
        Storage?.ScheduleSave();
        RefreshFatV2WorkspaceUx(refreshRows: true);
        RaiseSelectedIedContextProperties();
    }

    private void InstallRemovedSignalsButton()
    {
        var engineering = FindVisualDescendants<Button>(this)
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Engineering", StringComparison.Ordinal));
        if (engineering == null || VisualTreeHelper.GetParent(engineering) is not Panel panel)
            return;

        _removedSignalsButton = new Button
        {
            Content = "Removed Signals",
            Padding = new Thickness(11, 7, 11, 7),
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Review and restore signals removed from the active FAT scope"
        };
        if (TryFindResource("SoftButton") is Style softButton)
            _removedSignalsButton.Style = softButton;
        _removedSignalsButton.Click += RemovedSignals_Click;

        var index = panel.Children.IndexOf(engineering);
        panel.Children.Insert(Math.Max(0, index), _removedSignalsButton);
    }

    private void RemovedSignals_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditPlan)
            return;

        var window = new RemovedFatSignalsWindow(Project) { Owner = this };
        if (window.ShowDialog() != true || window.RestoredCount == 0)
            return;

        Storage?.ScheduleSave();
        RefreshFatV2WorkspaceUx(refreshRows: true);
        RaiseSelectedIedContextProperties();
    }

    private void UpdateFatWorkspaceLabels()
    {
        foreach (var text in FindVisualDescendants<TextBlock>(this))
        {
            if (text.Text == "IO LIST FAT")
                text.Text = "IEC 61850 FAT";
            else if (text.Text == "Workbook scope · report-first acquisition · relay-timestamped evidence")
                text.Text = "Static DataSet scope · Value 1 / Value 2 · immutable evidence history";
            else if (text.Text == "Workbook devices")
                text.Text = "FAT source IEDs";
        }

        if (string.IsNullOrWhiteSpace(Storage?.SourceWorkbookPath))
        {
            var excel = FindVisualDescendants<Button>(this)
                .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Excel", StringComparison.Ordinal));
            if (excel != null)
                excel.Visibility = Visibility.Collapsed;
        }
    }

    private static T? FindVisualDescendant<T>(DependencyObject root) where T : DependencyObject
        => FindVisualDescendants<T>(root).FirstOrDefault();

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root == null)
            yield break;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
                yield return typed;
            foreach (var nested in FindVisualDescendants<T>(child))
                yield return nested;
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        var current = child;
        while (current != null)
        {
            if (current is T typed)
                return typed;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
