using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// P0 persistent engineering-workbench shell.
///
/// This deliberately reuses the existing IED Explorer and Command Panel instances instead
/// of cloning their XAML into each tab. The IEC 61850 runtime, command handlers, bindings,
/// and safety/confirmation semantics therefore remain unchanged; only their presentation
/// ownership is lifted from the Explorer tab into one shared MainWindow workbench.
/// </summary>
public partial class MainWindow
{
    private PersistentWorkbenchState? _persistentWorkbench;

    private sealed class PersistentWorkbenchState
    {
        public Grid Shell { get; init; } = null!;
        public ColumnDefinition ExplorerColumn { get; init; } = null!;
        public GridSplitter Splitter { get; init; } = null!;
        public TextBlock CommandTargetText { get; init; } = null!;
        public Border? AlarmContextOverlay { get; set; }
        public TextBlock? AlarmContextTitle { get; set; }
        public TextBlock? AlarmContextBody { get; set; }
        public bool UserSizedExplorer { get; set; }
        public bool ApplyingDockState { get; set; }
        public bool SuppressDockStateCapture { get; set; }

        // P0 defaults: command-centric views open the dock, observational views do not.
        // A user's manual expand/collapse choice is then retained per workspace for the
        // remainder of the session.
        public Dictionary<int, bool> DockExpandedByWorkspace { get; } = new()
        {
            [0] = true,   // IEC 61850 Explorer
            [1] = false,  // Live Monitor
            [2] = true,   // Event Log
            [3] = false,  // Alarm
            [4] = false,  // GOOSE Subscriber
            [5] = false   // Diagnostics
        };
    }

