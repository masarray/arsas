using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

/// <summary>
/// Native continuous FAT workspace layered on the persistent P0/P1 Engineering shell.
///
/// Authority boundary:
/// - IED Explorer owns SelectedDevice, signal engineering, live value and acquisition.
/// - the shared Command Dock remains the only command UI/runtime owner.
/// - this workspace owns only FAT captures, result/history and per-IED persistence.
///
/// The legacy IoListTestingWindow remains untouched as a compatibility fallback while
/// report/evidence parity is migrated incrementally.
/// </summary>
public partial class MainWindow
{
    private const int NativeFatWorkspaceIndex = 6;

    private readonly NativeFatStateStore _nativeFatStore = new();
    private readonly ObservableCollection<NativeFatSignalRow> _nativeFatRows = new();
    private readonly Dictionary<string, NativeFatDeviceState> _nativeFatStateCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _nativeFatSaveGate = new(1, 1);

    private DispatcherTimer? _nativeFatInstallRetry;
    private DispatcherTimer? _nativeFatReconcileTimer;
    private DispatcherTimer? _nativeFatSaveTimer;
    private CancellationTokenSource? _nativeFatLoadCts;
    private TabItem? _nativeFatTab;
    private Button? _nativeFatNavButton;
    private DataGrid? _nativeFatGrid;
    private DataGrid? _nativeFatPreviewGrid;
    private ICollectionView? _nativeFatView;
    private TextBox? _nativeFatSearchBox;
    private CheckBox? _nativeFatShowHistoricalCheck;
    private Border? _nativeFatPreviewPane;
    private ColumnDefinition? _nativeFatPreviewGapColumn;
    private ColumnDefinition? _nativeFatPreviewColumn;
    private TextBlock? _nativeFatContextText;
    private TextBlock? _nativeFatStatusText;
    private TextBlock? _nativeFatPreviewDeviceText;
    private TextBlock? _nativeFatPreviewSummaryText;
    private TextBlock? _nativeFatPersistenceText;
    private Iec61850MonitorDevice? _nativeFatObservedDevice;
    private NativeFatDeviceState? _nativeFatCurrentState;
    private bool _nativeFatInstalled;
    private bool _nativeFatLoading;
    private bool _nativeFatPreviewVisible;

