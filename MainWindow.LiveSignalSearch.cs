using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

/// <summary>
/// Lightweight, presentation-only search for the Explorer live-value workspace.
/// Filtering is performed over the existing ICollectionView; it never changes the
/// monitored point collection, acquisition lifecycle, or IEC 61850 network traffic.
/// </summary>
public partial class MainWindow
{
    private static readonly bool LiveSignalSearchClassHandlerRegistered = RegisterLiveSignalSearchClassHandler();

    private DataGrid? _liveSignalSearchGrid;
    private TextBox? _liveSignalSearchBox;
    private TextBlock? _liveSignalSearchCount;
    private ICollectionView? _liveSignalSearchView;
    private INotifyCollectionChanged? _liveSignalSearchCollection;
    private DependencyPropertyDescriptor? _liveSignalItemsSourceDescriptor;
    private bool _liveSignalSearchInstalled;
    private string _unifiedExplorerSearchText = "";
    private string[] _unifiedLiveSearchTokens = Array.Empty<string>();
    private bool _liveSignalCountRefreshScheduled;
    // Debounce only large point-model text edits. Switching IED and clearing
    // the query still applies immediately so the visible workspace is current.
    private DispatcherTimer? _liveSignalFilterDebounce;

    private static bool RegisterLiveSignalSearchClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(LiveSignalSearch_MainWindowLoaded));
        return true;
    }

    private static void LiveSignalSearch_MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
            return;

        window.Dispatcher.BeginInvoke(new Action(window.InstallLiveSignalSearch));
    }

    private void InstallLiveSignalSearch()
    {
        if (_liveSignalSearchInstalled)
            return;

        var dataGrid = FindLiveSignalVisualChildren<DataGrid>(this)
            .FirstOrDefault(IsExplorerLiveSignalGrid);
        if (dataGrid?.Parent is not Grid host)
            return;

        _liveSignalSearchInstalled = true;
        _liveSignalSearchGrid = dataGrid;

        // The existing host contains the DataGrid plus its empty-workspace overlay.
        // Give both row 1 and reserve a compact row 0 for the search toolbar.
        if (host.RowDefinitions.Count == 0)
        {
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            foreach (UIElement child in host.Children.Cast<UIElement>().ToArray())
                Grid.SetRow(child, 1);
        }

        var toolbar = BuildLiveSignalSearchToolbar();
        Grid.SetRow(toolbar, 0);
        host.Children.Add(toolbar);

        _liveSignalItemsSourceDescriptor = DependencyPropertyDescriptor.FromProperty(
            ItemsControl.ItemsSourceProperty,
            typeof(DataGrid));
        _liveSignalItemsSourceDescriptor?.AddValueChanged(dataGrid, LiveSignalSearch_ItemsSourceChanged);

        PreviewKeyDown += LiveSignalSearch_WindowPreviewKeyDown;
        Closed += LiveSignalSearch_WindowClosed;
        AttachLiveSignalSearchSource();
    }

    private FrameworkElement BuildLiveSignalSearchToolbar()
    {
        var toolbar = new Grid
        {
            Margin = new Thickness(2, 0, 2, 9),
            Height = 34
        };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        titlePanel.Children.Add(new Border
        {
            Width = 7,
            Height = 7,
            CornerRadius = new CornerRadius(3.5),
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            Margin = new Thickness(2, 0, 8, 0)
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = "LIVE SIGNAL VALUES",
            FontSize = 11.2,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(70, 86, 109)),
            VerticalAlignment = VerticalAlignment.Center
        });
        _liveSignalSearchCount = new TextBlock
        {
            Text = string.Empty,
            FontSize = 10.8,
            Foreground = new SolidColorBrush(Color.FromRgb(126, 142, 165)),
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        titlePanel.Children.Add(_liveSignalSearchCount);
        toolbar.Children.Add(titlePanel);

        _liveSignalSearchBox = new TextBox
        {
            Width = 390,
            Style = ResolveIndustrialSearchStyle(),
            Tag = "Search signal, IEC reference, value, quality or acquisition",
            ToolTip = "Filter the current live workspace. Ctrl+F focuses search; Esc clears it.",
            VerticalAlignment = VerticalAlignment.Center
        };
        _liveSignalSearchBox.TextChanged += LiveSignalSearch_TextChanged;
        _liveSignalSearchBox.PreviewKeyDown += LiveSignalSearch_BoxPreviewKeyDown;
        Grid.SetColumn(_liveSignalSearchBox, 1);
        toolbar.Children.Add(_liveSignalSearchBox);

        return toolbar;
    }

    private Style? ResolveIndustrialSearchStyle()
    {
        var style = TryFindResource("IndustrialSearchTextBox") as Style
                    ?? Application.Current?.TryFindResource("IndustrialSearchTextBox") as Style;
        if (style != null)
            return style;

        // Defensive fallback for load ordering: use the same resource dictionary that
        // P2IndustrialWorkstationUx uses for the IED Explorer search field.
        var dictionary = new ResourceDictionary
        {
            Source = new Uri("/ARSAS;component/Resources/P2IndustrialControls.xaml", UriKind.Relative)
        };
        return dictionary["IndustrialSearchTextBox"] as Style;
    }

    private static bool IsExplorerLiveSignalGrid(DataGrid grid)
    {
        var headers = grid.Columns
            .Select(column => column.Header?.ToString() ?? string.Empty)
            .ToArray();

        // This six-column signature is unique to the selected-IED live-value workspace.
        // The global monitor has an additional IED column and the command grid has a
        // different schema, so no visual-tree TabItem assumptions are required.
        return headers.Length == 6 &&
               headers[0].Equals("Signal", StringComparison.OrdinalIgnoreCase) &&
               headers[1].Equals("IEC Telegram", StringComparison.OrdinalIgnoreCase) &&
               headers[2].Equals("Value", StringComparison.OrdinalIgnoreCase) &&
               headers[3].Equals("Quality", StringComparison.OrdinalIgnoreCase) &&
               headers[4].Equals("IED Timestamp", StringComparison.OrdinalIgnoreCase) &&
               headers[5].Equals("Acquisition", StringComparison.OrdinalIgnoreCase);
    }

    private void LiveSignalSearch_ItemsSourceChanged(object? sender, EventArgs e)
        => AttachLiveSignalSearchSource();

    private void AttachLiveSignalSearchSource()
    {
        if (_liveSignalSearchCollection != null)
            _liveSignalSearchCollection.CollectionChanged -= LiveSignalSearch_CollectionChanged;
        if (_liveSignalSearchView?.Filter != null)
            _liveSignalSearchView.Filter = null;

        var source = _liveSignalSearchGrid?.ItemsSource;
        _liveSignalSearchCollection = source as INotifyCollectionChanged;
        if (_liveSignalSearchCollection != null)
            _liveSignalSearchCollection.CollectionChanged += LiveSignalSearch_CollectionChanged;

        _liveSignalSearchView = source == null
            ? null
            : CollectionViewSource.GetDefaultView(source);
        // Natural ordering is presentation-only. The underlying static DataSet
        // descriptors and MMS/GOOSE FCDA order must remain untouched.
        // WPF CustomSort is O(N log N) on the Dispatcher. On large models,
        // retain deterministic source order rather than freezing the IED switch.
        if (_liveSignalSearchView is ListCollectionView listView &&
            _liveSignalSearchGrid?.ItemsSource is ICollection sourceCollection &&
            sourceCollection.Count <= 2048 &&
            listView.SortDescriptions.Count == 0 && listView.CustomSort is null)
            listView.CustomSort = IecNaturalLiveMonitorSort.Instance;
        ApplyLiveSignalSearch();
    }

    private void LiveSignalSearch_TextChanged(object sender, TextChangedEventArgs e)
        => ScheduleUnifiedLiveSignalSearch();

    private void ScheduleUnifiedLiveSignalSearch()
    {
        var explorerText = ExplorerLiveSearchBox?.Text?.Trim();
        var toolbarText = _liveSignalSearchBox?.Text?.Trim();
        // Empty search is especially important during reconnect/IED switching:
        // drop the old filter immediately, not after a debounce period.
        var cleared = string.IsNullOrEmpty(explorerText) &&
                      string.IsNullOrEmpty(toolbarText);
        if (cleared ||
            _liveSignalSearchGrid?.ItemsSource is not ICollection { Count: > 2048 })
        {
            _liveSignalFilterDebounce?.Stop();
            ApplyExplorerLiveSearchFilter();
            return;
        }

        if (_liveSignalFilterDebounce is null)
        {
            _liveSignalFilterDebounce = new DispatcherTimer(
                DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(220)
            };
            _liveSignalFilterDebounce.Tick += (_, _) =>
            {
                _liveSignalFilterDebounce.Stop();
                ApplyExplorerLiveSearchFilter();
            };
        }
        // Latest text wins. Never queue one 33k-row WPF Refresh per keystroke.
        _liveSignalFilterDebounce.Stop();
        _liveSignalFilterDebounce.Start();
    }

    private void LiveSignalSearch_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_liveSignalCountRefreshScheduled) return;
        _liveSignalCountRefreshScheduled = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
            new Action(() =>
            {
                _liveSignalCountRefreshScheduled = false;
                UpdateLiveSignalSearchCount();
            }));
    }

    private void ApplyLiveSignalSearch()
    {
        _liveSignalFilterDebounce?.Stop();
        _unifiedExplorerSearchText = (ExplorerLiveSearchBox?.Text ?? "").Trim();
        var detailedQuery = (_liveSignalSearchBox?.Text ?? "").Trim();
        _unifiedLiveSearchTokens = detailedQuery.Split(
            new[] { ' ', '\t', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (_liveSignalSearchView == null)
        {
            UpdateLiveSignalSearchCount();
            return;
        }
        using var filterPerf = IedBrowserPerformanceProbe.Measure(
            "Explorer.UnifiedFilter", SelectedDevice?.Name,
            _liveSignalSearchGrid?.Items.Count ?? 0);
        if (_unifiedExplorerSearchText.Length == 0 && _unifiedLiveSearchTokens.Length == 0)
        {
            if (_liveSignalSearchView.Filter is not null)
                _liveSignalSearchView.Filter = null;
        }
        else
        {
            // Filter assignment refreshes the view; never Refresh() again.
            _liveSignalSearchView.Filter = LiveSignalSearch_Matches;
        }
        UpdateLiveSignalSearchCount();
    }

    private bool LiveSignalSearch_Matches(object item)
    {
        if (item is not Iec61850MonitorPoint point) return false;
        if (_unifiedExplorerSearchText.Length > 0 &&
            !LiveSignalSearchContains(point, _unifiedExplorerSearchText))
            return false;
        foreach (var token in _unifiedLiveSearchTokens)
            if (!LiveSignalSearchContains(point, token))
                return false;
        return true;
    }

    private static bool LiveSignalSearchContains(Iec61850MonitorPoint point, string token)
        => Contains(point.DeviceName, token) ||
           Contains(point.SignalName, token) ||
           Contains(point.IecTelegram, token) ||
           Contains(point.IecReference, token) ||
           Contains(point.DisplayValue, token) ||
           Contains(point.Quality, token) ||
           Contains(point.DeviceTimestamp, token) ||
           Contains(point.SourceMode, token) ||
           Contains(point.Category, token) ||
           Contains(point.IecDataType, token);

    private void UpdateLiveSignalSearchCount()
    {
        if (_liveSignalSearchCount is null)
            return;

        var source = _liveSignalSearchGrid?.ItemsSource;
        var total = source is ICollection collection ? collection.Count : 0;
        // Unfiltered view count equals source count; do not enumerate all SG7
        // points merely to update a small toolbar caption on each notification.
        var filtered = _unifiedExplorerSearchText.Length > 0 ||
                       _unifiedLiveSearchTokens.Length > 0;
        var visible = filtered
            ? (_liveSignalSearchView?.Cast<object>().Count() ?? total)
            : total;
        _liveSignalSearchCount.Text = filtered
            ? $"{visible:N0} of {total:N0} shown"
            : $"{total:N0} signals";
    }

    private void LiveSignalSearch_WindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            MainTabs?.SelectedIndex == 0 && _liveSignalSearchBox != null)
        {
            _liveSignalSearchBox.Focus();
            _liveSignalSearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void LiveSignalSearch_BoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _liveSignalFilterDebounce?.Stop();
            ApplyLiveSignalSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ClearLiveSignalSearch();
            e.Handled = true;
        }
    }

    private void ClearLiveSignalSearch()
    {
        if (_liveSignalSearchBox == null)
            return;
        _liveSignalSearchBox.Clear();
        _liveSignalSearchBox.Focus();
    }

    private void LiveSignalSearch_WindowClosed(object? sender, EventArgs e)
    {
        _liveSignalFilterDebounce?.Stop();
        PreviewKeyDown -= LiveSignalSearch_WindowPreviewKeyDown;
        Closed -= LiveSignalSearch_WindowClosed;
        if (_liveSignalSearchGrid != null)
            _liveSignalItemsSourceDescriptor?.RemoveValueChanged(_liveSignalSearchGrid, LiveSignalSearch_ItemsSourceChanged);
        if (_liveSignalSearchCollection != null)
            _liveSignalSearchCollection.CollectionChanged -= LiveSignalSearch_CollectionChanged;
        if (_liveSignalSearchView?.Filter != null)
            _liveSignalSearchView.Filter = null;
    }

    private static IEnumerable<T> FindLiveSignalVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
                yield return typed;
            foreach (var descendant in FindLiveSignalVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
