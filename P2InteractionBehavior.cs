using System.Collections;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Presentation-only P2 interaction layer.
///
/// Keeps the existing IEC 61850 collections, acquisition paths and filters intact while
/// making dense engineering workspaces easier to scan: virtualized pixel scrolling,
/// accumulated/eased mouse-wheel motion, an IED finder, keyboard find shortcuts, and
/// next/previous navigation through the already-filtered live-value grids.
/// </summary>
internal static class P2InteractionBehavior
{
    private sealed class WindowState
    {
        public TextBox? IedFinderBox { get; set; }
        public ListBox? IedList { get; set; }
        public ICollectionView? IedView { get; set; }
        public Predicate<object>? PreviousIedFilter { get; set; }
        public TextBlock? IedResultText { get; set; }
        public DispatcherTimer? IedFilterTimer { get; set; }
        public TextBox? ActiveSearchBox { get; set; }
    }

    private sealed class SmoothScrollState
    {
        public required ScrollViewer Viewer { get; init; }
        public required DispatcherTimer Timer { get; init; }
        public double TargetOffset { get; set; }
    }

    private sealed class SearchBoxState
    {
        public required TextBox Box { get; init; }
        public required DispatcherTimer CountTimer { get; init; }
        public TextBlock? ResultText { get; set; }
    }

    private sealed class Marker { }

