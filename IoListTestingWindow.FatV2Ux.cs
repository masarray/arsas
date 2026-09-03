using System.Globalization;
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
    private IoTestPointPlan? _fatContextPoint;
    private bool _fatV2UxInstalled;
    private readonly BooleanToVisibilityConverter _fatBooleanVisibility = new();
    private readonly IoFatSelectedIedPlanEditabilityConverter _fatPlanEditabilityConverter = new();

    // P0.3/P2: plan ownership is per IED. Connection preparation and evidence sessions
    // lock only the owning IED; sibling IEDs remain independently editable.
    public bool SelectedCanEditPlan => SelectedIed is not null && CanEditIedPlan(SelectedIed);

    private bool CanEditIedPlan(IoTestIedPlan ied)
        => !ied.IsPreparing && !Session.IsIedSessionActive(ied);

    private bool CanEditPointPlan(IoTestPointPlan point)
    {
        var owner = Project.Ieds.FirstOrDefault(ied => ied.TestPoints.Contains(point));
        return owner is not null && CanEditIedPlan(owner);
    }

    private bool CanRestoreAnyRemovedSignal => Project.Ieds.Any(ied =>
        CanEditIedPlan(ied) && ied.TestPoints.Any(point => !point.IsIncludedInFat));

    private void InstallFatV2WorkspaceUx()
    {
        if (!_fatV2UxInstalled)
        {
            _fatSignalsGrid = FindVisualDescendant<DataGrid>(this);
            if (_fatSignalsGrid != null)
            {
                // P3 desktop selection contract: Ctrl+Click selects disjoint rows,
                // Shift+Click selects ranges, and Recapture consumes SelectedItems.
                _fatSignalsGrid.SelectionMode = DataGridSelectionMode.Extended;
                _fatSignalsGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
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
        // Selected/session/preparation notifications all converge here through the selected
        // IED context refresh. Re-evaluate the per-IED edit contract without touching rows.
        Raise(nameof(SelectedCanEditPlan));
        Raise(nameof(CanRestoreAnyRemovedSignal));

        if (_fatSignalsGrid != null)
        {
            var view = CollectionViewSource.GetDefaultView(_fatSignalsGrid.ItemsSource);
            if (view != null && !ReferenceEquals(view, _fatSignalsView))
            {
                _fatSignalsView = view;
                // FAT projects the shared Engineering workspace selection. TEST is a FAT-only
                // evidence-scope toggle and must never hide a shared signal. Remove/Restore is
                // a second, orthogonal FAT-only disposition overlay.
                view.Filter = item => item is IoTestPointPlan point &&
                                             point.IsIncludedInFat &&
                                             point.WorkspaceSelected;
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
            _removedSignalsButton.IsEnabled = CanRestoreAnyRemovedSignal && Project.RemovedSignalCount > 0;
        }

        Raise(nameof(ProjectSummary));
        Raise(nameof(SelectedIedSummary));
        Raise(nameof(SelectedProgressText));
        Raise(nameof(SelectedEvidenceCount));
    }

    private void ConfigureFatV2Columns(DataGrid grid)
    {
        grid.SelectionMode = DataGridSelectionMode.Extended;
        grid.SelectionUnit = DataGridSelectionUnit.FullRow;
        grid.Columns.Clear();

        var enabledFactory = new FrameworkElementFactory(typeof(CheckBox));
        enabledFactory.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(IoTestPointPlan.TestEnabled))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });

        // Bind directly to selected-IED/session/preparation sources so the active IED locks
        // synchronously when Session.Start or IsPreparing changes. This avoids a dispatcher
        // timing window where a stale global CanEditPlan value could accept one more click.
        var editability = new MultiBinding { Converter = _fatPlanEditabilityConverter };
        editability.Bindings.Add(new Binding("DataContext.SelectedIed")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
        });
        editability.Bindings.Add(new Binding("DataContext.Session.ActiveIed")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
        });
        editability.Bindings.Add(new Binding("DataContext.Session.IsSessionActive")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
        });
        editability.Bindings.Add(new Binding("DataContext.SelectedIed.IsPreparing")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
        });
        enabledFactory.SetBinding(UIElement.IsEnabledProperty, editability);
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

        var recapture = new MenuItem { Header = "Recapture" };
        var value1 = new MenuItem { Header = "Value 1" };
        value1.Click += RecaptureValue1_Click;
        var value2 = new MenuItem { Header = "Value 2" };
        value2.Click += RecaptureValue2_Click;
        var both = new MenuItem
        {
            Header = "Value 1 & Value 2",
            ToolTip = "Stage Value 1 now, change the test condition, then use Recapture → Value 2 to commit the new pair"
        };
        both.Click += BeginPairRecapture_Click;
        var cancelPair = new MenuItem { Header = "Cancel staged Value 1 & Value 2" };
        cancelPair.Click += CancelPairRecapture_Click;
        recapture.Items.Add(value1);
        recapture.Items.Add(value2);
        recapture.Items.Add(both);
        recapture.Items.Add(new Separator());
        recapture.Items.Add(cancelPair);
        menu.Items.Add(recapture);
        menu.Items.Add(new Separator());

        // Remove remains a deliberate one-row disposition action. Multi-selection is for
        // Recapture in P3; right-click remembers the exact context row so preserving a
        // broader selection never removes an arbitrary anchor row.
        var remove = new MenuItem { Header = "Remove from FAT" };
        remove.Click += RemoveSelectedFromFat_Click;
        menu.Items.Add(remove);
        menu.Opened += (_, _) =>
        {
            var count = SelectedFatRows().Count;
            recapture.Header = count <= 1 ? "Recapture" : $"Recapture ({count} selected)";
        };
        return menu;
    }

    private IReadOnlyList<IoTestPointPlan> SelectedFatRows()
    {
        if (_fatSignalsGrid == null)
            return Array.Empty<IoTestPointPlan>();
        return _fatSignalsGrid.SelectedItems
            .OfType<IoTestPointPlan>()
            .Distinct()
            .ToArray();
    }

    private void RecaptureValue1_Click(object sender, RoutedEventArgs e)
        => ApplyBulkRecapture(Session.RecaptureValues(SelectedFatRows(), FatValueSlot.Value1), "Value 1 Recapture failed");

    private void RecaptureValue2_Click(object sender, RoutedEventArgs e)
        => ApplyBulkRecapture(Session.RecaptureValues(SelectedFatRows(), FatValueSlot.Value2), "Value 2 Recapture failed");

    private void BeginPairRecapture_Click(object sender, RoutedEventArgs e)
        => ApplyBulkRecapture(Session.BeginPairRecapture(SelectedFatRows()), "Value 1 & Value 2 Recapture could not be staged");

    private void CancelPairRecapture_Click(object sender, RoutedEventArgs e)
        => ApplyBulkRecapture(Session.CancelPairRecapture(SelectedFatRows()), "Staged Value 1 & Value 2 Recapture could not be cancelled");

    private void ApplyBulkRecapture(IoTestSessionActionResult result, string failureTitle)
    {
        if (!result.Succeeded)
        {
            ShowActionResult(result, failureTitle);
            return;
        }

        // Success feedback stays compact in the selected IED footer through Session.StatusText.
        // Never display one modal popup per row for a batch operation.
        Storage?.ScheduleSave();
        RaiseSelectedIedContextProperties();
        RefreshFatV2WorkspaceUx();
    }

    private void FatSignalsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_fatSignalsGrid == null)
            return;
        var row = FindVisualAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not IoTestPointPlan point)
            return;

        _fatContextPoint = point;
        if (row.IsSelected)
            return;

        // Desktop convention: right-clicking outside the current set makes that row the
        // sole target. Right-clicking inside an existing Ctrl/Shift selection preserves it.
        _fatSignalsGrid.SelectedItems.Clear();
        row.IsSelected = true;
        _fatSignalsGrid.SelectedItem = point;
    }

    private void RemoveSelectedFromFat_Click(object sender, RoutedEventArgs e)
    {
        var point = _fatContextPoint ?? _fatSignalsGrid?.SelectedItem as IoTestPointPlan;
        if (point == null)
            return;
        if (!CanEditPointPlan(point))
        {
            MessageBox.Show(
                this,
                "This IED's FAT scope is locked while its connection preparation or evidence session is active. Other IEDs remain editable.",
                "IED FAT scope is locked",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        point.RemoveFromFat();
        _fatContextPoint = null;
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
        if (!CanRestoreAnyRemovedSignal)
        {
            MessageBox.Show(
                this,
                "Removed signals currently belong only to an IED whose connection preparation or FAT evidence session owns an immutable scope.",
                "Removed Signals are locked",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var window = new RemovedFatSignalsWindow(Project, CanEditPointPlan) { Owner = this };
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
                text.Text = "Shared SCL scope · Value 1 / Value 2 · immutable evidence history";
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

public sealed class IoFatSelectedIedPlanEditabilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 4 || values[0] is not IoTestIedPlan selectedIed)
            return false;

        var activeIed = values[1] as IoTestIedPlan;
        var sessionActive = values[2] is bool active && active;
        var selectedPreparing = values[3] is bool preparing && preparing;
        return !selectedPreparing && (!sessionActive || !ReferenceEquals(activeIed, selectedIed));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => targetTypes.Select(_ => Binding.DoNothing).ToArray();
}
