using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Direct visual-tree UX behavior for the dense IEC 61850 workspaces.
///
/// The previous implementation depended on individual DataGrid Loaded events and
/// attempted to insert a separate filter host beside the grid. That was too fragile
/// for hidden TabItem content and virtualized command rows. This implementation hooks
/// the MainWindow itself, configures the actual column headers, and attaches directly
/// to the generators that create IED cards and command rows.
/// </summary>
internal static class GridUxBehavior
{
    private sealed class Marker { }

    private sealed class FilterWatermarkState
    {
        public object? OriginalToolTip { get; set; }
    }

    private sealed class MainWindowState
    {
        public DispatcherTimer? RetryTimer { get; set; }
        public bool IedListInstalled { get; set; }
        public bool CommandGridInstalled { get; set; }
        public bool GlobalGridInstalled { get; set; }
    }

    private sealed class GlobalRapidFilterState
    {
        public required DataGrid Grid { get; init; }
        public required ICollectionView View { get; init; }
        public required DispatcherTimer RefreshTimer { get; init; }
        public Dictionary<string, string> Filters { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly ConditionalWeakTable<MainWindow, MainWindowState> MainWindows = new();
    private static readonly ConditionalWeakTable<ListBox, Marker> IedLists = new();
    private static readonly ConditionalWeakTable<DataGrid, Marker> CommandGrids = new();
    private static readonly ConditionalWeakTable<DataGrid, GlobalRapidFilterState> GlobalGrids = new();
    private static readonly ConditionalWeakTable<TextBox, FilterWatermarkState> FilterWatermarks = new();
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
            typeof(DataGridRow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(DataGridRow_Loaded),
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

    private static void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow owner)
            return;

        var state = MainWindows.GetValue(owner, _ => new MainWindowState());
        if (state.RetryTimer != null)
            return;

        state.RetryTimer = new DispatcherTimer(DispatcherPriority.Background, owner.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
        state.RetryTimer.Tick += (_, _) => TryInstallMainWindowUx(owner, state);
        owner.Closed += (_, _) => state.RetryTimer?.Stop();
        state.RetryTimer.Start();

        owner.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => TryInstallMainWindowUx(owner, state)));
    }

    private static void TryInstallMainWindowUx(MainWindow owner, MainWindowState state)
    {
        if (!state.IedListInstalled)
        {
            var list = FindVisualChildren<ListBox>(owner)
                .FirstOrDefault(candidate => ReferenceEquals(candidate.ItemsSource, owner.Devices));
            if (list != null)
            {
                InstallCompactIedList(list);
                state.IedListInstalled = true;
            }
        }

        if (!state.CommandGridInstalled)
        {
            var commandGrid = FindVisualChildren<DataGrid>(owner)
                .FirstOrDefault(IsCommandGrid);
            if (commandGrid != null)
            {
                InstallCommandGridCleanup(commandGrid);
                state.CommandGridInstalled = true;
            }
        }

        if (!state.GlobalGridInstalled)
        {
            var globalGrid = FindVisualChildren<DataGrid>(owner)
                .FirstOrDefault(IsGlobalLiveGrid);
            if (globalGrid != null)
            {
                InstallGlobalRapidFilters(owner, globalGrid);
                state.GlobalGridInstalled = true;
            }
        }

        if (state.IedListInstalled && state.CommandGridInstalled && state.GlobalGridInstalled)
            state.RetryTimer?.Stop();
    }

    // -------------------------------------------------------------------------
    // Flat engineering grids
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // Compact IED cards
    // -------------------------------------------------------------------------

