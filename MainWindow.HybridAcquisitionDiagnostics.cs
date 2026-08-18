using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    public ObservableCollection<HybridSignalAcquisitionTelemetry> HybridAcquisitionTelemetry { get; } = new();

    private DispatcherTimer? _hybridAcquisitionTelemetryTimer;
    private TextBlock? _hybridAcquisitionSummaryText;
    private bool _hybridAcquisitionDiagnosticsInstalled;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Loaded += MainWindow_HybridAcquisitionDiagnosticsLoaded;
        Closed += MainWindow_HybridAcquisitionDiagnosticsClosed;
    }

    private void MainWindow_HybridAcquisitionDiagnosticsLoaded(object sender, RoutedEventArgs e)
    {
        InstallHybridAcquisitionDiagnosticsPanel();
        MainTabs.SelectionChanged += MainTabs_HybridAcquisitionDiagnosticsSelectionChanged;
        _hybridAcquisitionTelemetryTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        _hybridAcquisitionTelemetryTimer.Tick += (_, _) =>
        {
            if (MainTabs.SelectedIndex == 5)
                RefreshHybridAcquisitionTelemetry();
        };
        _hybridAcquisitionTelemetryTimer.Start();
    }

    private void MainWindow_HybridAcquisitionDiagnosticsClosed(object? sender, EventArgs e)
    {
        if (_hybridAcquisitionTelemetryTimer is not null)
        {
            _hybridAcquisitionTelemetryTimer.Stop();
            _hybridAcquisitionTelemetryTimer = null;
        }
    }

    private void MainTabs_HybridAcquisitionDiagnosticsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainTabs.SelectedIndex == 5)
            RefreshHybridAcquisitionTelemetry();
    }

    private void InstallHybridAcquisitionDiagnosticsPanel()
    {
        if (_hybridAcquisitionDiagnosticsInstalled)
            return;

        var diagnosticsTab = MainTabs.Items
            .OfType<TabItem>()
            .FirstOrDefault(item => string.Equals(item.Header?.ToString(), "Diagnostics", StringComparison.Ordinal));
        if (diagnosticsTab?.Content is not Border workspace || workspace.Child is not DockPanel dock)
            return;

        _hybridAcquisitionDiagnosticsInstalled = true;

        var shell = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(10, 9, 10, 8),
            Background = new SolidColorBrush(Color.FromRgb(248, 251, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(216, 228, 244)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12)
        };
        DockPanel.SetDock(shell, Dock.Top);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(7) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(228) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "IEC 61850 signal acquisition evidence",
            FontSize = 12.8,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 55))
        });
        _hybridAcquisitionSummaryText = new TextBlock
        {
            Text = "Start monitoring to capture engine-authoritative per-signal acquisition evidence.",
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 10.3,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133))
        };
        heading.Children.Add(_hybridAcquisitionSummaryText);
        header.Children.Add(heading);

        var refresh = new Button
        {
            Content = "Refresh acquisition",
            Padding = new Thickness(10, 5, 10, 5),
            MinHeight = 30,
            ToolTip = "Re-capture ARIEC planning, activation, dynamic-attempt, fallback, and rollback evidence"
        };
        if (TryFindResource("SoftButton") is Style buttonStyle)
            refresh.Style = buttonStyle;
        refresh.Click += (_, _) => RefreshHybridAcquisitionTelemetry();
        Grid.SetColumn(refresh, 1);
        header.Children.Add(refresh);
        layout.Children.Add(header);

        var grid = BuildHybridAcquisitionTelemetryGrid();
        Grid.SetRow(grid, 2);
        layout.Children.Add(grid);
        shell.Child = layout;

        // Existing diagnostics header stays first and the communication journal remains
        // the fill element. The evidence panel is deliberately inserted between them.
        dock.Children.Insert(Math.Min(1, dock.Children.Count), shell);
        RefreshHybridAcquisitionTelemetry();
    }

    private DataGrid BuildHybridAcquisitionTelemetryGrid()
    {
        var grid = new DataGrid
        {
            ItemsSource = HybridAcquisitionTelemetry,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            RowHeight = 34
        };
        if (TryFindResource("ModernDataGrid") is Style gridStyle)
            grid.Style = gridStyle;

        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(nameof(HybridSignalAcquisitionTelemetry.Detail))));
        grid.RowStyle = rowStyle;

        grid.Columns.Add(TextColumn("IED", nameof(HybridSignalAcquisitionTelemetry.DeviceName), 105));
        grid.Columns.Add(TextColumn("Signal", nameof(HybridSignalAcquisitionTelemetry.SignalName), 155));
        grid.Columns.Add(TextColumn("IEC reference", nameof(HybridSignalAcquisitionTelemetry.IecReference), 280));
        grid.Columns.Add(TextColumn("Final state", nameof(HybridSignalAcquisitionTelemetry.StateLabel), 178));
        grid.Columns.Add(TextColumn("Acquisition", nameof(HybridSignalAcquisitionTelemetry.AcquisitionKind), 125));
        grid.Columns.Add(TextColumn("Dynamic attempt", nameof(HybridSignalAcquisitionTelemetry.DynamicAttemptLabel), 130));
        grid.Columns.Add(TextColumn("Exact reason", nameof(HybridSignalAcquisitionTelemetry.ExactReason), 240));
        grid.Columns.Add(TextColumn("Cleanup", nameof(HybridSignalAcquisitionTelemetry.CleanupLabel), 120));
        grid.Columns.Add(TextColumn("RCB", nameof(HybridSignalAcquisitionTelemetry.ReportControlReference), 230));
        grid.Columns.Add(TextColumn("DataSet", nameof(HybridSignalAcquisitionTelemetry.DataSetReference), 230));
        return grid;
    }

    private static DataGridTextColumn TextColumn(string header, string property, double width)
        => new()
        {
            Header = header,
            Binding = new Binding(property) { Mode = BindingMode.OneWay },
            Width = width
        };

    private void RefreshHybridAcquisitionTelemetry()
    {
        if (!_hybridAcquisitionDiagnosticsInstalled || Dispatcher.HasShutdownStarted)
            return;

        var rows = new List<HybridSignalAcquisitionTelemetry>();
        var snapshotCount = 0;
        var staticCount = 0;
        var dynamicCount = 0;
        var dynamicFailedCount = 0;
        var pollingCount = 0;
        var pendingCount = 0;

        foreach (var device in Devices.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var snapshot = _runtime.CaptureHybridReportPhysicalValidation(device.DeviceId);
                snapshotCount++;
                rows.AddRange(snapshot.SignalTelemetry);
                staticCount += snapshot.StaticReportSignalCount;
                dynamicCount += snapshot.DynamicReportSignalCount;
                dynamicFailedCount += snapshot.DynamicFailedPollingSignalCount;
                pollingCount += snapshot.FinalPollingSignalCount;
                pendingCount += snapshot.PendingSignalCount;
            }
            catch (InvalidOperationException)
            {
                // Device has no active runtime session. This is normal for offline SCL
                // workspaces and disconnected IEDs, so it is not a diagnostics warning.
            }
        }

        rows = rows
            .OrderBy(item => TelemetryStateOrder(item.State))
            .ThenBy(item => item.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.IecReference, StringComparer.OrdinalIgnoreCase)
            .ToList();

        HybridAcquisitionTelemetry.Clear();
        foreach (var row in rows)
            HybridAcquisitionTelemetry.Add(row);

        if (_hybridAcquisitionSummaryText is null)
            return;

        _hybridAcquisitionSummaryText.Text = rows.Count == 0
            ? snapshotCount == 0
                ? "No active IEC 61850 runtime session. Connect/start monitoring to capture acquisition evidence."
                : "Hybrid planner has not produced per-signal evidence yet. Start or re-arm monitoring."
            : $"Signals {rows.Count:N0} • Static {staticCount:N0} • Dynamic {dynamicCount:N0} • Dynamic failed→polling {dynamicFailedCount:N0} • Final polling {pollingCount:N0} • Pending {pendingCount:N0}";
    }

    private static int TelemetryStateOrder(HybridSignalAcquisitionState state)
        => state switch
        {
            HybridSignalAcquisitionState.DynamicFailedPolling => 0,
            HybridSignalAcquisitionState.PollingFallback => 1,
            HybridSignalAcquisitionState.Uncovered => 2,
            HybridSignalAcquisitionState.Pending => 3,
            HybridSignalAcquisitionState.DynamicReport => 4,
            HybridSignalAcquisitionState.StaticReport => 5,
            _ => 6
        };
}