    private static readonly ConditionalWeakTable<MainWindow, WindowState> Windows = new();
    private static readonly ConditionalWeakTable<ScrollViewer, SmoothScrollState> SmoothScrollers = new();
    private static readonly ConditionalWeakTable<TextBox, SearchBoxState> SearchBoxes = new();
    private static readonly ConditionalWeakTable<ItemsControl, Marker> PixelScrollItems = new();
    private static int _installed;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MainWindow_Loaded),
            true);

        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            Mouse.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(MainWindow_PreviewMouseWheel),
            true);

        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(MainWindow_PreviewKeyDown),
            true);

        EventManager.RegisterClassHandler(
            typeof(ItemsControl),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ItemsControl_Loaded),
            true);

        EventManager.RegisterClassHandler(
            typeof(TextBox),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(TextBox_Loaded),
            true);
    }

    // ---------------------------------------------------------------------
    // Smooth virtualized scrolling
    // ---------------------------------------------------------------------

    private static void ItemsControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ItemsControl items || Window.GetWindow(items) is not MainWindow)
            return;
        if (PixelScrollItems.TryGetValue(items, out _))
            return;

        PixelScrollItems.Add(items, new Marker());

        // Pixel scroll keeps motion visually continuous while WPF virtualization remains on.
        // Do not flip CanContentScroll to false: that would disable logical virtualization on
        // very large relay models and make the UI heavier exactly where smoothness matters.
        VirtualizingPanel.SetIsVirtualizing(items, true);
        VirtualizingPanel.SetVirtualizationMode(items, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(items, ScrollUnit.Pixel);
        ScrollViewer.SetCanContentScroll(items, true);
        ScrollViewer.SetIsDeferredScrollingEnabled(items, false);
        ScrollViewer.SetPanningMode(items, PanningMode.VerticalFirst);
    }

    private static void MainWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not MainWindow || e.OriginalSource is not DependencyObject source)
            return;

        // Preserve platform conventions such as Ctrl+wheel and Shift+wheel.
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != ModifierKeys.None)
            return;
        if (FindAncestor<ScrollBar>(source) != null)
            return;
        if (FindAncestor<ComboBox>(source) is { IsDropDownOpen: true })
            return;
        if (FindAncestor<TextBox>(source) is { AcceptsReturn: true })
            return;

        var viewer = FindScrollableViewer(source, e.Delta);
        if (viewer == null || viewer.ScrollableHeight <= 0.5)
            return;

        var state = SmoothScrollers.GetValue(viewer, CreateSmoothScrollState);
        if (!state.Timer.IsEnabled || Math.Abs(state.TargetOffset - viewer.VerticalOffset) > Math.Max(240d, viewer.ViewportHeight * 1.5d))
            state.TargetOffset = viewer.VerticalOffset;

        var wheelLines = SystemParameters.WheelScrollLines;
        var distancePerNotch = wheelLines < 0
            ? Math.Max(96d, viewer.ViewportHeight * 0.82d)
            : Math.Clamp(wheelLines, 1, 6) * 30d;

        // High-resolution touchpads often send deltas below 120. Scale proportionally
        // instead of quantizing to full wheel notches so those devices remain precise.
        var deltaPixels = -(e.Delta / 120d) * distancePerNotch;
        var nextTarget = Math.Clamp(state.TargetOffset + deltaPixels, 0d, viewer.ScrollableHeight);
        if (Math.Abs(nextTarget - state.TargetOffset) < 0.1d)
            return;

        state.TargetOffset = nextTarget;
        if (!state.Timer.IsEnabled)
            state.Timer.Start();
        e.Handled = true;
    }

    private static SmoothScrollState CreateSmoothScrollState(ScrollViewer viewer)
    {
        SmoothScrollState? state = null;
        var timer = new DispatcherTimer(DispatcherPriority.Render, viewer.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        state = new SmoothScrollState
        {
            Viewer = viewer,
            Timer = timer,
            TargetOffset = viewer.VerticalOffset
        };

        timer.Tick += (_, _) =>
        {
            if (!viewer.IsVisible || viewer.ScrollableHeight <= 0.5d)
            {
                timer.Stop();
                state.TargetOffset = viewer.VerticalOffset;
                return;
            }

            state.TargetOffset = Math.Clamp(state.TargetOffset, 0d, viewer.ScrollableHeight);
            var remaining = state.TargetOffset - viewer.VerticalOffset;
            if (Math.Abs(remaining) <= 0.45d)
            {
                viewer.ScrollToVerticalOffset(state.TargetOffset);
                timer.Stop();
                return;
            }

            // Critically damped-feeling ease: quick response at the start, calm landing.
            // The bounded minimum step prevents tiny offsets from visibly stalling.
            var step = remaining * 0.24d;
            if (Math.Abs(step) < 0.7d)
                step = Math.CopySign(0.7d, remaining);
            if (Math.Abs(step) > Math.Abs(remaining))
                step = remaining;

            viewer.ScrollToVerticalOffset(Math.Clamp(viewer.VerticalOffset + step, 0d, viewer.ScrollableHeight));
        };

        viewer.Unloaded += (_, _) => timer.Stop();
        return state;
    }

    private static ScrollViewer? FindScrollableViewer(DependencyObject source, int wheelDelta)
    {
        for (DependencyObject? current = source; current != null; current = GetParent(current))
        {
            if (current is not ScrollViewer viewer || viewer.ScrollableHeight <= 0.5d)
                continue;

            var canMove = wheelDelta < 0
                ? viewer.VerticalOffset < viewer.ScrollableHeight - 0.5d
                : viewer.VerticalOffset > 0.5d;
            if (canMove)
                return viewer;
        }

        return null;
    }

    // ---------------------------------------------------------------------
    // IED quick finder
    // ---------------------------------------------------------------------

    private static void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow owner)
            return;

        owner.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => TryInstallIedFinder(owner)));
    }

    private static void TryInstallIedFinder(MainWindow owner)
    {
        var state = Windows.GetValue(owner, _ => new WindowState());
        if (state.IedFinderBox != null)
            return;

        var list = FindVisualChildren<ListBox>(owner)
            .FirstOrDefault(candidate =>
                GetBindingPath(candidate, ItemsControl.ItemsSourceProperty)
                    .Equals("Devices", StringComparison.Ordinal));
        if (list?.Parent is not DockPanel dock)
            return;

        var searchHost = BuildIedFinder(owner, list, state);
        var listIndex = dock.Children.IndexOf(list);
        DockPanel.SetDock(searchHost, Dock.Top);
        dock.Children.Insert(Math.Max(0, listIndex), searchHost);
    }

    private static Border BuildIedFinder(MainWindow owner, ListBox list, WindowState state)
    {
        var view = CollectionViewSource.GetDefaultView(owner.Devices);
        var previousFilter = view.CanFilter ? view.Filter : null;

        var box = new TextBox
        {
            Tag = "P2IedFinder",
            Height = 34,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(38, 52, 69)),
            CaretBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            FontSize = 11.5,
            Padding = new Thickness(0, 0, 74, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            FocusVisualStyle = null,
            ToolTip = "Find IED by name, IP, endpoint or status • Ctrl+K / Ctrl+Shift+F"
        };

        var placeholder = new TextBlock
        {
            Text = "Find IED — name, IP, endpoint or status",
            Foreground = new SolidColorBrush(Color.FromRgb(139, 152, 168)),
            FontSize = 10.8,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Margin = new Thickness(0, 0, 70, 0)
        };

        var result = new TextBlock
        {
            Text = $"{owner.Devices.Count:N0} IED",
            Foreground = new SolidColorBrush(Color.FromRgb(96, 112, 134)),
            FontSize = 9.9,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        var clear = new Button
        {
            Content = "×",
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(96, 112, 134)),
            FontSize = 16,
            Cursor = Cursors.Hand,
            FocusVisualStyle = null,
            ToolTip = "Clear IED search",
            Visibility = Visibility.Collapsed
        };

        var searchGrid = new Grid();
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M10.5,3.5 A7,7 0 1 1 10.4,3.5 M15.5,15.5 L21,21"),
            Width = 14,
            Height = 14,
            Stretch = Stretch.Uniform,
            Stroke = new SolidColorBrush(Color.FromRgb(115, 131, 152)),
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(icon, 0);
        searchGrid.Children.Add(icon);

        var inputLayer = new Grid();
        inputLayer.Children.Add(box);
        inputLayer.Children.Add(placeholder);
        Grid.SetColumn(inputLayer, 1);
        searchGrid.Children.Add(inputLayer);

        Grid.SetColumn(result, 2);
        searchGrid.Children.Add(result);
        Grid.SetColumn(clear, 3);
        searchGrid.Children.Add(clear);

        var host = new Border
        {
            Tag = "P2IedFinderHost",
            Height = 36,
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(5, 0, 6, 0),
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(200, 210, 222)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Child = searchGrid
        };

        var timer = new DispatcherTimer(DispatcherPriority.Background, owner.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(110)
        };

        state.IedFinderBox = box;
        state.IedList = list;
        state.IedView = view;
        state.PreviousIedFilter = previousFilter;
        state.IedResultText = result;
        state.IedFilterTimer = timer;

        if (view.CanFilter)
        {
            view.Filter = item =>
                (state.PreviousIedFilter?.Invoke(item) ?? true) &&
                (item is not Iec61850MonitorDevice device || IedMatches(device, box.Text));
        }

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (view.CanFilter)
                view.Refresh();
            UpdateIedFinderResult(state);
        };

        box.TextChanged += (_, _) =>
        {
            placeholder.Visibility = string.IsNullOrWhiteSpace(box.Text) ? Visibility.Visible : Visibility.Collapsed;
            clear.Visibility = string.IsNullOrWhiteSpace(box.Text) ? Visibility.Collapsed : Visibility.Visible;
            timer.Stop();
            timer.Start();
        };
        box.GotKeyboardFocus += (_, _) => state.ActiveSearchBox = box;
        box.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                box.Clear();
                args.Handled = true;
            }
            else if (args.Key == Key.Enter)
            {
                timer.Stop();
                if (view.CanFilter)
                    view.Refresh();
                UpdateIedFinderResult(state);
                CycleIedFinder(state, reverse: (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
                args.Handled = true;
            }
        };
        clear.Click += (_, _) =>
        {
            box.Clear();
            box.Focus();
        };
        host.PreviewMouseLeftButtonDown += (_, _) => box.Focus();

        return host;
    }

    private static bool IedMatches(Iec61850MonitorDevice device, string? query)
    {
        var tokens = Tokenize(query);
        if (tokens.Length == 0)
            return true;

        var searchable = string.Join(" ", new[]
        {
            device.Name,
            device.IpAddress,
            device.EndpointText,
            device.Status,
            device.IdentitySource,
            device.LogicalDeviceSummary,
            device.AcquisitionMode,
            device.SclIedName,
            device.SclAccessPointName
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return tokens.All(token => searchable.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static void UpdateIedFinderResult(WindowState state)
    {
        if (state.IedView == null || state.IedResultText == null)
            return;

        var count = state.IedView.Cast<object>().Count();
        state.IedResultText.Text = string.IsNullOrWhiteSpace(state.IedFinderBox?.Text)
            ? $"{count:N0} IED"
            : count == 1 ? "1 match" : $"{count:N0} matches";
        state.IedResultText.Foreground = new SolidColorBrush(
            count == 0 ? Color.FromRgb(180, 35, 24) : Color.FromRgb(96, 112, 134));
    }

    private static void CycleIedFinder(WindowState state, bool reverse)
    {
        if (state.IedList == null || state.IedView == null)
            return;

        var items = state.IedView.Cast<object>().ToArray();
        if (items.Length == 0)
            return;

        var current = Array.IndexOf(items, state.IedList.SelectedItem);
        var next = reverse
            ? (current <= 0 ? items.Length - 1 : current - 1)
            : (current < 0 || current >= items.Length - 1 ? 0 : current + 1);

        state.IedList.SelectedItem = items[next];
        state.IedList.ScrollIntoView(items[next]);
    }

    // ---------------------------------------------------------------------
    // Existing live-search boxes: result awareness + next/previous navigation
    // ---------------------------------------------------------------------

    private static void TextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || Window.GetWindow(box) is not MainWindow owner)
            return;
        if (!box.Name.Equals("ExplorerLiveSearchBox", StringComparison.Ordinal) &&
            !box.Name.Equals("GlobalLiveSearchBox", StringComparison.Ordinal))
        {
            return;
        }
        if (SearchBoxes.TryGetValue(box, out _))
            return;

        var timer = new DispatcherTimer(DispatcherPriority.Background, owner.Dispatcher)
        {
            // Global rapid filtering already debounces at 160 ms. Count after that filter
            // settles rather than forcing a second immediate CollectionView refresh.
            Interval = TimeSpan.FromMilliseconds(210)
        };
        var state = new SearchBoxState { Box = box, CountTimer = timer };
        SearchBoxes.Add(box, state);

        state.ResultText = AddSearchResultBadge(box);
        box.ToolTip = $"{box.ToolTip} • Enter next • Shift+Enter previous • Esc clear • Ctrl+F focus";
        box.GotKeyboardFocus += (_, _) => Windows.GetValue(owner, _ => new WindowState()).ActiveSearchBox = box;
        box.TextChanged += (_, _) =>
        {
            timer.Stop();
            timer.Start();
        };
        box.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                box.Clear();
                args.Handled = true;
            }
            else if (args.Key == Key.Enter)
            {
                CycleLiveSearchResult(owner, box, reverse: (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
                args.Handled = true;
            }
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            UpdateLiveSearchResult(owner, state);
        };
    }

    private static TextBlock? AddSearchResultBadge(TextBox box)
    {
        if (box.Parent is not Grid host)
            return null;

        // Reserve a small right-side lane only while a query exists. The badge is
        // intentionally text-only so it does not compete with live process values.
        var result = new TextBlock
        {
            Text = string.Empty,
            FontSize = 9.9,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(96, 112, 134)),
            Background = new SolidColorBrush(Color.FromArgb(236, 248, 250, 252)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 2, 0),
            Padding = new Thickness(5, 1, 3, 1),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

        host.Children.Add(result);
        box.Padding = new Thickness(box.Padding.Left, box.Padding.Top, Math.Max(box.Padding.Right, 76d), box.Padding.Bottom);
        return result;
    }

    private static void UpdateLiveSearchResult(MainWindow owner, SearchBoxState state)
    {
        if (state.ResultText == null)
            return;

        if (string.IsNullOrWhiteSpace(state.Box.Text))
        {
            state.ResultText.Visibility = Visibility.Collapsed;
            return;
        }

        var grid = ResolveSearchGrid(owner, state.Box);
        if (grid == null)
            return;

        var count = VisibleGridItems(grid).Count;
        state.ResultText.Text = count == 1 ? "1 match" : $"{count:N0} matches";
        state.ResultText.Foreground = new SolidColorBrush(
            count == 0 ? Color.FromRgb(180, 35, 24) : Color.FromRgb(96, 112, 134));
        state.ResultText.Visibility = Visibility.Visible;
    }

    private static void CycleLiveSearchResult(MainWindow owner, TextBox box, bool reverse)
    {
        var grid = ResolveSearchGrid(owner, box);
        if (grid == null)
            return;

        var items = VisibleGridItems(grid);
        if (items.Count == 0)
            return;

        var current = items.IndexOf(grid.SelectedItem);
        var next = reverse
            ? (current <= 0 ? items.Count - 1 : current - 1)
            : (current < 0 || current >= items.Count - 1 ? 0 : current + 1);

        var item = items[next];
        grid.SelectedItem = item;
        grid.ScrollIntoView(item);
        grid.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                if (grid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
                    row.BringIntoView();
                box.Focus();
                box.CaretIndex = box.Text?.Length ?? 0;
            }));
    }

    private static DataGrid? ResolveSearchGrid(MainWindow owner, TextBox box)
    {
        if (box.Name.Equals("GlobalLiveSearchBox", StringComparison.Ordinal))
            return owner.FindName("GlobalLiveGrid") as DataGrid;

        return FindVisualChildren<DataGrid>(owner)
            .FirstOrDefault(grid =>
                GetBindingPath(grid, ItemsControl.ItemsSourceProperty)
                    .Equals("SelectedDevice.Points", StringComparison.Ordinal));
    }

    private static List<object> VisibleGridItems(DataGrid grid)
        => grid.Items.Cast<object>()
            .Where(item => !ReferenceEquals(item, CollectionView.NewItemPlaceholder))
            .ToList();

    // ---------------------------------------------------------------------
    // Keyboard fast-find shortcuts
    // ---------------------------------------------------------------------

    private static void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not MainWindow owner)
            return;

        var modifiers = Keyboard.Modifiers;
        if ((modifiers & ModifierKeys.Control) != 0 && e.Key == Key.K)
        {
            FocusIedFinder(owner, switchToExplorer: true);
            e.Handled = true;
            return;
        }

        if ((modifiers & ModifierKeys.Control) != 0 && e.Key == Key.F)
        {
            if ((modifiers & ModifierKeys.Shift) != 0)
            {
                FocusIedFinder(owner, switchToExplorer: true);
                e.Handled = true;
                return;
            }

            TextBox? target = owner.MainTabs.SelectedIndex switch
            {
                0 => owner.FindName("ExplorerLiveSearchBox") as TextBox,
                1 => owner.FindName("GlobalLiveSearchBox") as TextBox,
                _ => Windows.GetValue(owner, _ => new WindowState()).ActiveSearchBox
            };

            if (target != null)
            {
                target.Focus();
                target.SelectAll();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.F3)
        {
            var state = Windows.GetValue(owner, _ => new WindowState());
            var target = state.ActiveSearchBox;
            if (target == state.IedFinderBox)
                CycleIedFinder(state, reverse: (modifiers & ModifierKeys.Shift) != 0);
            else if (target != null)
                CycleLiveSearchResult(owner, target, reverse: (modifiers & ModifierKeys.Shift) != 0);
            else
                return;

            e.Handled = true;
        }
    }

    private static void FocusIedFinder(MainWindow owner, bool switchToExplorer)
    {
        var state = Windows.GetValue(owner, _ => new WindowState());
        if (switchToExplorer && owner.MainTabs.SelectedIndex != 0)
            owner.MainTabs.SelectedIndex = 0;

        owner.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                TryInstallIedFinder(owner);
                if (state.IedFinderBox == null)
                    return;
                state.IedFinderBox.Focus();
                state.IedFinderBox.SelectAll();
                state.ActiveSearchBox = state.IedFinderBox;
            }));
    }

    private static string[] Tokenize(string? text)
        => (text ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string GetBindingPath(DependencyObject target, DependencyProperty property)
        => BindingOperations.GetBindingExpression(target, property)?.ParentBinding.Path?.Path ?? string.Empty;

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        for (var item = current; item != null; item = GetParent(item))
        {
            if (item is T match)
                return match;
        }
        return null;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        try
        {
            return VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }
        catch (InvalidOperationException)
        {
            return LogicalTreeHelper.GetParent(current);
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