    [ModuleInitializer]
    internal static void RegisterNativeFatWorkspace()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(NativeFatWorkspace_MainWindowLoaded),
            handledEventsToo: true);
    }

    private static void NativeFatWorkspace_MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._nativeFatInstalled)
            return;

        // P0 moves MainTabs/Explorer/Command Dock at ContextIdle. Install FAT after that
        // re-parenting has settled so this stacked feature never races the stable P0 shell.
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(window.TryInstallNativeFatWorkspace));
    }

    private void TryInstallNativeFatWorkspace()
    {
        if (_nativeFatInstalled || !IsLoaded)
            return;

        if (_persistentWorkbench == null || MainTabs.Items.Count < 6)
        {
            _nativeFatInstallRetry ??= new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromMilliseconds(140)
            };
            _nativeFatInstallRetry.Tick -= NativeFatInstallRetry_Tick;
            _nativeFatInstallRetry.Tick += NativeFatInstallRetry_Tick;
            _nativeFatInstallRetry.Start();
            return;
        }

        _nativeFatInstallRetry?.Stop();
        _nativeFatInstalled = true;

        _nativeFatTab = new TabItem
        {
            Header = "FAT",
            Content = BuildNativeFatWorkspaceContent()
        };
        MainTabs.Items.Add(_nativeFatTab);

        // The command dock is deliberately shared, not cloned. FAT is command-centric,
        // therefore start with the same expanded behavior as Explorer/Event Log.
        _persistentWorkbench.DockExpandedByWorkspace[NativeFatWorkspaceIndex] = true;

        InstallNativeFatNavigationButton();
        InstallNativeFatTimers();
        AttachNativeFatObservedDevice(SelectedDevice);

        PropertyChanged += NativeFat_MainWindowPropertyChanged;
        MainTabs.SelectionChanged += NativeFat_MainTabsSelectionChanged;
        SizeChanged += NativeFat_MainWindowSizeChanged;
        Closed += NativeFat_MainWindowClosed;

        QueueNativeFatNavigationGeometry();
        UpdateNativeFatSummary();
    }

    private void NativeFatInstallRetry_Tick(object? sender, EventArgs e)
    {
        _nativeFatInstallRetry?.Stop();
        TryInstallNativeFatWorkspace();
    }

    private UIElement BuildNativeFatWorkspaceContent()
    {
        var root = new Grid { Margin = new Thickness(0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = BuildNativeFatHeader();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var toolbar = BuildNativeFatToolbar();
        Grid.SetRow(toolbar, 2);
        root.Children.Add(toolbar);

        var body = BuildNativeFatBody();
        Grid.SetRow(body, 4);
        root.Children.Add(body);
        return root;
    }

    private Border BuildNativeFatHeader()
    {
        var border = new Border
        {
            Padding = new Thickness(14, 10, 14, 10),
            CornerRadius = new CornerRadius(12),
            Background = ResourceBrush("SurfaceElevated", Colors.White),
            BorderBrush = ResourceBrush("Line", Color.FromRgb(0xDD, 0xE5, 0xF0)),
            BorderThickness = new Thickness(1)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = "FAT · Continuous Testing",
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("Ink", Color.FromRgb(0x20, 0x30, 0x4A))
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Explorer signal authority · live values stay shared · captures autosave per IED · removed signals keep their history",
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 10.8,
            Foreground = ResourceBrush("Muted", Color.FromRgb(0x66, 0x75, 0x8B)),
            TextWrapping = TextWrapping.Wrap
        });
        grid.Children.Add(titleStack);

        _nativeFatContextText = new TextBlock
        {
            Text = "IED · NONE",
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("Accent", Color.FromRgb(0x25, 0x63, 0xEB)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0)
        };
        Grid.SetColumn(_nativeFatContextText, 1);
        grid.Children.Add(_nativeFatContextText);

        border.Child = grid;
        return border;
    }

    private Border BuildNativeFatToolbar()
    {
        var border = new Border
        {
            Padding = new Thickness(9, 7, 9, 7),
            CornerRadius = new CornerRadius(11),
            Background = ResourceBrush("Surface", Colors.White),
            BorderBrush = ResourceBrush("Line", Color.FromRgb(0xDD, 0xE5, 0xF0)),
            BorderThickness = new Thickness(1)
        };

        var wrap = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        _nativeFatSearchBox = new TextBox
        {
            Width = 210,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(8, 4, 8, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Search Signal, IEC reference, type, value, result or history state"
        };
        _nativeFatSearchBox.TextChanged += NativeFatSearchBox_TextChanged;
        wrap.Children.Add(_nativeFatSearchBox);

        wrap.Children.Add(CreateNativeFatButton("Refresh", NativeFatRefresh_Click, "Reconcile the saved FAT state with the current Explorer signal scope."));
        wrap.Children.Add(CreateNativeFatButton("Capture V1", NativeFatCapture1_Click, "Capture the current Explorer live value into Value 1 for selected FAT rows."));
        wrap.Children.Add(CreateNativeFatButton("Capture V2", NativeFatCapture2_Click, "Capture the current Explorer live value into Value 2 for selected FAT rows."));
        wrap.Children.Add(CreateNativeFatButton("PASS", NativeFatPass_Click, "Mark selected FAT rows PASS; previous results remain in history."));
        wrap.Children.Add(CreateNativeFatButton("REVIEW", NativeFatReview_Click, "Mark selected FAT rows REVIEW; previous results remain in history."));
        wrap.Children.Add(CreateNativeFatButton("FAIL", NativeFatFail_Click, "Mark selected FAT rows FAIL; previous results remain in history."));
        wrap.Children.Add(CreateNativeFatButton("Reset current", NativeFatReset_Click, "Clear current captures/result while retaining the previous state in history."));
        wrap.Children.Add(CreateNativeFatButton("Report Preview", NativeFatReportPreview_Click, "Show or hide the native FAT report preview without opening another FAT runtime."));

        _nativeFatShowHistoricalCheck = new CheckBox
        {
            Content = "Show historical",
            IsChecked = false,
            Margin = new Thickness(7, 6, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 10.7,
            Foreground = ResourceBrush("Muted", Color.FromRgb(0x66, 0x75, 0x8B)),
            ToolTip = "Show signals removed from the current Explorer/SCL scope. Their previous FAT evidence is never deleted."
        };
        _nativeFatShowHistoricalCheck.Checked += NativeFatShowHistorical_Changed;
        _nativeFatShowHistoricalCheck.Unchecked += NativeFatShowHistorical_Changed;
        wrap.Children.Add(_nativeFatShowHistoricalCheck);

        _nativeFatStatusText = new TextBlock
        {
            Text = "Select an IED to begin.",
            Margin = new Thickness(9, 7, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 10.4,
            Foreground = ResourceBrush("Muted", Color.FromRgb(0x66, 0x75, 0x8B))
        };
        wrap.Children.Add(_nativeFatStatusText);

        border.Child = wrap;
        return border;
    }

    private Grid BuildNativeFatBody()
    {
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
        _nativeFatPreviewGapColumn = new ColumnDefinition { Width = new GridLength(0) };
        _nativeFatPreviewColumn = new ColumnDefinition { Width = new GridLength(0), MinWidth = 0 };
        body.ColumnDefinitions.Add(_nativeFatPreviewGapColumn);
        body.ColumnDefinitions.Add(_nativeFatPreviewColumn);

        _nativeFatGrid = BuildNativeFatGrid(preview: false);
        Grid.SetColumn(_nativeFatGrid, 0);
        body.Children.Add(_nativeFatGrid);

        _nativeFatPreviewPane = BuildNativeFatPreviewPane();
        _nativeFatPreviewPane.Visibility = Visibility.Collapsed;
        Grid.SetColumn(_nativeFatPreviewPane, 2);
        body.Children.Add(_nativeFatPreviewPane);
        return body;
    }

    private DataGrid BuildNativeFatGrid(bool preview)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserReorderColumns = true,
            CanUserResizeColumns = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            IsReadOnly = true,
            SelectionMode = preview ? DataGridSelectionMode.Single : DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = ResourceBrush("Line", Color.FromRgb(0xE2, 0xE8, 0xF0)),
            BorderBrush = ResourceBrush("Line", Color.FromRgb(0xDD, 0xE5, 0xF0)),
            BorderThickness = new Thickness(1),
            RowHeight = preview ? 29 : 32,
            ColumnHeaderHeight = 31,
            Background = ResourceBrush("Surface", Colors.White),
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(0xFA, 0xFB, 0xFD)),
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true
        };

        if (TryFindResource("DataGridHeaderCompact") is Style headerStyle)
            grid.ColumnHeaderStyle = headerStyle;
        if (TryFindResource("DataGridCellCompact") is Style cellStyle)
            grid.CellStyle = cellStyle;

        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.FontSizeProperty, preview ? 10.0 : 10.4));
        var historicalTrigger = new DataTrigger
        {
            Binding = new Binding(nameof(NativeFatSignalRow.IsHistorical)),
            Value = true
        };
        historicalTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.56));
        rowStyle.Triggers.Add(historicalTrigger);
        grid.RowStyle = rowStyle;

        if (!preview)
        {
            grid.Columns.Add(TextColumn("SIGNAL", nameof(NativeFatSignalRow.SignalName), 1.25, minWidth: 130));
            grid.Columns.Add(TextColumn("IEC REFERENCE", nameof(NativeFatSignalRow.IecReference), 1.85, minWidth: 190));
            grid.Columns.Add(TextColumn("TYPE", nameof(NativeFatSignalRow.DataType), 0.72, minWidth: 72));
            grid.Columns.Add(TextColumn("LIVE VALUE", nameof(NativeFatSignalRow.LiveValue), 0.9, minWidth: 90));
            grid.Columns.Add(TextColumn("QUALITY", nameof(NativeFatSignalRow.Quality), 0.82, minWidth: 82));
            grid.Columns.Add(TextColumn("VALUE 1", nameof(NativeFatSignalRow.Value1Text), 0.78, minWidth: 78));
            grid.Columns.Add(TextColumn("VALUE 2", nameof(NativeFatSignalRow.Value2Text), 0.78, minWidth: 78));
            grid.Columns.Add(TextColumn("STATUS", nameof(NativeFatSignalRow.StatusText), 0.72, minWidth: 76));
            grid.Columns.Add(TextColumn("RESULT", nameof(NativeFatSignalRow.Result), 0.72, minWidth: 72));
            grid.Columns.Add(TextColumn("HISTORY", nameof(NativeFatSignalRow.HistoryText), 0.78, minWidth: 78));
        }
        else
        {
            grid.Columns.Add(TextColumn("SIGNAL", nameof(NativeFatSignalRow.SignalName), 1.35, minWidth: 115));
            grid.Columns.Add(TextColumn("V1", nameof(NativeFatSignalRow.Value1Text), 0.65, minWidth: 62));
            grid.Columns.Add(TextColumn("V2", nameof(NativeFatSignalRow.Value2Text), 0.65, minWidth: 62));
            grid.Columns.Add(TextColumn("RESULT", nameof(NativeFatSignalRow.Result), 0.72, minWidth: 68));
        }

        return grid;
    }

    private Border BuildNativeFatPreviewPane()
    {
        var border = new Border
        {
            Padding = new Thickness(11),
            CornerRadius = new CornerRadius(12),
            Background = ResourceBrush("SurfaceElevated", Colors.White),
            BorderBrush = ResourceBrush("Line", Color.FromRgb(0xDD, 0xE5, 0xF0)),
            BorderThickness = new Thickness(1)
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "FAT Report Preview",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("Ink", Color.FromRgb(0x20, 0x30, 0x4A))
        };
        grid.Children.Add(title);

        _nativeFatPreviewDeviceText = new TextBlock
        {
            Text = "IED · NONE",
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 10.6,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("Accent", Color.FromRgb(0x25, 0x63, 0xEB))
        };
        Grid.SetRow(_nativeFatPreviewDeviceText, 1);
        grid.Children.Add(_nativeFatPreviewDeviceText);

        _nativeFatPreviewSummaryText = new TextBlock
        {
            Text = "No FAT state loaded.",
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 10.2,
            Foreground = ResourceBrush("Muted", Color.FromRgb(0x66, 0x75, 0x8B)),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(_nativeFatPreviewSummaryText, 2);
        grid.Children.Add(_nativeFatPreviewSummaryText);

        _nativeFatPreviewGrid = BuildNativeFatGrid(preview: true);
        Grid.SetRow(_nativeFatPreviewGrid, 4);
        grid.Children.Add(_nativeFatPreviewGrid);

        _nativeFatPersistenceText = new TextBlock
        {
            Text = "Per-IED JSON · non-destructive reconciliation",
            Margin = new Thickness(0, 7, 0, 0),
            FontSize = 9.5,
            Foreground = ResourceBrush("Muted", Color.FromRgb(0x66, 0x75, 0x8B)),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(_nativeFatPersistenceText, 5);
        grid.Children.Add(_nativeFatPersistenceText);

        border.Child = grid;
        return border;
    }

    private DataGridTextColumn TextColumn(string header, string path, double star, double minWidth)
        => new()
        {
            Header = header,
            Binding = new Binding(path) { Mode = BindingMode.OneWay },
            Width = new DataGridLength(star, DataGridLengthUnitType.Star),
            MinWidth = minWidth
        };

    private Button CreateNativeFatButton(string text, RoutedEventHandler click, string toolTip)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(9, 5, 9, 5),
            MinHeight = 29,
            FontSize = 10.5,
            ToolTip = toolTip
        };
        if (TryFindResource("SoftButton") is Style style)
            button.Style = style;
        button.Click += click;
        return button;
    }

    private void InstallNativeFatNavigationButton()
    {
        if (_nativeFatNavButton != null)
            return;

        while (WorkflowNavGrid.ColumnDefinitions.Count < 7)
            WorkflowNavGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumnSpan(WorkflowPill, 7);
        _nativeFatNavButton = new Button
        {
            Name = "NavNativeFatButton",
            Content = "FAT",
            Tag = NativeFatWorkspaceIndex,
            MinWidth = 0,
            ToolTip = "Continuous FAT testing using the selected IED and live Explorer signal authority"
        };
        if (TryFindResource("SegmentedNavButton") is Style navStyle)
            _nativeFatNavButton.Style = navStyle;
        _nativeFatNavButton.Click += NativeFatNavButton_Click;
        Grid.SetColumn(_nativeFatNavButton, 6);
        WorkflowNavGrid.Children.Add(_nativeFatNavButton);
    }

    private void InstallNativeFatTimers()
    {
        _nativeFatReconcileTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(320)
        };
        _nativeFatReconcileTimer.Tick += NativeFatReconcileTimer_Tick;

        _nativeFatSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _nativeFatSaveTimer.Tick += NativeFatSaveTimer_Tick;
    }

    private async void NativeFatNavButton_Click(object sender, RoutedEventArgs e)
    {
        if (MainTabs.Items.Count <= NativeFatWorkspaceIndex)
            return;

        MainTabs.SelectedIndex = NativeFatWorkspaceIndex;
        QueueNativeFatNavigationGeometry();
        await EnsureNativeFatLoadedAsync(forceReconcile: false);
    }

    private void NativeFat_MainTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabs))
            return;

        QueueNativeFatNavigationGeometry();
        if (MainTabs.SelectedIndex == NativeFatWorkspaceIndex)
            _ = EnsureNativeFatLoadedAsync(forceReconcile: false);
        else
            QueueNativeFatSave();
    }

    private void NativeFat_MainWindowSizeChanged(object sender, SizeChangedEventArgs e)
        => QueueNativeFatNavigationGeometry();

    private void QueueNativeFatNavigationGeometry()
    {
        if (!_nativeFatInstalled)
            return;
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(ApplyNativeFatNavigationGeometry));
    }

    private void ApplyNativeFatNavigationGeometry()
    {
        if (!_nativeFatInstalled || _nativeFatNavButton == null)
            return;

        var availableWidth = ActualWidth > 0d ? ActualWidth : 1480d;
        var wide = availableWidth >= 1700d;
        var medium = availableWidth >= 1380d;
        var shellWidth = wide ? 1085d : medium ? 995d : 805d;

        WorkflowNavShell.Width = shellWidth;
        WorkflowNavShell.MinWidth = shellWidth;
        WorkflowNavShell.Height = 60;
        WorkflowNavShell.Padding = new Thickness(5, 6, 5, 6);
        WorkflowNavGrid.ClipToBounds = false;

        while (WorkflowNavGrid.ColumnDefinitions.Count < 7)
            WorkflowNavGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var column in WorkflowNavGrid.ColumnDefinitions)
            column.Width = new GridLength(1, GridUnitType.Star);

        var buttons = new[]
        {
            NavExplorerButton,
            NavLiveButton,
            NavEventsButton,
            NavAlarmButton,
            NavGooseButton,
            NavDiagnosticsButton,
            _nativeFatNavButton
        };
        for (var index = 0; index < buttons.Length; index++)
        {
            var button = buttons[index];
            button.MinHeight = 40;
            button.MinWidth = 0;
            button.Margin = new Thickness(1);
            button.Padding = wide ? new Thickness(9, 7, 9, 7) : new Thickness(5, 7, 5, 7);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            button.Foreground = index == MainTabs.SelectedIndex
                ? Brushes.White
                : ResourceBrush("Muted", Color.FromRgb(0x5F, 0x6B, 0x7A));
        }

        Grid.SetColumnSpan(WorkflowPill, 7);
        var contentWidth = Math.Max(0d, shellWidth - WorkflowNavShell.Padding.Left - WorkflowNavShell.Padding.Right);
        var cellWidth = contentWidth / 7d;
        WorkflowPill.Width = Math.Max(1d, cellWidth - 2d);
        WorkflowPill.Height = 36;

        WorkflowPillTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        WorkflowPillTranslate.X = Math.Clamp(MainTabs.SelectedIndex, 0, NativeFatWorkspaceIndex) * cellWidth;
    }

    private void NativeFat_MainWindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SelectedDevice))
            return;

        QueueNativeFatSave();
        AttachNativeFatObservedDevice(SelectedDevice);
        if (MainTabs.SelectedIndex == NativeFatWorkspaceIndex)
            _ = EnsureNativeFatLoadedAsync(forceReconcile: true);
        else
            UpdateNativeFatSummary();
    }

    private void AttachNativeFatObservedDevice(Iec61850MonitorDevice? device)
    {
        if (ReferenceEquals(_nativeFatObservedDevice, device))
            return;

        if (_nativeFatObservedDevice != null)
        {
            _nativeFatObservedDevice.Signals.CollectionChanged -= NativeFatSignalCollectionChanged;
            _nativeFatObservedDevice.Points.CollectionChanged -= NativeFatSignalCollectionChanged;
        }

        _nativeFatObservedDevice = device;
        if (_nativeFatObservedDevice != null)
        {
            _nativeFatObservedDevice.Signals.CollectionChanged += NativeFatSignalCollectionChanged;
            _nativeFatObservedDevice.Points.CollectionChanged += NativeFatSignalCollectionChanged;
        }
    }

    private void NativeFatSignalCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (MainTabs.SelectedIndex != NativeFatWorkspaceIndex)
            return;
        _nativeFatReconcileTimer?.Stop();
        _nativeFatReconcileTimer?.Start();
    }

    private async void NativeFatReconcileTimer_Tick(object? sender, EventArgs e)
    {
        _nativeFatReconcileTimer?.Stop();
        await EnsureNativeFatLoadedAsync(forceReconcile: true);
    }

    private async Task EnsureNativeFatLoadedAsync(bool forceReconcile)
    {
        if (!_nativeFatInstalled || MainTabs.SelectedIndex != NativeFatWorkspaceIndex)
            return;

        var device = SelectedDevice;
        if (device == null)
        {
            ClearNativeFatRows();
            _nativeFatCurrentState = null;
            UpdateNativeFatSummary();
            return;
        }

        var cacheKey = StableNativeFatDeviceKey(device);
        if (!forceReconcile && _nativeFatCurrentState != null &&
            _nativeFatCurrentState.DeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            UpdateNativeFatSummary();
            return;
        }

        _nativeFatLoadCts?.Cancel();
        _nativeFatLoadCts?.Dispose();
        _nativeFatLoadCts = CancellationTokenSource.CreateLinkedTokenSource(_applicationCancellation.Token);
        var token = _nativeFatLoadCts.Token;
        _nativeFatLoading = true;
        SetNativeFatStatus($"Loading FAT state for {device.Name}…");

        try
        {
            NativeFatDeviceState state;
            if (_nativeFatStateCache.TryGetValue(cacheKey, out var cached))
            {
                state = cached;
            }
            else
            {
                state = await _nativeFatStore.LoadAsync(device, token);
                token.ThrowIfCancellationRequested();
                _nativeFatStateCache[cacheKey] = state;
            }

            // We are back on the WPF dispatcher here. Reconciliation enumerates Explorer
            // ObservableCollections only on their owning UI thread.
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(device, SelectedDevice))
                return;

            NativeFatStateStore.Reconcile(state, device);
            _nativeFatCurrentState = state;
            RebuildNativeFatRows(state, device);
            QueueNativeFatSave();
            SetNativeFatStatus($"FAT ready · {device.Name}");
        }
        catch (OperationCanceledException)
        {
            // Fast IED switches are normal; stale loads are intentionally discarded.
        }
        catch (Exception ex)
        {
            AddLog("WARN", "Native FAT", $"FAT state load/reconcile failed: {ex.Message}");
            SetNativeFatStatus("FAT state could not be loaded. Explorer monitoring remains unaffected.");
        }
        finally
        {
            _nativeFatLoading = false;
            UpdateNativeFatSummary();
        }
    }

    private void RebuildNativeFatRows(NativeFatDeviceState state, Iec61850MonitorDevice device)
    {
        ClearNativeFatRows();
        foreach (var row in NativeFatStateStore.BuildRows(state, device))
        {
            row.StateChanged += NativeFatRow_StateChanged;
            _nativeFatRows.Add(row);
        }

        _nativeFatView = CollectionViewSource.GetDefaultView(_nativeFatRows);
        _nativeFatView.Filter = NativeFatViewFilter;
        if (_nativeFatGrid != null)
            _nativeFatGrid.ItemsSource = _nativeFatView;
        if (_nativeFatPreviewGrid != null)
            _nativeFatPreviewGrid.ItemsSource = _nativeFatRows;
        UpdateNativeFatSummary();
    }

    private void ClearNativeFatRows()
    {
        foreach (var row in _nativeFatRows)
        {
            row.StateChanged -= NativeFatRow_StateChanged;
            row.Dispose();
        }
        _nativeFatRows.Clear();
        _nativeFatView = null;
        if (_nativeFatGrid != null)
            _nativeFatGrid.ItemsSource = null;
        if (_nativeFatPreviewGrid != null)
            _nativeFatPreviewGrid.ItemsSource = null;
    }

    private bool NativeFatViewFilter(object item)
    {
        if (item is not NativeFatSignalRow row)
            return false;

        if (row.IsHistorical && _nativeFatShowHistoricalCheck?.IsChecked != true)
            return false;

        var query = _nativeFatSearchBox?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return row.SignalName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               row.IecReference.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               row.IecTelegram.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               row.DataType.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               row.LiveValue.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               row.Value1Text.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               row.Value2Text.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               row.Result.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               row.StatusText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void NativeFatSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => _nativeFatView?.Refresh();

    private void NativeFatShowHistorical_Changed(object sender, RoutedEventArgs e)
    {
        _nativeFatView?.Refresh();
        UpdateNativeFatSummary();
    }

    private void NativeFatRow_StateChanged(object? sender, EventArgs e)
    {
        QueueNativeFatSave();
        UpdateNativeFatSummary();
        _nativeFatPreviewGrid?.Items.Refresh();
    }

    private IReadOnlyList<NativeFatSignalRow> SelectedNativeFatRows()
    {
        if (_nativeFatGrid == null)
            return Array.Empty<NativeFatSignalRow>();

        var selected = _nativeFatGrid.SelectedItems.OfType<NativeFatSignalRow>().ToArray();
        if (selected.Length > 0)
            return selected;
        return _nativeFatGrid.CurrentItem is NativeFatSignalRow current
            ? new[] { current }
            : Array.Empty<NativeFatSignalRow>();
    }

    private void NativeFatRefresh_Click(object sender, RoutedEventArgs e)
        => _ = EnsureNativeFatLoadedAsync(forceReconcile: true);

    private void NativeFatCapture1_Click(object sender, RoutedEventArgs e)
        => CaptureNativeFatSelection(1);

    private void NativeFatCapture2_Click(object sender, RoutedEventArgs e)
        => CaptureNativeFatSelection(2);

    private void CaptureNativeFatSelection(int slot)
    {
        var rows = SelectedNativeFatRows();
        if (rows.Count == 0)
        {
            SetNativeFatStatus("Select one or more FAT signals first.");
            return;
        }

        var captured = 0;
        foreach (var row in rows)
        {
            if (row.CaptureValue(slot))
                captured++;
        }
        SetNativeFatStatus(captured == 0
            ? "No selected row has a current Explorer signal source. Historical rows cannot be captured."
            : $"Captured Value {slot} for {captured} signal(s). Autosave queued.");
    }

    private void NativeFatPass_Click(object sender, RoutedEventArgs e)
        => SetNativeFatSelectionResult(NativeFatResult.Pass);

    private void NativeFatReview_Click(object sender, RoutedEventArgs e)
        => SetNativeFatSelectionResult(NativeFatResult.Review);

    private void NativeFatFail_Click(object sender, RoutedEventArgs e)
        => SetNativeFatSelectionResult(NativeFatResult.Fail);

    private void SetNativeFatSelectionResult(string result)
    {
        var rows = SelectedNativeFatRows();
        if (rows.Count == 0)
        {
            SetNativeFatStatus("Select one or more FAT signals first.");
            return;
        }

        foreach (var row in rows)
            row.SetResult(result);
        SetNativeFatStatus($"{rows.Count} signal(s) marked {result}. Previous state retained in history.");
    }

    private void NativeFatReset_Click(object sender, RoutedEventArgs e)
    {
        var rows = SelectedNativeFatRows();
        if (rows.Count == 0)
        {
            SetNativeFatStatus("Select one or more FAT signals first.");
            return;
        }

        foreach (var row in rows)
            row.ResetCurrentResult();
        SetNativeFatStatus($"Reset current FAT state for {rows.Count} signal(s); history was retained.");
    }

    private void NativeFatReportPreview_Click(object sender, RoutedEventArgs e)
    {
        _nativeFatPreviewVisible = !_nativeFatPreviewVisible;
        if (_nativeFatPreviewPane == null || _nativeFatPreviewGapColumn == null || _nativeFatPreviewColumn == null)
            return;

        _nativeFatPreviewPane.Visibility = _nativeFatPreviewVisible ? Visibility.Visible : Visibility.Collapsed;
        _nativeFatPreviewGapColumn.Width = new GridLength(_nativeFatPreviewVisible ? 10 : 0);
        _nativeFatPreviewColumn.Width = _nativeFatPreviewVisible ? new GridLength(330) : new GridLength(0);
        UpdateNativeFatSummary();
    }

    private void QueueNativeFatSave()
    {
        if (_nativeFatCurrentState == null || _nativeFatSaveTimer == null)
            return;
        _nativeFatSaveTimer.Stop();
        _nativeFatSaveTimer.Start();
    }

    private async void NativeFatSaveTimer_Tick(object? sender, EventArgs e)
    {
        _nativeFatSaveTimer?.Stop();
        if (_nativeFatCurrentState != null)
            await SaveNativeFatStateAsync(_nativeFatCurrentState);
    }

    private async Task SaveNativeFatStateAsync(NativeFatDeviceState state)
    {
        try
        {
            await _nativeFatSaveGate.WaitAsync(_applicationCancellation.Token);
            try
            {
                await _nativeFatStore.SaveAsync(state, _applicationCancellation.Token);
            }
            finally
            {
                _nativeFatSaveGate.Release();
            }

            if (ReferenceEquals(state, _nativeFatCurrentState))
            {
                SetNativeFatStatus($"Saved · {DateTime.Now:HH:mm:ss}");
                UpdateNativeFatSummary();
            }
        }
        catch (OperationCanceledException)
        {
            // Application shutdown or superseded state.
        }
        catch (Exception ex)
        {
            AddLog("WARN", "Native FAT", $"Autosave failed: {ex.Message}");
            if (ReferenceEquals(state, _nativeFatCurrentState))
                SetNativeFatStatus("Autosave failed; current in-memory FAT state is still available.");
        }
    }

    private void UpdateNativeFatSummary()
    {
        var device = SelectedDevice;
        var deviceLabel = device == null ? "NONE" : device.Name;
        if (_nativeFatContextText != null)
            _nativeFatContextText.Text = $"IED · {deviceLabel}";
        if (_nativeFatPreviewDeviceText != null)
            _nativeFatPreviewDeviceText.Text = $"IED · {deviceLabel}";

        var current = _nativeFatRows.Count(row => !row.IsHistorical);
        var historical = _nativeFatRows.Count(row => row.IsHistorical);
        var pass = _nativeFatRows.Count(row => !row.IsHistorical && row.Result == NativeFatResult.Pass);
        var review = _nativeFatRows.Count(row => !row.IsHistorical && row.Result == NativeFatResult.Review);
        var fail = _nativeFatRows.Count(row => !row.IsHistorical && row.Result == NativeFatResult.Fail);
        var untested = Math.Max(0, current - pass - review - fail);

        if (_nativeFatPreviewSummaryText != null)
        {
            _nativeFatPreviewSummaryText.Text = device == null
                ? "Select an IED in the persistent Explorer."
                : $"Current {current} · PASS {pass} · REVIEW {review} · FAIL {fail} · UNTESTED {untested} · historical {historical}";
        }

        if (_nativeFatPersistenceText != null)
        {
            _nativeFatPersistenceText.Text = _nativeFatCurrentState == null
                ? "Per-IED JSON · non-destructive reconciliation"
                : $"Resume file: {Path.GetFileName(_nativeFatCurrentState.StoragePath)}\nHistorical IEC identities remain stored when engineering changes.";
        }

        if (!_nativeFatLoading && device != null && _nativeFatStatusText != null &&
            (_nativeFatStatusText.Text.StartsWith("Select", StringComparison.OrdinalIgnoreCase) ||
             _nativeFatStatusText.Text.StartsWith("Loading", StringComparison.OrdinalIgnoreCase)))
        {
            _nativeFatStatusText.Text = $"{current} current · {historical} historical · {untested} untested";
        }
    }

    private void SetNativeFatStatus(string text)
    {
        if (_nativeFatStatusText != null)
            _nativeFatStatusText.Text = text;
    }

    private static string StableNativeFatDeviceKey(Iec61850MonitorDevice device)
        => !string.IsNullOrWhiteSpace(device.DeviceId) ? device.DeviceId : device.Name;

    private Brush ResourceBrush(string key, Color fallback)
        => TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    private void NativeFat_MainWindowClosed(object? sender, EventArgs e)
    {
        _nativeFatInstallRetry?.Stop();
        _nativeFatReconcileTimer?.Stop();
        _nativeFatSaveTimer?.Stop();
        _nativeFatLoadCts?.Cancel();
        _nativeFatLoadCts?.Dispose();

        PropertyChanged -= NativeFat_MainWindowPropertyChanged;
        MainTabs.SelectionChanged -= NativeFat_MainTabsSelectionChanged;
        SizeChanged -= NativeFat_MainWindowSizeChanged;
        Closed -= NativeFat_MainWindowClosed;

        if (_nativeFatObservedDevice != null)
        {
            _nativeFatObservedDevice.Signals.CollectionChanged -= NativeFatSignalCollectionChanged;
            _nativeFatObservedDevice.Points.CollectionChanged -= NativeFatSignalCollectionChanged;
        }

        ClearNativeFatRows();
        _nativeFatSaveGate.Dispose();
    }
}
