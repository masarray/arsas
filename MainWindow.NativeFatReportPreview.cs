using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

/// <summary>
/// P2 report-preview hardening for the native FAT workspace. The preview consumes an
/// immutable evidence snapshot instead of the live row objects, while the main FAT grid
/// remains directly bound to Explorer/runtime values for testing.
/// </summary>
public partial class MainWindow
{
    private DispatcherTimer? _nativeFatReportPreviewInstallRetry;
    private TextBlock? _nativeFatPreviewEvidenceText;
    private NativeFatReportSnapshot? _nativeFatReportSnapshot;
    private readonly HashSet<NativeFatSignalRow> _nativeFatReportObservedRows = new();
    private bool _nativeFatReportPreviewEnhanced;
    private bool _nativeFatReportRefreshQueued;

    [ModuleInitializer]
    internal static void RegisterNativeFatReportPreviewEnhancement()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(NativeFatReportPreview_MainWindowLoaded),
            handledEventsToo: true);
    }

    private static void NativeFatReportPreview_MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._nativeFatReportPreviewEnhanced)
            return;

        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(window.TryInstallNativeFatReportPreviewEnhancement));
    }

    private void TryInstallNativeFatReportPreviewEnhancement()
    {
        if (_nativeFatReportPreviewEnhanced || !IsLoaded)
            return;

        if (!_nativeFatInstalled || _nativeFatPreviewPane?.Child is not Grid previewRoot ||
            _nativeFatPreviewGrid == null || _nativeFatPreviewSummaryText == null)
        {
            _nativeFatReportPreviewInstallRetry ??= new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            _nativeFatReportPreviewInstallRetry.Tick -= NativeFatReportPreviewInstallRetry_Tick;
            _nativeFatReportPreviewInstallRetry.Tick += NativeFatReportPreviewInstallRetry_Tick;
            _nativeFatReportPreviewInstallRetry.Start();
            return;
        }

        _nativeFatReportPreviewInstallRetry?.Stop();
        _nativeFatReportPreviewEnhanced = true;

        // The initial P2 pane already has title/device/summary/grid/persistence rows. Add
        // one compact evidence inspector between the grid and persistence note.
        previewRoot.RowDefinitions.Insert(5, new RowDefinition { Height = GridLength.Auto });
        if (_nativeFatPersistenceText != null)
            Grid.SetRow(_nativeFatPersistenceText, 6);

        _nativeFatPreviewEvidenceText = new TextBlock
        {
            Text = "Select a report row to inspect captured evidence.",
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(8, 7, 8, 7),
            FontSize = 9.4,
            Foreground = ResourceBrush("Muted", Color.FromRgb(0x66, 0x75, 0x8B)),
            Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF9, 0xFC)),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(_nativeFatPreviewEvidenceText, 5);
        previewRoot.Children.Add(_nativeFatPreviewEvidenceText);

        // Existing preview columns deliberately use property names also exposed by the
        // snapshot row. Add the canonical IEC identity so report scope can be audited.
        if (_nativeFatPreviewGrid.Columns.All(column => !Equals(column.Header, "IEC")))
        {
            _nativeFatPreviewGrid.Columns.Insert(1, new DataGridTextColumn
            {
                Header = "IEC",
                Binding = new Binding(nameof(NativeFatReportRow.IecReference)) { Mode = BindingMode.OneWay },
                Width = new DataGridLength(1.15, DataGridLengthUnitType.Star),
                MinWidth = 105
            });
        }

        if (_nativeFatPreviewGrid.Columns.Count >= 5)
        {
            _nativeFatPreviewGrid.Columns[0].Width = new DataGridLength(1.15, DataGridLengthUnitType.Star);
            _nativeFatPreviewGrid.Columns[2].Width = new DataGridLength(0.58, DataGridLengthUnitType.Star);
            _nativeFatPreviewGrid.Columns[3].Width = new DataGridLength(0.58, DataGridLengthUnitType.Star);
            _nativeFatPreviewGrid.Columns[4].Width = new DataGridLength(0.62, DataGridLengthUnitType.Star);
        }

        _nativeFatPreviewGrid.SelectionChanged += NativeFatPreviewGrid_SelectionChanged;
        _nativeFatPreviewPane!.IsVisibleChanged += NativeFatPreviewPane_IsVisibleChanged;
        _nativeFatRows.CollectionChanged += NativeFatReportRows_CollectionChanged;
        if (_nativeFatShowHistoricalCheck != null)
        {
            _nativeFatShowHistoricalCheck.Checked += NativeFatReportScope_Changed;
            _nativeFatShowHistoricalCheck.Unchecked += NativeFatReportScope_Changed;
        }

        SyncNativeFatReportRowSubscriptions();
        Closed += NativeFatReportPreview_MainWindowClosed;
        QueueNativeFatReportPreviewRefresh();
    }

    private void NativeFatReportPreviewInstallRetry_Tick(object? sender, EventArgs e)
    {
        _nativeFatReportPreviewInstallRetry?.Stop();
        TryInstallNativeFatReportPreviewEnhancement();
    }

    private void NativeFatPreviewPane_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_nativeFatPreviewPane?.IsVisible == true && _nativeFatPreviewColumn != null)
        {
            // 330 px was enough for a four-column mock preview but not for auditable IEC
            // identity + evidence. Keep it compact while making the report pane useful.
            _nativeFatPreviewColumn.Width = new GridLength(430);
            QueueNativeFatReportPreviewRefresh();
        }
    }

    private void NativeFatReportRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncNativeFatReportRowSubscriptions();
        QueueNativeFatReportPreviewRefresh();
    }

    private void SyncNativeFatReportRowSubscriptions()
    {
        var current = _nativeFatRows.ToHashSet();
        foreach (var row in _nativeFatReportObservedRows.Where(row => !current.Contains(row)).ToArray())
        {
            row.PropertyChanged -= NativeFatReportRow_PropertyChanged;
            _nativeFatReportObservedRows.Remove(row);
        }

        foreach (var row in current)
        {
            if (!_nativeFatReportObservedRows.Add(row))
                continue;
            row.PropertyChanged += NativeFatReportRow_PropertyChanged;
        }
    }

    private void NativeFatReportRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NativeFatSignalRow.Value1Text) or
            nameof(NativeFatSignalRow.Value2Text) or
            nameof(NativeFatSignalRow.Result) or
            nameof(NativeFatSignalRow.HistoryText) or
            nameof(NativeFatSignalRow.IsHistorical))
        {
            QueueNativeFatReportPreviewRefresh();
        }
    }

    private void NativeFatReportScope_Changed(object sender, RoutedEventArgs e)
        => QueueNativeFatReportPreviewRefresh();

    private void QueueNativeFatReportPreviewRefresh()
    {
        if (!_nativeFatReportPreviewEnhanced || _nativeFatPreviewPane?.IsVisible != true || _nativeFatReportRefreshQueued)
            return;

        _nativeFatReportRefreshQueued = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                _nativeFatReportRefreshQueued = false;
                RefreshNativeFatReportPreviewSnapshot();
            }));
    }

    private void RefreshNativeFatReportPreviewSnapshot()
    {
        if (_nativeFatPreviewPane?.IsVisible != true || _nativeFatPreviewGrid == null)
            return;

        var device = SelectedDevice;
        var state = _nativeFatCurrentState;
        if (device == null || state == null ||
            !state.DeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            _nativeFatReportSnapshot = null;
            _nativeFatPreviewGrid.ItemsSource = null;
            if (_nativeFatPreviewSummaryText != null)
                _nativeFatPreviewSummaryText.Text = "Select an IED with a loaded FAT state.";
            if (_nativeFatPreviewEvidenceText != null)
                _nativeFatPreviewEvidenceText.Text = "No report evidence is loaded.";
            return;
        }

        var previousReference = (_nativeFatPreviewGrid.SelectedItem as NativeFatReportRow)?.IecReference;
        _nativeFatReportSnapshot = NativeFatReportSnapshotBuilder.Build(device, state);
        var showHistorical = _nativeFatShowHistoricalCheck?.IsChecked == true;
        var visibleRows = _nativeFatReportSnapshot.Rows
            .Where(row => showHistorical || !row.IsHistorical)
            .ToArray();
        _nativeFatPreviewGrid.ItemsSource = visibleRows;

        if (_nativeFatPreviewDeviceText != null)
            _nativeFatPreviewDeviceText.Text = $"IED · {_nativeFatReportSnapshot.IedName} · {_nativeFatReportSnapshot.IpAddress}:{_nativeFatReportSnapshot.Port}";
        if (_nativeFatPreviewSummaryText != null)
        {
            var scopeSuffix = !showHistorical && _nativeFatReportSnapshot.HistoricalCount > 0
                ? " · historical hidden"
                : string.Empty;
            _nativeFatPreviewSummaryText.Text = _nativeFatReportSnapshot.SummaryText + scopeSuffix;
        }

        NativeFatReportRow? selection = null;
        if (!string.IsNullOrWhiteSpace(previousReference))
        {
            selection = visibleRows.FirstOrDefault(row =>
                row.IecReference.Equals(previousReference, StringComparison.OrdinalIgnoreCase));
        }
        selection ??= visibleRows.FirstOrDefault();
        if (selection != null)
        {
            _nativeFatPreviewGrid.SelectedItem = selection;
            _nativeFatPreviewGrid.ScrollIntoView(selection);
        }
        else
        {
            UpdateNativeFatPreviewEvidence(null);
        }
    }

    private void NativeFatPreviewGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateNativeFatPreviewEvidence(_nativeFatPreviewGrid?.SelectedItem as NativeFatReportRow);

    private void UpdateNativeFatPreviewEvidence(NativeFatReportRow? row)
    {
        if (_nativeFatPreviewEvidenceText == null)
            return;

        _nativeFatPreviewEvidenceText.Text = row?.EvidenceSummaryText ??
                                             "Select a report row to inspect captured evidence.";
    }

    private void NativeFatReportPreview_MainWindowClosed(object? sender, EventArgs e)
    {
        _nativeFatReportPreviewInstallRetry?.Stop();
        if (_nativeFatPreviewGrid != null)
            _nativeFatPreviewGrid.SelectionChanged -= NativeFatPreviewGrid_SelectionChanged;
        if (_nativeFatPreviewPane != null)
            _nativeFatPreviewPane.IsVisibleChanged -= NativeFatPreviewPane_IsVisibleChanged;
        _nativeFatRows.CollectionChanged -= NativeFatReportRows_CollectionChanged;
        if (_nativeFatShowHistoricalCheck != null)
        {
            _nativeFatShowHistoricalCheck.Checked -= NativeFatReportScope_Changed;
            _nativeFatShowHistoricalCheck.Unchecked -= NativeFatReportScope_Changed;
        }

        foreach (var row in _nativeFatReportObservedRows)
            row.PropertyChanged -= NativeFatReportRow_PropertyChanged;
        _nativeFatReportObservedRows.Clear();
        Closed -= NativeFatReportPreview_MainWindowClosed;
    }
}