    private static void InstallCompactIedList(ListBox list)
    {
        if (IedLists.TryGetValue(list, out _))
            return;

        IedLists.Add(list, new Marker());

        void ApplyCards()
            => list.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() => ApplyCompactIedCards(list)));

        list.ItemContainerGenerator.StatusChanged += (_, _) => ApplyCards();
        list.Loaded += (_, _) => ApplyCards();
        ApplyCards();
    }

    private static void ApplyCompactIedCards(ListBox list)
    {
        for (var index = 0; index < list.Items.Count; index++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem container)
                ConfigureCompactIedCard(container);
        }
    }

    private static void ConfigureCompactIedCard(ListBoxItem container)
    {
        container.Margin = new Thickness(0, 0, 0, 6);
        container.ApplyTemplate();

        if (container.Template.FindName("Card", container) is Border card)
        {
            card.CornerRadius = new CornerRadius(10);
            card.Padding = new Thickness(8);
        }

        var nameText = FindVisualChildren<TextBlock>(container)
            .FirstOrDefault(text => GetBindingPath(text, TextBlock.TextProperty)
                .Equals("Name", StringComparison.Ordinal));
        if (nameText?.Parent is not StackPanel identityPanel || identityPanel.Parent is not Grid cardContent)
            return;

        var root = cardContent.Parent as Grid;
        if (root != null)
            root.MinHeight = 70;

        cardContent.VerticalAlignment = VerticalAlignment.Center;
        cardContent.ColumnDefinitions.Clear();
        cardContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        cardContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cardContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetRow(identityPanel, 0);
        Grid.SetColumn(identityPanel, 1);
        identityPanel.VerticalAlignment = VerticalAlignment.Center;
        identityPanel.Margin = new Thickness(0);

        nameText.FontSize = 13;
        nameText.FontWeight = FontWeights.SemiBold;

        var endpointText = FindVisualChildren<TextBlock>(identityPanel)
            .FirstOrDefault(text => GetBindingPath(text, TextBlock.TextProperty)
                .Equals("IdentityText", StringComparison.Ordinal));
        if (endpointText != null)
        {
            BindingOperations.SetBinding(
                endpointText,
                TextBlock.TextProperty,
                new Binding("EndpointText") { Mode = BindingMode.OneWay });
            endpointText.FontSize = 11.5;
            endpointText.Margin = new Thickness(0, 3, 0, 0);
        }

        foreach (var noisyPath in new[] { "SummaryText", "AcquisitionMode" })
        {
            var noisyText = FindVisualChildren<TextBlock>(identityPanel)
                .FirstOrDefault(text => GetBindingPath(text, TextBlock.TextProperty)
                    .Equals(noisyPath, StringComparison.Ordinal));
            if (noisyText != null)
                noisyText.Visibility = Visibility.Collapsed;
        }

        var unreadText = FindVisualChildren<TextBlock>(container)
            .FirstOrDefault(text => GetBindingPath(text, TextBlock.TextProperty)
                .Equals("UnreadEventText", StringComparison.Ordinal));
        var unreadBadge = FindAncestor<Border>(unreadText);
        if (unreadBadge != null)
            unreadBadge.Visibility = Visibility.Collapsed;

        var statusHost = cardContent.Children
            .OfType<Grid>()
            .FirstOrDefault(grid => Grid.GetRow(grid) == 0 && Grid.GetColumn(grid) == 0);
        if (statusHost != null)
            ConfigureIedIcon(statusHost);

        var actionHost = cardContent.Children
            .OfType<Border>()
            .FirstOrDefault(border => FindVisualChildren<Button>(border).Count() >= 4);
        if (actionHost != null)
        {
            Grid.SetRow(actionHost, 0);
            Grid.SetColumn(actionHost, 2);
            actionHost.HorizontalAlignment = HorizontalAlignment.Right;
            actionHost.VerticalAlignment = VerticalAlignment.Center;
            actionHost.Margin = new Thickness(8, 0, 0, 0);
            actionHost.Padding = new Thickness(0);
            actionHost.Background = Brushes.Transparent;
            actionHost.BorderThickness = new Thickness(0);
            actionHost.CornerRadius = new CornerRadius(0);

            foreach (var button in FindVisualChildren<Button>(actionHost))
                button.Margin = new Thickness(1.5, 0, 1.5, 0);
        }
    }

    private static void ConfigureIedIcon(Grid statusHost)
    {
        statusHost.Width = 36;
        statusHost.Height = 36;
        statusHost.Margin = new Thickness(0, 0, 8, 0);
        statusHost.VerticalAlignment = VerticalAlignment.Center;

        if (statusHost.Children.OfType<Border>().Any(border => Equals(border.Tag, "CompactIedIcon")))
            return;

        var icon = new Border
        {
            Tag = "CompactIedIcon",
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromRgb(238, 244, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(210, 224, 251)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M4,3 H20 V21 H4 Z M8,8 H16 M8,12 H16 M8,16 H14"),
                Width = 19,
                Height = 19,
                Stretch = Stretch.Uniform,
                Stroke = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = Brushes.Transparent
            }
        };

        statusHost.Children.Insert(0, icon);
    }

    // -------------------------------------------------------------------------
    // Command Panel cleanup
    // -------------------------------------------------------------------------

    private static bool IsCommandGrid(DataGrid grid)
    {
        var headers = HeaderNames(grid);
        return headers.Contains("Control model") &&
               headers.Contains("CDC / Type") &&
               headers.Contains("Control");
    }

    private static void InstallCommandGridCleanup(DataGrid grid)
    {
        if (CommandGrids.TryGetValue(grid, out _))
            return;

        CommandGrids.Add(grid, new Marker());
        grid.RowHeight = 44;
        grid.MinRowHeight = 42;
        grid.LoadingRow += (_, args) =>
            args.Row.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() => CleanupCommandRow(args.Row)));

        foreach (var row in FindVisualChildren<DataGridRow>(grid))
            CleanupCommandRow(row);
    }

    private static void CleanupCommandRow(DataGridRow row)
    {
        row.ApplyTemplate();

        foreach (var button in FindVisualChildren<Button>(row).ToArray())
        {
            var content = ExtractButtonText(button);
            if (content.Equals("Details", StringComparison.OrdinalIgnoreCase))
            {
                RemoveOrCollapse(button);
                continue;
            }

            if (!content.Equals("Technical details", StringComparison.OrdinalIgnoreCase) &&
                !content.Equals("Not available", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (button.Parent is Panel parent)
            {
                var label = parent.Children
                    .OfType<TextBlock>()
                    .FirstOrDefault(text => Equals(text.Tag, "CommandUnavailableLabel"));
                if (label == null)
                {
                    label = new TextBlock
                    {
                        Tag = "CommandUnavailableLabel",
                        Text = "Not available",
                        Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    parent.Children.Add(label);
                }

                parent.Children.Remove(button);
            }
            else
            {
                RemoveOrCollapse(button);
            }
        }

        foreach (var text in FindVisualChildren<TextBlock>(row))
        {
            if (!GetBindingPath(text, TextBlock.TextProperty)
                    .Equals(nameof(SignalDefinition.ControlLastResult), StringComparison.Ordinal))
            {
                continue;
            }

            if (FindAncestor<DockPanel>(text) is DockPanel resultLine)
                resultLine.Visibility = Visibility.Collapsed;
            else
                text.Visibility = Visibility.Collapsed;
        }
    }

    private static void RemoveOrCollapse(FrameworkElement element)
    {
        if (element.Parent is Panel panel)
            panel.Children.Remove(element);
        else
        {
            element.Visibility = Visibility.Collapsed;
            element.IsHitTestVisible = false;
        }
    }

    private static string ExtractButtonText(Button button)
        => button.Content switch
        {
            string text => text.Trim(),
            TextBlock textBlock => textBlock.Text?.Trim() ?? string.Empty,
            _ => button.Content?.ToString()?.Trim() ?? string.Empty
        };

    // -------------------------------------------------------------------------
    // Global Live Monitor rapid filters
    // -------------------------------------------------------------------------

    private static bool IsGlobalLiveGrid(DataGrid grid)
    {
        var headers = HeaderNames(grid);
        return headers.SetEquals(new[]
        {
            "IED", "Signal", "IEC Telegram", "Value", "Quality", "IED Timestamp", "Acquisition"
        });
    }

    private static void InstallGlobalRapidFilters(MainWindow owner, DataGrid grid)
    {
        if (GlobalGrids.TryGetValue(grid, out _))
            return;

        var view = CollectionViewSource.GetDefaultView(owner.GlobalPoints);
        var timer = new DispatcherTimer(DispatcherPriority.Background, owner.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(160)
        };
        var state = new GlobalRapidFilterState
        {
            Grid = grid,
            View = view,
            RefreshTimer = timer
        };
        GlobalGrids.Add(grid, state);

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            state.View.Refresh();
        };
        state.View.Filter = item => FilterGlobalPoint(item, state.Filters);

        var baseHeaderStyle = owner.TryFindResource(typeof(DataGridColumnHeader)) as Style;
        var headerStyle = new Style(typeof(DataGridColumnHeader), baseHeaderStyle);
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        headerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        headerStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
        headerStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 70d));
        grid.ColumnHeaderStyle = headerStyle;
        grid.ColumnHeaderHeight = 70;

        foreach (var column in grid.Columns)
        {
            var caption = column.Header?.ToString() ?? string.Empty;
            column.Header = BuildRapidFilterHeader(state, caption);
        }
    }

    private static FrameworkElement BuildRapidFilterHeader(GlobalRapidFilterState state, string caption)
    {
        var root = new Grid
        {
            Background = Brushes.Transparent,
            SnapsToDevicePixels = true
        };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(33) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(37) });

        var title = new TextBlock
        {
            Text = caption,
            Margin = new Thickness(10, 0, 8, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103)),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.3,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var filterBox = CreateRapidFilterTextBox(state, caption);
        var filterBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 227, 236)),
            BorderThickness = new Thickness(0, 1, 1, 0),
            CornerRadius = new CornerRadius(0),
            Child = filterBox
        };
        Grid.SetRow(filterBorder, 1);
        root.Children.Add(filterBorder);

        return root;
    }

    private static Grid CreateRapidFilterTextBox(GlobalRapidFilterState state, string key)
    {
        var box = new TextBox
        {
            Tag = key,
            Height = 36,
            Padding = new Thickness(9, 0, 7, 0),
            FontSize = 12.1,
            FontWeight = FontWeights.Normal,
            Foreground = new SolidColorBrush(Color.FromRgb(29, 41, 57)),
            CaretBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            Background = Brushes.White,
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FocusVisualStyle = null,
            Template = BuildSquareTextBoxTemplate()
        };

        var watermark = new TextBlock
        {
            Text = "Filter…",
            Margin = new Thickness(9, 0, 7, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(152, 162, 179)),
            FontSize = 12.1,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        void UpdateWatermark()
            => watermark.Visibility = string.IsNullOrEmpty(box.Text) && !box.IsKeyboardFocused
                ? Visibility.Visible
                : Visibility.Collapsed;

        box.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (box.IsKeyboardFocusWithin)
                return;
            e.Handled = true;
            box.Focus();
        };
        box.PreviewMouseLeftButtonUp += (_, e) => e.Handled = true;
        box.GotKeyboardFocus += (_, _) => UpdateWatermark();
        box.LostKeyboardFocus += (_, _) => UpdateWatermark();
        box.TextChanged += (_, _) =>
        {
            state.Filters[key] = box.Text ?? string.Empty;
            UpdateWatermark();
            state.RefreshTimer.Stop();
            state.RefreshTimer.Start();
        };
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

        var host = new Grid();
        host.Children.Add(box);
        host.Children.Add(watermark);
        return host;
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
                "IEC Telegram" => point.IecTelegram,
                "Value" => point.Value,
                "Quality" => point.Quality,
                "IED Timestamp" => point.DeviceTimestamp,
                "Acquisition" => point.SourceMode,
                _ => string.Empty
            };

            if (!tokens.All(token => (field ?? string.Empty)
                    .Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] Tokenize(string? text)
        => Regex.Matches(text ?? string.Empty, @"[^\s]+")
            .Select(match => match.Value.Trim())
            .Where(token => token.Length > 0)
            .ToArray();

    // -------------------------------------------------------------------------
    // Selection Wizard filter watermark
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static HashSet<string> HeaderNames(DataGrid grid)
        => grid.Columns
            .Select(column => column.Header?.ToString() ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string GetBindingPath(DependencyObject target, DependencyProperty property)
        => BindingOperations.GetBindingExpression(target, property)?.ParentBinding.Path?.Path ?? string.Empty;

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
                return match;

            try
            {
                current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
            }
            catch (InvalidOperationException)
            {
                current = LogicalTreeHelper.GetParent(current);
            }
        }

        return null;
    }

    private static ControlTemplate BuildSquareTextBoxTemplate()
    {
#pragma warning disable CS0618
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background))
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
