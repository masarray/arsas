using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Shared dense-grid behavior for commissioning workspaces. It keeps DataGrid
/// selection flat, removes redundant command-row chrome, and adds a virtualized
/// per-column rapid filter to the global live monitor.
/// </summary>
internal static class GridUxBehavior
{
    private sealed class FilterWatermarkState
    {
        public object? OriginalToolTip { get; set; }
    }

    private sealed class CommandGridState
    {
        public bool LayoutHooked { get; set; }
    }

    private sealed class GlobalRapidFilterState
    {
        public required DataGrid Grid { get; init; }
        public required ICollectionView View { get; init; }
        public required ScrollViewer FilterScrollViewer { get; init; }
        public required Grid FilterGrid { get; init; }
        public required DispatcherTimer RefreshTimer { get; init; }
        public Dictionary<string, string> Filters { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ScrollViewer? GridScrollViewer { get; set; }
        public bool UpdatingWidths { get; set; }
    }

    private static readonly ConditionalWeakTable<TextBox, FilterWatermarkState> FilterWatermarks = new();
    private static readonly ConditionalWeakTable<DataGrid, CommandGridState> CommandGrids = new();
    private static readonly ConditionalWeakTable<DataGrid, GlobalRapidFilterState> GlobalFilterGrids = new();
    private static int _installed;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        EventManager.RegisterClassHandler(
            typeof(DataGridRow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(DataGridRow_Loaded),
            true);

        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(DataGrid_Loaded),
            true);

        EventManager.RegisterClassHandler(
            typeof(TextBox),
            Keyboard.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(FilterTextBox_GotKeyboardFocus),
            true);

        EventManager.RegisterClassHandler(
            typeof(TextBox),
            Keyboard.LostKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(FilterTextBox_LostKeyboardFocus),
            true);
    }

