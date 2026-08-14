using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ArIED61850Tester.Models;

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
    private TextBlock? _liveSignalSearchPlaceholder;
    private TextBlock? _liveSignalSearchCount;
    private Button? _liveSignalSearchClearButton;
    private ICollectionView? _liveSignalSearchView;
    private INotifyCollectionChanged? _liveSignalSearchCollection;
    private DependencyPropertyDescriptor? _liveSignalItemsSourceDescriptor;
    private bool _liveSignalSearchInstalled;

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
            Height = 38
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
            Text = "",
            FontSize = 10.8,
            Foreground = new SolidColorBrush(Color.FromRgb(126, 142, 165)),
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        titlePanel.Children.Add(_liveSignalSearchCount);
        toolbar.Children.Add(titlePanel);

        var searchShell = new Border
        {
            Width = 390,
            Height = 36,
            CornerRadius = new CornerRadius(12),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(205, 217, 233)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(searchShell, 1);

        var searchGrid = new Grid();
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        var searchIcon = new Grid
        {
            Width = 15,
            Height = 15,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        searchIcon.Children.Add(new Ellipse
        {
            Width = 9,
            Height = 9,
            Stroke = new SolidColorBrush(Color.FromRgb(99, 117, 142)),
            StrokeThickness = 1.55,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        });
        searchIcon.Children.Add(new Border
        {
            Width = 6,
            Height = 1.5,
            Background = new SolidColorBrush(Color.FromRgb(99, 117, 142)),
            RenderTransform = new RotateTransform(45),
            RenderTransformOrigin = new Point(0.5, 0.5),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 1, 2)
        });
        searchGrid.Children.Add(searchIcon);

        var textHost = new Grid();
        Grid.SetColumn(textHost, 1);
        _liveSignalSearchPlaceholder = new TextBlock
        {
            Text = "Search signal, IEC reference, value…",
            FontSize = 11.6,
            Foreground = new SolidColorBrush(Color.FromRgb(144, 158, 179)),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        textHost.Children.Add(_liveSignalSearchPlaceholder);

        _liveSignalSearchBox = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            FontSize = 11.8,
            Foreground = new SolidColorBrush(Color.FromRgb(42, 57, 78)),
            VerticalContentAlignment = VerticalAlignment.Center,
            CaretBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            ToolTip = "Filter the current live workspace. Ctrl+F focuses search; Esc clears it."
        };
        _liveSignalSearchBox.TextChanged += LiveSignalSearch_TextChanged;
        _liveSignalSearchBox.PreviewKeyDown += LiveSignalSearch_BoxPreviewKeyDown;
        textHost.Children.Add(_liveSignalSearchBox);
        searchGrid.Children.Add(textHost);

        _liveSignalSearchClearButton = new Button
        {
            Content = "×",
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(104, 120, 144)),
            FontSize = 16,
            FontWeight = FontWeights.Normal,
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
            ToolTip = "Clear search"
        };
        _liveSignalSearchClearButton.Click += (_, _) => ClearLiveSignalSearch();
        Grid.SetColumn(_liveSignalSearchClearButton, 2);
        searchGrid.Children.Add(_liveSignalSearchClearButton);

        searchShell.Child = searchGrid;
        toolbar.Children.Add(searchShell);
        return toolbar;
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
        if (_liveSignalSearchView != null)
            _liveSignalSearchView.Filter = null;

        var source = _liveSignalSearchGrid?.ItemsSource;
        _liveSignalSearchCollection = source as INotifyCollectionChanged;
        if (_liveSignalSearchCollection != null)
            _liveSignalSearchCollection.CollectionChanged += LiveSignalSearch_CollectionChanged;

        _liveSignalSearchView = source == null
            ? null
            : CollectionViewSource.GetDefaultView(source);
        ApplyLiveSignalSearch();
    }

    private void LiveSignalSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_liveSignalSearchPlaceholder != null)
            _liveSignalSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(_liveSignalSearchBox?.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        if (_liveSignalSearchClearButton != null)
            _liveSignalSearchClearButton.Visibility = string.IsNullOrWhiteSpace(_liveSignalSearchBox?.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        ApplyLiveSignalSearch();
    }

    private void LiveSignalSearch_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.BeginInvoke(new Action(UpdateLiveSignalSearchCount));

    private void ApplyLiveSignalSearch()
    {
        if (_liveSignalSearchView == null)
        {
            UpdateLiveSignalSearchCount();
            return;
        }

        _liveSignalSearchView.Filter = LiveSignalSearch_Matches;
        _liveSignalSearchView.Refresh();
        UpdateLiveSignalSearchCount();
    }

    private bool LiveSignalSearch_Matches(object item)
    {
        var query = (_liveSignalSearchBox?.Text ?? string.Empty).Trim();
        if (query.Length == 0)
            return true;
        if (item is not Iec61850MonitorPoint point)
            return true;

        var searchable = string.Join('\n', new[]
        {
            point.DeviceName,
            point.SignalName,
            point.IecTelegram,
            point.IecReference,
            point.Value,
            point.Quality,
            point.DeviceTimestamp,
            point.SourceMode
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        // Multiple terms use AND semantics: "MMXU instCVal" quickly narrows large workspaces.
        var tokens = query.Split(
            new[] { ' ', '\t', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.All(token => searchable.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateLiveSignalSearchCount()
    {
        if (_liveSignalSearchCount == null)
            return;

        var source = _liveSignalSearchGrid?.ItemsSource;
        var total = source switch
        {
            ICollection collection => collection.Count,
            IEnumerable<object> enumerable => enumerable.Count(),
            _ => 0
        };
        var visible = _liveSignalSearchView?.Cast<object>().Count() ?? total;
        var filtered = !string.IsNullOrWhiteSpace(_liveSignalSearchBox?.Text);
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
        if (e.Key != Key.Escape)
            return;
        ClearLiveSignalSearch();
        e.Handled = true;
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
        PreviewKeyDown -= LiveSignalSearch_WindowPreviewKeyDown;
        Closed -= LiveSignalSearch_WindowClosed;
        if (_liveSignalSearchGrid != null)
            _liveSignalItemsSourceDescriptor?.RemoveValueChanged(_liveSignalSearchGrid, LiveSignalSearch_ItemsSourceChanged);
        if (_liveSignalSearchCollection != null)
            _liveSignalSearchCollection.CollectionChanged -= LiveSignalSearch_CollectionChanged;
        if (_liveSignalSearchView != null)
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