    [ModuleInitializer]
    internal static void RegisterP0PersistentWorkbenchShell()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P0PersistentWorkbench_MainWindowLoaded),
            true);
    }

    private static void P0PersistentWorkbench_MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._persistentWorkbench != null)
            return;

        // Let XAML namescopes, existing Loaded behaviours, and the first selected TabItem
        // finish materialising before the two existing controls are re-parented.
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(window.TryInstallP0PersistentWorkbench));
    }

    private void TryInstallP0PersistentWorkbench()
    {
        if (_persistentWorkbench != null || !IsLoaded || MainTabs.Items.Count < 6)
            return;

        if (MainTabs.Parent is not Grid root ||
            MainTabs.Items[0] is not TabItem explorerTab ||
            explorerTab.Content is not Grid legacyExplorerLayout ||
            legacyExplorerLayout.ColumnDefinitions.Count < 3 ||
            CommandPanelExpander.Parent is not Panel legacyCommandParent)
        {
            return;
        }

        var explorerPane = legacyExplorerLayout.Children
            .OfType<Border>()
            .FirstOrDefault(element => Grid.GetColumn(element) == 0);
        if (explorerPane == null)
            return;

        // Build one shared shell in the original MainTabs slot.
        var explorerColumn = new ColumnDefinition
        {
            Width = new GridLength(GetRecommendedP0ExplorerWidth(ActualWidth)),
            MinWidth = 205,
            MaxWidth = 330
        };
        var splitterColumn = new ColumnDefinition { Width = new GridLength(10) };
        var workspaceColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 };

        var shell = new Grid
        {
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        shell.ColumnDefinitions.Add(explorerColumn);
        shell.ColumnDefinitions.Add(splitterColumn);
        shell.ColumnDefinitions.Add(workspaceColumn);
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var splitter = new GridSplitter
        {
            Width = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xED, 0xF3)),
            ResizeDirection = GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ShowsPreview = true,
            Focusable = false,
            Cursor = Cursors.SizeWE,
            ToolTip = "Resize IED Explorer"
        };
        Grid.SetColumn(splitter, 1);
        Grid.SetRow(splitter, 0);
        Grid.SetRowSpan(splitter, 2);

        // Remove before adding: WPF elements can have only one logical/visual parent.
        legacyExplorerLayout.Children.Remove(explorerPane);
        legacyCommandParent.Children.Remove(CommandPanelExpander);
        root.Children.Remove(MainTabs);

        // The old Explorer tab now owns only its right-hand data workspace.
        legacyExplorerLayout.ColumnDefinitions[0].Width = new GridLength(0);
        legacyExplorerLayout.ColumnDefinitions[0].MinWidth = 0;
        legacyExplorerLayout.ColumnDefinitions[1].Width = new GridLength(0);
        legacyExplorerLayout.ColumnDefinitions[1].MinWidth = 0;

        explorerPane.Margin = new Thickness(0);
        Grid.SetColumn(explorerPane, 0);
        Grid.SetRow(explorerPane, 0);
        Grid.SetRowSpan(explorerPane, 2);
        shell.Children.Add(explorerPane);

        MainTabs.Margin = new Thickness(0);
        Grid.SetColumn(MainTabs, 2);
        Grid.SetRow(MainTabs, 0);
        Grid.SetRowSpan(MainTabs, 1);
        shell.Children.Add(MainTabs);

        // Preserve the existing command Expander instance and every existing click handler.
        CommandPanelExpander.Margin = new Thickness(0, 10, 0, 0);
        CommandPanelExpander.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(CommandPanelExpander, 2);
        Grid.SetRow(CommandPanelExpander, 1);
        Grid.SetRowSpan(CommandPanelExpander, 1);
        BindingOperations.SetBinding(
            CommandPanelExpander,
            UIElement.VisibilityProperty,
            new Binding(nameof(SelectedExplorerVisibility)) { Source = this, Mode = BindingMode.OneWay });
        shell.Children.Add(CommandPanelExpander);
        shell.Children.Add(splitter);

        Grid.SetRow(shell, 1);
        Grid.SetColumn(shell, 0);
        root.Children.Add(shell);

        var targetText = DecorateP0CommandDockHeader();
        _persistentWorkbench = new PersistentWorkbenchState
        {
            Shell = shell,
            ExplorerColumn = explorerColumn,
            Splitter = splitter,
            CommandTargetText = targetText
        };

        InstallP0EventScopeChip();
        InstallP0AlarmSingleIedAuthority(_persistentWorkbench);
        UpdateP0CommandTarget(_persistentWorkbench);
        SynchronizeP0AlarmWithSelectedIed(_persistentWorkbench);
        ApplyP0ExplorerWidth(_persistentWorkbench);
        ApplyP0CommandDockState(_persistentWorkbench, MainTabs.SelectedIndex);

        MainTabs.SelectionChanged += P0PersistentWorkbench_MainTabsSelectionChanged;
        CommandPanelExpander.Expanded += P0PersistentWorkbench_CommandPanelExpanded;
        CommandPanelExpander.Collapsed += P0PersistentWorkbench_CommandPanelCollapsed;
        PropertyChanged += P0PersistentWorkbench_PropertyChanged;
        AnnunciatorDevices.CollectionChanged += P0PersistentWorkbench_AnnunciatorDevicesChanged;
        SizeChanged += P0PersistentWorkbench_SizeChanged;
        splitter.DragCompleted += P0PersistentWorkbench_SplitterDragCompleted;
        Closed += P0PersistentWorkbench_Closed;
    }

    private TextBlock DecorateP0CommandDockHeader()
    {
        if (CommandPanelExpander.Header is not StackPanel header)
        {
            return new TextBlock
            {
                Text = "TARGET · NONE",
                Visibility = Visibility.Collapsed
            };
        }

        var title = header.Children
            .OfType<TextBlock>()
            .FirstOrDefault(element => string.Equals(element.Text, "IED Command Panel", StringComparison.Ordinal));
        if (title != null)
        {
            title.Text = "Command Dock";
            title.FontSize = 13.2;
        }

        var targetText = new TextBlock
        {
            Text = "TARGET · NONE",
            FontSize = 10.2,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x58, 0x6B, 0x82)),
            VerticalAlignment = VerticalAlignment.Center
        };
        var targetBadge = new Border
        {
            Tag = "P0CommandTargetBadge",
            Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF7, 0xFB)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD6, 0xE0, 0xEC)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(7, 3, 7, 3),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Command target follows the selected IED. Changing IED clears pending control confirmations.",
            Child = targetText
        };
        header.Children.Add(targetBadge);
        return targetText;
    }

    private void InstallP0EventScopeChip()
    {
        if (MainTabs.Items.Count <= 2 || MainTabs.Items[2] is not TabItem eventTab)
            return;

        var exportButton = P0LogicalDescendants<Button>(eventTab)
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Export CSV", StringComparison.Ordinal));
        if (exportButton?.Parent is not WrapPanel actions ||
            actions.Children.OfType<FrameworkElement>().Any(element => Equals(element.Tag, "P0EventAllIedScope")))
        {
            return;
        }

        var scopeChip = new Border
        {
            Tag = "P0EventAllIedScope",
            Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF7, 0xFB)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD6, 0xE0, 0xEC)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(7, 4, 7, 4),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Event Log remains global across all IEDs. IED Explorer selects the command target, not the Event Log filter.",
            Child = new TextBlock
            {
                Text = "VIEW · ALL IEDs",
                FontSize = 9.8,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x6C, 0x82)),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        actions.Children.Insert(0, scopeChip);
    }

    private void InstallP0AlarmSingleIedAuthority(PersistentWorkbenchState state)
    {
        if (MainTabs.Items.Count <= 3 || MainTabs.Items[3] is not TabItem alarmTab)
            return;

        var annunciatorList = P0LogicalDescendants<ListBox>(alarmTab)
            .FirstOrDefault(list =>
            {
                var binding = BindingOperations.GetBinding(list, ItemsControl.ItemsSourceProperty);
                return string.Equals(binding?.Path?.Path, nameof(AnnunciatorDevices), StringComparison.Ordinal);
            });

        if (annunciatorList != null)
        {
            var railBorder = P0FindAncestor<Border>(annunciatorList);
            if (railBorder?.Parent is Grid annunciatorGrid &&
                annunciatorGrid.ColumnDefinitions.Count >= 3 &&
                Grid.GetColumn(railBorder) == 0)
            {
                // Keep the legacy list alive for its binding/state but remove it from the
                // operator layout. Global IED Explorer is now the only IED selection authority.
                railBorder.Visibility = Visibility.Collapsed;
                annunciatorGrid.ColumnDefinitions[0].Width = new GridLength(0);
                annunciatorGrid.ColumnDefinitions[0].MinWidth = 0;
                annunciatorGrid.ColumnDefinitions[1].Width = new GridLength(0);
                annunciatorGrid.ColumnDefinitions[1].MinWidth = 0;
            }
        }

        if (alarmTab.Content is not Border workspaceCard || workspaceCard.Child is not Grid alarmRoot)
            return;

        var title = new TextBlock
        {
            FontSize = 14.2,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0xF6, 0xFA)),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        var body = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xC2, 0xCF)),
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 470
        };
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(title);
        stack.Children.Add(body);

        var overlay = new Border
        {
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromRgb(0x29, 0x2E, 0x35)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x48, 0x53)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(26),
            Margin = new Thickness(1),
            Child = stack
        };
        Grid.SetRow(overlay, 2);
        Panel.SetZIndex(overlay, 100);
        alarmRoot.Children.Add(overlay);

        state.AlarmContextOverlay = overlay;
        state.AlarmContextTitle = title;
        state.AlarmContextBody = body;
    }

    private void P0PersistentWorkbench_MainTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_persistentWorkbench == null || !ReferenceEquals(e.Source, MainTabs))
            return;

        ApplyP0CommandDockState(_persistentWorkbench, MainTabs.SelectedIndex);
    }

    private void P0PersistentWorkbench_CommandPanelExpanded(object sender, RoutedEventArgs e)
        => CaptureP0CommandDockState(true);

    private void P0PersistentWorkbench_CommandPanelCollapsed(object sender, RoutedEventArgs e)
        => CaptureP0CommandDockState(false);

    private void CaptureP0CommandDockState(bool expanded)
    {
        var state = _persistentWorkbench;
        if (state == null || state.ApplyingDockState || state.SuppressDockStateCapture)
            return;

        state.DockExpandedByWorkspace[MainTabs.SelectedIndex] = expanded;
    }

    private void ApplyP0CommandDockState(PersistentWorkbenchState state, int workspaceIndex)
    {
        if (!state.DockExpandedByWorkspace.TryGetValue(workspaceIndex, out var expanded))
            expanded = false;

        state.ApplyingDockState = true;
        try
        {
            CommandPanelExpander.IsExpanded = expanded;
        }
        finally
        {
            state.ApplyingDockState = false;
        }
    }

    private void P0PersistentWorkbench_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var state = _persistentWorkbench;
        if (state == null || e.PropertyName != nameof(SelectedDevice))
            return;

        UpdateP0CommandTarget(state);
        SynchronizeP0AlarmWithSelectedIed(state);

        // SelectedDevice's legacy UX may auto-expand the old command panel at Background
        // priority. Suppress that programmatic event from overwriting this workspace's own
        // dock preference, then re-apply the intended state after that queue item runs.
        state.SuppressDockStateCapture = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (!ReferenceEquals(_persistentWorkbench, state))
                return;

            try
            {
                ApplyP0CommandDockState(state, MainTabs.SelectedIndex);
                SynchronizeP0AlarmWithSelectedIed(state);
            }
            finally
            {
                state.SuppressDockStateCapture = false;
            }
        }));
    }

    private void P0PersistentWorkbench_AnnunciatorDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var state = _persistentWorkbench;
        if (state == null)
            return;

        // Alarm collection reconciliation may select its historical first IED later in the
        // same mutation. Reassert Explorer authority once the current dispatcher work drains.
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (ReferenceEquals(_persistentWorkbench, state))
                SynchronizeP0AlarmWithSelectedIed(state);
        }));
    }

    private void SynchronizeP0AlarmWithSelectedIed(PersistentWorkbenchState state)
    {
        var selected = SelectedDevice;
        var matchingGroup = selected == null
            ? null
            : AnnunciatorDevices.FirstOrDefault(group =>
                group.DeviceId.Equals(selected.DeviceId, StringComparison.OrdinalIgnoreCase));

        if (!ReferenceEquals(SelectedAnnunciatorDevice, matchingGroup))
            SelectedAnnunciatorDevice = matchingGroup;

        if (state.AlarmContextOverlay == null ||
            state.AlarmContextTitle == null ||
            state.AlarmContextBody == null)
        {
            return;
        }

        // Preserve the existing project-wide empty-state when there are no alarm windows.
        if (AnnunciatorDevices.Count == 0 || matchingGroup != null)
        {
            state.AlarmContextOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        state.AlarmContextOverlay.Visibility = Visibility.Visible;
        if (selected == null)
        {
            state.AlarmContextTitle.Text = "Select an IED to view its annunciator";
            state.AlarmContextBody.Text = "IED Explorer is now the single device context for Alarm and Command workflows.";
        }
        else
        {
            state.AlarmContextTitle.Text = $"No Alarm points configured for {selected.Name}";
            state.AlarmContextBody.Text = "Select Alarm on the required ST points in IEC 61850 Explorer. Other IED alarms are not shown under the wrong device context.";
        }
    }

    private void UpdateP0CommandTarget(PersistentWorkbenchState state)
    {
        state.CommandTargetText.Text = SelectedDevice == null
            ? "TARGET · NONE"
            : $"TARGET · {SelectedDevice.Name}";
    }

    private void P0PersistentWorkbench_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_persistentWorkbench != null)
            ApplyP0ExplorerWidth(_persistentWorkbench);
    }

    private void ApplyP0ExplorerWidth(PersistentWorkbenchState state)
    {
        var available = Math.Max(1180, ActualWidth);
        state.ExplorerColumn.MaxWidth = Math.Min(330, Math.Max(245, available * 0.22));

        if (!state.UserSizedExplorer)
            state.ExplorerColumn.Width = new GridLength(GetRecommendedP0ExplorerWidth(available));
    }

    private static double GetRecommendedP0ExplorerWidth(double width)
        => width >= 1600 ? 258 : width >= 1380 ? 238 : 218;

    private void P0PersistentWorkbench_SplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_persistentWorkbench != null)
            _persistentWorkbench.UserSizedExplorer = true;
    }

    private void P0PersistentWorkbench_Closed(object? sender, EventArgs e)
    {
        var state = _persistentWorkbench;
        if (state == null)
            return;

        MainTabs.SelectionChanged -= P0PersistentWorkbench_MainTabsSelectionChanged;
        CommandPanelExpander.Expanded -= P0PersistentWorkbench_CommandPanelExpanded;
        CommandPanelExpander.Collapsed -= P0PersistentWorkbench_CommandPanelCollapsed;
        PropertyChanged -= P0PersistentWorkbench_PropertyChanged;
        AnnunciatorDevices.CollectionChanged -= P0PersistentWorkbench_AnnunciatorDevicesChanged;
        SizeChanged -= P0PersistentWorkbench_SizeChanged;
        state.Splitter.DragCompleted -= P0PersistentWorkbench_SplitterDragCompleted;
        Closed -= P0PersistentWorkbench_Closed;
        _persistentWorkbench = null;
    }

    private static IEnumerable<T> P0LogicalDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dependencyObject)
                continue;

            if (dependencyObject is T typed)
                yield return typed;

            foreach (var nested in P0LogicalDescendants<T>(dependencyObject))
                yield return nested;
        }
    }

    private static T? P0FindAncestor<T>(DependencyObject child)
        where T : DependencyObject
    {
        DependencyObject? current = child;
        while (current != null)
        {
            current = current switch
            {
                FrameworkElement element when element.Parent != null => element.Parent,
                FrameworkContentElement contentElement when contentElement.Parent != null => contentElement.Parent,
                _ => LogicalTreeHelper.GetParent(current)
            };

            if (current is T typed)
                return typed;
        }

        return null;
    }
}