    private static void DataGridRow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGridRow row)
            return;

        row.Margin = new Thickness(0);
        row.ApplyTemplate();

        if (row.Template.FindName("RowBorder", row) is Border namedBorder)
        {
            namedBorder.CornerRadius = new CornerRadius(0);
            namedBorder.Margin = new Thickness(0);
            return;
        }

        var firstBorder = FindVisualChildren<Border>(row).FirstOrDefault();
        if (firstBorder != null)
        {
            firstBorder.CornerRadius = new CornerRadius(0);
            firstBorder.Margin = new Thickness(0);
        }
    }

    private static void DataGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid)
            return;

        var headers = grid.Columns
            .Select(column => column.Header?.ToString() ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (headers.Contains("Control model") && headers.Contains("CDC / Type") && headers.Contains("Control"))
        {
            InstallCommandGridCleanup(grid);
            return;
        }

        if (headers.SetEquals(new[]
            {
                "IED", "Signal", "IEC Telegram", "Value", "Quality", "IED Timestamp", "Acquisition"
            }))
        {
            InstallGlobalRapidFilter(grid);
        }
    }

    private static void InstallCommandGridCleanup(DataGrid grid)
    {
        var state = CommandGrids.GetValue(grid, _ => new CommandGridState());
        if (state.LayoutHooked)
            return;

        state.LayoutHooked = true;
        grid.LayoutUpdated += (_, _) => ApplyCommandGridCleanup(grid);
        ApplyCommandGridCleanup(grid);
    }

    private static void ApplyCommandGridCleanup(DataGrid grid)
    {
        // The previous 60 px row was only needed for the second-line result text.
        // The simplified panel is a single-line operating grid.
        if (Math.Abs(grid.RowHeight - 44d) > 0.1)
            grid.RowHeight = 44d;
        if (Math.Abs(grid.MinRowHeight - 42d) > 0.1)
            grid.MinRowHeight = 42d;

        foreach (var button in FindVisualChildren<Button>(grid).ToArray())
        {
            var content = ExtractButtonText(button);
            if (content.Equals("Details", StringComparison.OrdinalIgnoreCase))
            {
                button.Visibility = Visibility.Collapsed;
                button.IsHitTestVisible = false;
                continue;
            }

            if (!content.Equals("Technical details", StringComparison.OrdinalIgnoreCase) &&
                !content.Equals("Not available", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (button.Parent is not Panel parent)
            {
                button.Visibility = Visibility.Collapsed;
                button.IsHitTestVisible = false;
                continue;
            }

            var signal = button.DataContext as SignalDefinition;
            var unavailableLabel = parent.Children
                .OfType<TextBlock>()
                .FirstOrDefault(text => Equals(text.Tag, "CommandUnavailableLabel"));

            if (unavailableLabel == null)
            {
                unavailableLabel = new TextBlock
                {
                    Tag = "CommandUnavailableLabel",
                    Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                parent.Children.Add(unavailableLabel);
            }

            unavailableLabel.Text = signal?.ControlModelResolved == true
                ? "Not available"
                : "Reading…";
            parent.Children.Remove(button);
        }

        foreach (var textBlock in FindVisualChildren<TextBlock>(grid))
        {
            var expression = BindingOperations.GetBindingExpression(textBlock, TextBlock.TextProperty);
            if (expression?.ParentBinding.Path?.Path == nameof(SignalDefinition.ControlLastResult))
                textBlock.Visibility = Visibility.Collapsed;
        }
    }

    private static string ExtractButtonText(Button button)
        => button.Content switch
        {
            string text => text.Trim(),
            TextBlock textBlock => textBlock.Text?.Trim() ?? string.Empty,
            _ => button.Content?.ToString()?.Trim() ?? string.Empty
        };

    private static void FilterTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox textBox || !IsSelectionWizardColumnFilter(textBox))
            return;

        var state = FilterWatermarks.GetValue(textBox, _ => new FilterWatermarkState());
        state.OriginalToolTip ??= textBox.ToolTip;
        textBox.ToolTip = string.Empty;
    }

    private static void FilterTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox textBox || !IsSelectionWizardColumnFilter(textBox))
            return;

        if (FilterWatermarks.TryGetValue(textBox, out var state))
            textBox.ToolTip = state.OriginalToolTip ?? "Filter…";
    }

    private static bool IsSelectionWizardColumnFilter(TextBox textBox)
    {
        if (textBox.Tag is not string key || string.IsNullOrWhiteSpace(key))
            return false;
        if (Window.GetWindow(textBox) is not SignalSelectionWizardWindow)
            return false;

        return textBox.ToolTip?.ToString()?.Contains("Filter", StringComparison.OrdinalIgnoreCase) == true ||
               FilterWatermarks.TryGetValue(textBox, out _);
    }

    private static void InstallGlobalRapidFilter(DataGrid grid)
    {
        if (GlobalFilterGrids.TryGetValue(grid, out _))
            return;
        if (Window.GetWindow(grid) is not MainWindow owner)
            return;
        if (grid.Parent is not DockPanel parent)
            return;

        var view = CollectionViewSource.GetDefaultView(owner.GlobalPoints);
        var filterGrid = new Grid
        {
            Background = Brushes.White,
            SnapsToDevicePixels = true
        };

        for (var i = 0; i < grid.Columns.Count; i++)
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = ToGridLength(grid.Columns[i].Width) });

        var filterScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            Content = filterGrid,
            Focusable = false
        };

        var host = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 227, 236)),
            BorderThickness = new Thickness(1, 0, 1, 1),
            CornerRadius = new CornerRadius(0),
            Height = 37,
            ClipToBounds = true,
            Child = filterScroll
        };
        DockPanel.SetDock(host, Dock.Top);

        var refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(160)
        };

        var state = new GlobalRapidFilterState
        {
            Grid = grid,
            View = view,
            FilterScrollViewer = filterScroll,
            FilterGrid = filterGrid,
            RefreshTimer = refreshTimer
        };
        GlobalFilterGrids.Add(grid, state);

        refreshTimer.Tick += (_, _) =>
        {
            refreshTimer.Stop();
            state.View.Refresh();
        };
        state.View.Filter = item => FilterGlobalPoint(item, state.Filters);

        var fields = new[]
        {
            ("IED", "Filter IED…"),
            ("Signal", "Filter signal…"),
            ("Telegram", "Filter IEC Telegram…"),
            ("Value", "Filter value…"),
            ("Quality", "Filter quality…"),
            ("Timestamp", "Filter timestamp…"),
            ("Acquisition", "Filter acquisition…")
        };

        for (var i = 0; i < fields.Length; i++)
        {
            var cell = BuildRapidFilterCell(state, fields[i].Item1, fields[i].Item2);
            Grid.SetColumn(cell, i);
            filterGrid.Children.Add(cell);
        }

        // Preserve the existing title area, then place the rapid-filter row directly
        // above the virtualized signal grid.
        parent.Children.Remove(grid);
        parent.Children.Add(host);
        parent.Children.Add(grid);
        grid.ItemsSource = view;

        grid.LayoutUpdated += (_, _) =>
        {
            UpdateRapidFilterWidths(state);
            AttachGridScrollSync(state);
        };

        UpdateRapidFilterWidths(state);
        AttachGridScrollSync(state);
    }

    private static Border BuildRapidFilterCell(GlobalRapidFilterState state, string key, string placeholder)
    {
        var box = new TextBox
        {
            Tag = key,
            Height = 35,
            Padding = new Thickness(9, 0, 7, 0),
            FontSize = 12.2,
            Foreground = new SolidColorBrush(Color.FromRgb(29, 41, 57)),
            CaretBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            Background = Brushes.White,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FocusVisualStyle = null,
            Template = BuildSquareTextBoxTemplate()
        };

        var watermark = new TextBlock
        {
            Text = placeholder,
            Foreground = new SolidColorBrush(Color.FromRgb(152, 162, 179)),
            FontSize = 12.1,
            Margin = new Thickness(9, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        void UpdateWatermark()
            => watermark.Visibility = string.IsNullOrEmpty(box.Text) && !box.IsKeyboardFocused
                ? Visibility.Visible
                : Visibility.Collapsed;

        box.TextChanged += (_, _) =>
        {
            state.Filters[key] = box.Text ?? string.Empty;
            UpdateWatermark();
            state.RefreshTimer.Stop();
            state.RefreshTimer.Start();
        };
        box.GotKeyboardFocus += (_, _) => UpdateWatermark();
        box.LostKeyboardFocus += (_, _) => UpdateWatermark();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                box.Clear();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                state.RefreshTimer.Stop();
                state.View.Refresh();
                state.Grid.Focus();
                e.Handled = true;
            }
        };

        var content = new Grid();
        content.Children.Add(box);
        content.Children.Add(watermark);

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 227, 236)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            CornerRadius = new CornerRadius(0),
            Child = content
        };
    }

    private static ControlTemplate BuildSquareTextBoxTemplate()
    {
#pragma warning disable CS0618
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(Control.BorderBrush))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        border.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(Control.BorderThickness))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(0));

        var host = new FrameworkElementFactory(typeof(ScrollViewer));
        host.SetValue(FrameworkElement.NameProperty, "PART_ContentHost");
        host.SetBinding(FrameworkElement.MarginProperty, new Binding(nameof(Control.Padding))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
        });
        border.AppendChild(host);

        return new ControlTemplate(typeof(TextBox)) { VisualTree = border };
#pragma warning restore CS0618
    }

    private static bool FilterGlobalPoint(object item, IReadOnlyDictionary<string, string> filters)
    {
        if (item is not Iec61850MonitorPoint point)
            return false;

        foreach (var (key, rawFilter) in filters)
        {
            var tokens = Tokenize(rawFilter);
            if (tokens.Length == 0)
                continue;

            var field = key switch
            {
                "IED" => point.DeviceName,
                "Signal" => point.SignalName,
                "Telegram" => point.IecTelegram,
                "Value" => point.Value,
                "Quality" => point.Quality,
                "Timestamp" => point.DeviceTimestamp,
                "Acquisition" => point.SourceMode,
                _ => string.Empty
            };

            if (!tokens.All(token => (field ?? string.Empty).Contains(token, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        return true;
    }

    private static string[] Tokenize(string? text)
        => Regex.Matches(text ?? string.Empty, @"[^\s]+")
            .Select(match => match.Value.Trim())
            .Where(token => token.Length > 0)
            .ToArray();

    private static void UpdateRapidFilterWidths(GlobalRapidFilterState state)
    {
        if (state.UpdatingWidths || state.FilterGrid.ColumnDefinitions.Count != state.Grid.Columns.Count)
            return;

        state.UpdatingWidths = true;
        try
        {
            var total = 0d;
            for (var i = 0; i < state.Grid.Columns.Count; i++)
            {
                var width = Math.Max(20d, state.Grid.Columns[i].ActualWidth);
                total += width;
                var current = state.FilterGrid.ColumnDefinitions[i].Width;
                if (!current.IsAbsolute || Math.Abs(current.Value - width) > 0.5)
                    state.FilterGrid.ColumnDefinitions[i].Width = new GridLength(width);
            }
            state.FilterGrid.Width = total;
        }
        finally
        {
            state.UpdatingWidths = false;
        }
    }

    private static void AttachGridScrollSync(GlobalRapidFilterState state)
    {
        if (state.GridScrollViewer != null)
            return;

        var viewer = FindVisualChildren<ScrollViewer>(state.Grid)
            .FirstOrDefault(candidate => candidate.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled);
        if (viewer == null)
            return;

        state.GridScrollViewer = viewer;
        viewer.ScrollChanged += (_, e) =>
        {
            if (Math.Abs(e.HorizontalChange) > 0.001)
                state.FilterScrollViewer.ScrollToHorizontalOffset(e.HorizontalOffset);
        };
        state.FilterScrollViewer.ScrollToHorizontalOffset(viewer.HorizontalOffset);
    }

    private static GridLength ToGridLength(DataGridLength value)
    {
        if (value.IsAbsolute)
            return new GridLength(value.Value);
        if (value.IsStar)
            return new GridLength(value.Value, GridUnitType.Star);
        return GridLength.Auto;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
