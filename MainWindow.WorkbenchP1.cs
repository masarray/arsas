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

namespace ArIED61850Tester;

/// <summary>
/// P1 workstation context layer.
///
/// P0 established one persistent IED Explorer and one shared Command Dock. P1 makes
/// observation scope explicit without coupling it to command target: Live Monitor and
/// Event Log remain ALL IEDs by default, but each can focus on the currently selected
/// IED. GOOSE intentionally remains NIC/network scoped.
///
/// P1 refinement deliberately reuses ARSAS' existing MiniChipButton/Caption language
/// instead of introducing a second segmented-control visual system. The Command Dock
/// also has an intentional empty state: its engineering grid is removed from view when
/// there are no validated control objects, so an empty white table can never look like
/// a broken layout.
/// </summary>
public partial class MainWindow
{
    private WorkbenchP1State? _workbenchP1;
    private bool _workbenchP1InstallQueued;
    private DispatcherTimer? _workbenchP1InstallRetryTimer;
    private DispatcherTimer? _workbenchP1LiveRetryTimer;

    private enum WorkbenchViewScope
    {
        AllIeds,
        SelectedIed
    }

    private sealed class WorkbenchP1State
    {
        public required Border SplitterGuide { get; init; }

        public required ICollectionView EventView { get; init; }
        public Predicate<object>? OriginalEventFilter { get; init; }
        public Predicate<object>? EventCompositeFilter { get; set; }
        public required Button EventAllButton { get; init; }
        public required Button EventSelectedButton { get; init; }
        public WorkbenchViewScope EventScope { get; set; } = WorkbenchViewScope.AllIeds;

        public ICollectionView? LiveView { get; set; }
        public Predicate<object>? OriginalLiveFilter { get; set; }
        public Predicate<object>? LiveCompositeFilter { get; set; }
        public Button? LiveAllButton { get; set; }
        public Button? LiveSelectedButton { get; set; }
        public WorkbenchViewScope LiveScope { get; set; } = WorkbenchViewScope.AllIeds;

        public DataGrid? CommandGrid { get; set; }
        public Border? CommandEmptyState { get; set; }
        public TextBlock? CommandEmptyTitle { get; set; }
        public TextBlock? CommandEmptyBody { get; set; }
        public Button? CommandRefreshButton { get; set; }
        public Iec61850MonitorDevice? CommandStateDevice { get; set; }

        public bool SplitterDragging { get; set; }
    }

    [ModuleInitializer]
    internal static void RegisterWorkbenchP1()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(WorkbenchP1_MainWindowLoaded),
            true);
    }

    private static void WorkbenchP1_MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            window.QueueWorkbenchP1Install();
    }

    private void QueueWorkbenchP1Install()
    {
        if (_workbenchP1 != null || _workbenchP1InstallQueued || !IsLoaded)
            return;

        _workbenchP1InstallQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            _workbenchP1InstallQueued = false;
            TryInstallWorkbenchP1();
        }));
    }

    private void TryInstallWorkbenchP1()
    {
        if (_workbenchP1 != null || !IsLoaded)
            return;

        // P0 installs at ContextIdle. If another Loaded participant delayed it, retry
        // gently instead of spinning dispatcher work while the Explorer tab is active.
        if (_persistentWorkbench == null)
        {
            EnsureP1InstallRetryTimer();
            return;
        }

        if (MainTabs.Items.Count < 3 || MainTabs.Items[2] is not TabItem eventTab)
            return;

        var eventGrid = P1LogicalDescendants<DataGrid>(eventTab)
            .FirstOrDefault(grid => string.Equals(
                BindingOperations.GetBinding(grid, ItemsControl.ItemsSourceProperty)?.Path?.Path,
                nameof(Events),
                StringComparison.Ordinal));
        if (eventGrid == null)
            return;

        var eventScope = CreateP1ScopeControl(
            "Choose whether Event Log shows every IED or only the IED selected in IED Explorer.",
            out var eventAll,
            out var eventSelected);
        if (!InstallP1EventScopeControl(eventTab, eventScope))
            return;

        var eventView = CollectionViewSource.GetDefaultView(Events);
        var originalEventFilter = eventView.Filter;
        var guide = InstallP1ModernSplitter(_persistentWorkbench);

        var state = new WorkbenchP1State
        {
            SplitterGuide = guide,
            EventView = eventView,
            OriginalEventFilter = originalEventFilter,
            EventAllButton = eventAll,
            EventSelectedButton = eventSelected
        };
        Predicate<object> eventComposite = item =>
            (originalEventFilter?.Invoke(item) ?? true) &&
            IsP1ItemInSelectedScope(item, state.EventScope);
        state.EventCompositeFilter = eventComposite;
        eventView.Filter = eventComposite;
        _workbenchP1 = state;

        eventAll.Click += P1EventAll_Click;
        eventSelected.Click += P1EventSelected_Click;
        GlobalLiveGrid.Loaded += WorkbenchP1_GlobalLiveGridLoaded;
        PropertyChanged += WorkbenchP1_PropertyChanged;
        Closed += WorkbenchP1_Closed;

        InstallP1CommandDockEmptyState(state);
        UpdateP1ScopePresentation(state);
        eventView.Refresh();

        // Global Live rapid filtering is installed by GridUxBehavior only when the
        // hidden tab enters the visual tree. Compose P1 after that filter exists.
        if (GlobalLiveGrid.IsLoaded)
            TryInstallP1LiveScope();
    }

    private void EnsureP1InstallRetryTimer()
    {
        if (_workbenchP1InstallRetryTimer != null)
            return;

        _workbenchP1InstallRetryTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(140)
        };
        _workbenchP1InstallRetryTimer.Tick += WorkbenchP1_InstallRetryTick;
        _workbenchP1InstallRetryTimer.Start();
    }

    private void WorkbenchP1_InstallRetryTick(object? sender, EventArgs e)
    {
        if (_workbenchP1InstallRetryTimer != null)
        {
            _workbenchP1InstallRetryTimer.Stop();
            _workbenchP1InstallRetryTimer.Tick -= WorkbenchP1_InstallRetryTick;
            _workbenchP1InstallRetryTimer = null;
        }
        QueueWorkbenchP1Install();
    }

    private void WorkbenchP1_GlobalLiveGridLoaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(TryInstallP1LiveScope));
    }

    private void TryInstallP1LiveScope()
    {
        var state = _workbenchP1;
        if (state == null || state.LiveView != null || !GlobalLiveGrid.IsLoaded)
            return;

        var liveView = CollectionViewSource.GetDefaultView(GlobalPoints);
        if (liveView.Filter == null)
        {
            EnsureP1LiveRetryTimer();
            return;
        }

        StopP1LiveRetryTimer();
        var liveScope = CreateP1ScopeControl(
            "Choose whether Live Monitor shows every IED or only the IED selected in IED Explorer.",
            out var liveAll,
            out var liveSelected);
        if (!InstallP1LiveScopeControl(liveScope))
            return;

        var originalLiveFilter = liveView.Filter;
        Predicate<object> liveComposite = item =>
            (originalLiveFilter?.Invoke(item) ?? true) &&
            IsP1ItemInSelectedScope(item, state.LiveScope);

        state.LiveView = liveView;
        state.OriginalLiveFilter = originalLiveFilter;
        state.LiveCompositeFilter = liveComposite;
        state.LiveAllButton = liveAll;
        state.LiveSelectedButton = liveSelected;

        liveView.Filter = liveComposite;
        liveAll.Click += P1LiveAll_Click;
        liveSelected.Click += P1LiveSelected_Click;
        UpdateP1ScopePresentation(state);
        liveView.Refresh();
    }

    private void EnsureP1LiveRetryTimer()
    {
        if (_workbenchP1LiveRetryTimer != null)
            return;

        _workbenchP1LiveRetryTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _workbenchP1LiveRetryTimer.Tick += WorkbenchP1_LiveRetryTick;
        _workbenchP1LiveRetryTimer.Start();
    }

    private void WorkbenchP1_LiveRetryTick(object? sender, EventArgs e)
        => TryInstallP1LiveScope();

    private void StopP1LiveRetryTimer()
    {
        if (_workbenchP1LiveRetryTimer == null)
            return;

        _workbenchP1LiveRetryTimer.Stop();
        _workbenchP1LiveRetryTimer.Tick -= WorkbenchP1_LiveRetryTick;
        _workbenchP1LiveRetryTimer = null;
    }

    private Border InstallP1ModernSplitter(PersistentWorkbenchState p0)
    {
        // One layout pixel remains between panes, but the 12 px transparent hit area
        // overlaps both sides so the resize target is easy to acquire without a bar.
        if (p0.Shell.ColumnDefinitions.Count >= 2)
            p0.Shell.ColumnDefinitions[1].Width = new GridLength(1);

        var splitter = p0.Splitter;
        splitter.Width = 12;
        splitter.Background = Brushes.Transparent;
        splitter.HorizontalAlignment = HorizontalAlignment.Center;
        splitter.Focusable = false;
        splitter.Cursor = Cursors.SizeWE;
        splitter.ToolTip = "Drag to resize IED Explorer";
        Panel.SetZIndex(splitter, 60);

        var guide = new Border
        {
            Width = 2,
            Background = new SolidColorBrush(Color.FromRgb(0x4F, 0x86, 0xE5)),
            CornerRadius = new CornerRadius(1),
            Opacity = 0,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 5, 0, 5)
        };
        Grid.SetColumn(guide, 1);
        Grid.SetRow(guide, 0);
        Grid.SetRowSpan(guide, 2);
        Panel.SetZIndex(guide, 55);
        p0.Shell.Children.Insert(Math.Max(0, p0.Shell.Children.IndexOf(splitter)), guide);

        splitter.MouseEnter += P1Splitter_MouseEnter;
        splitter.MouseLeave += P1Splitter_MouseLeave;
        splitter.DragStarted += P1Splitter_DragStarted;
        splitter.DragCompleted += P1Splitter_DragCompleted;
        return guide;
    }

    private void P1Splitter_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_workbenchP1 != null)
            AnimateP1SplitterGuide(_workbenchP1.SplitterGuide, 0.95, 110);
    }

    private void P1Splitter_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_workbenchP1 is { SplitterDragging: false } state)
            AnimateP1SplitterGuide(state.SplitterGuide, 0, 150);
    }

    private void P1Splitter_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (_workbenchP1 == null)
            return;

        _workbenchP1.SplitterDragging = true;
        AnimateP1SplitterGuide(_workbenchP1.SplitterGuide, 1, 70);
    }

    private void P1Splitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_workbenchP1 == null)
            return;

        _workbenchP1.SplitterDragging = false;
        var target = _persistentWorkbench?.Splitter.IsMouseOver == true ? 0.95 : 0;
        AnimateP1SplitterGuide(_workbenchP1.SplitterGuide, target, 140);
    }

    private static void AnimateP1SplitterGuide(UIElement guide, double opacity, int milliseconds)
    {
        guide.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            To = opacity,
            Duration = TimeSpan.FromMilliseconds(milliseconds),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private FrameworkElement CreateP1ScopeControl(
        string toolTip,
        out Button allButton,
        out Button selectedButton)
    {
        var host = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = toolTip
        };

        var caption = new TextBlock
        {
            Text = "Scope",
            Style = TryFindResource("Caption") as Style,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        allButton = CreateP1ScopeButton("All IEDs");
        selectedButton = CreateP1ScopeButton("Selected IED");
        selectedButton.Margin = new Thickness(4, 0, 0, 0);

        host.Children.Add(caption);
        host.Children.Add(allButton);
        host.Children.Add(selectedButton);
        return host;
    }

    private Button CreateP1ScopeButton(string text)
    {
        var button = new Button
        {
            Content = text,
            Style = TryFindResource("MiniChipButton") as Style ?? TryFindResource("SoftButton") as Style,
            Padding = new Thickness(10, 5, 10, 5),
            MinHeight = 30,
            MinWidth = 0,
            FontSize = 11.4,
            FocusVisualStyle = null
        };
        return button;
    }

    private bool InstallP1LiveScopeControl(FrameworkElement scopeControl)
    {
        if (GlobalLiveFiltersButton?.Parent is not Grid searchRow)
            return false;

        searchRow.Children.Remove(GlobalLiveFiltersButton);
        var actions = new StackPanel
        {
            Tag = "P1LiveScope",
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        scopeControl.Margin = new Thickness(0, 0, 10, 0);
        actions.Children.Add(scopeControl);
        actions.Children.Add(GlobalLiveFiltersButton);
        Grid.SetColumn(actions, 2);
        searchRow.Children.Add(actions);
        return true;
    }

    private bool InstallP1EventScopeControl(TabItem eventTab, FrameworkElement scopeControl)
    {
        var exportButton = P1LogicalDescendants<Button>(eventTab)
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Export CSV", StringComparison.Ordinal));
        if (exportButton?.Parent is not WrapPanel actions)
            return false;

        var oldScope = actions.Children
            .OfType<FrameworkElement>()
            .FirstOrDefault(element => Equals(element.Tag, "P0EventAllIedScope"));
        var insertIndex = oldScope == null ? 0 : actions.Children.IndexOf(oldScope);
        if (oldScope != null)
            actions.Children.Remove(oldScope);

        scopeControl.Tag = "P1EventScope";
        scopeControl.Margin = new Thickness(0, 0, 12, 0);
        actions.Children.Insert(Math.Max(0, insertIndex), scopeControl);
        return true;
    }

    private void InstallP1CommandDockEmptyState(WorkbenchP1State state)
    {
        var commandGrid = P1LogicalDescendants<DataGrid>(CommandPanelExpander)
            .FirstOrDefault(grid => string.Equals(
                BindingOperations.GetBinding(grid, ItemsControl.ItemsSourceProperty)?.Path?.Path,
                "SelectedDevice.CommandSignals",
                StringComparison.Ordinal));
        var legacyEmptyLabel = P1LogicalDescendants<TextBlock>(CommandPanelExpander)
            .FirstOrDefault(text => string.Equals(text.Text, "No operable commands", StringComparison.Ordinal));
        if (commandGrid == null || legacyEmptyLabel?.Parent is not Border emptyState)
            return;

        var title = new TextBlock
        {
            Text = "No operable commands",
            Style = TryFindResource("SectionTitle") as Style,
            FontSize = 12.6,
            VerticalAlignment = VerticalAlignment.Center
        };
        var body = new TextBlock
        {
            Style = TryFindResource("Caption") as Style,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        var copy = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center
        };
        copy.Children.Add(title);
        copy.Children.Add(body);

        emptyState.Child = copy;
        emptyState.HorizontalAlignment = HorizontalAlignment.Stretch;
        emptyState.VerticalAlignment = VerticalAlignment.Center;
        emptyState.MinHeight = 62;
        emptyState.Padding = new Thickness(14, 11, 14, 11);
        emptyState.Margin = new Thickness(0);
        emptyState.CornerRadius = new CornerRadius(11);
        emptyState.Background = TryFindResource("Surface") as Brush ?? new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFD));
        emptyState.BorderBrush = TryFindResource("Line") as Brush ?? new SolidColorBrush(Color.FromRgb(0xDD, 0xE5, 0xF0));
        emptyState.BorderThickness = new Thickness(1);

        state.CommandGrid = commandGrid;
        state.CommandEmptyState = emptyState;
        state.CommandEmptyTitle = title;
        state.CommandEmptyBody = body;
        state.CommandRefreshButton = P1LogicalDescendants<Button>(CommandPanelExpander)
            .FirstOrDefault(button => P1LogicalDescendants<TextBlock>(button)
                .Any(text => string.Equals(text.Text, "Refresh values", StringComparison.Ordinal)));

        RebindP1CommandState(state);
    }

    private void RebindP1CommandState(WorkbenchP1State state)
    {
        if (state.CommandStateDevice != null)
            state.CommandStateDevice.CommandSignals.CollectionChanged -= WorkbenchP1_CommandSignalsChanged;

        state.CommandStateDevice = SelectedDevice;
        if (state.CommandStateDevice != null)
            state.CommandStateDevice.CommandSignals.CollectionChanged += WorkbenchP1_CommandSignalsChanged;

        UpdateP1CommandDockPresentation(state);
    }

    private void WorkbenchP1_CommandSignalsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_workbenchP1 != null)
            UpdateP1CommandDockPresentation(_workbenchP1);
    }

    private void UpdateP1CommandDockPresentation(WorkbenchP1State state)
    {
        if (state.CommandGrid == null || state.CommandEmptyState == null)
            return;

        var selected = SelectedDevice;
        var commandCount = selected?.CommandSignals.Count ?? 0;
        var hasCommands = selected != null && commandCount > 0;

        state.CommandGrid.Visibility = hasCommands ? Visibility.Visible : Visibility.Collapsed;
        state.CommandEmptyState.Visibility = selected != null && !hasCommands
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (state.CommandRefreshButton != null)
            state.CommandRefreshButton.Visibility = hasCommands ? Visibility.Visible : Visibility.Collapsed;

        if (selected == null || state.CommandEmptyTitle == null || state.CommandEmptyBody == null)
            return;

        state.CommandEmptyTitle.Text = "No operable commands";
        state.CommandEmptyBody.Text =
            $"{selected.Name} has no validated control objects in the current signal selection. Use Edit signals to add supported control objects.";
    }

    private void P1LiveAll_Click(object sender, RoutedEventArgs e)
        => SetP1Scope(isLive: true, WorkbenchViewScope.AllIeds);

    private void P1LiveSelected_Click(object sender, RoutedEventArgs e)
        => SetP1Scope(isLive: true, WorkbenchViewScope.SelectedIed);

    private void P1EventAll_Click(object sender, RoutedEventArgs e)
        => SetP1Scope(isLive: false, WorkbenchViewScope.AllIeds);

    private void P1EventSelected_Click(object sender, RoutedEventArgs e)
        => SetP1Scope(isLive: false, WorkbenchViewScope.SelectedIed);

    private void SetP1Scope(bool isLive, WorkbenchViewScope scope)
    {
        var state = _workbenchP1;
        if (state == null || (scope == WorkbenchViewScope.SelectedIed && SelectedDevice == null))
            return;

        if (isLive)
        {
            if (state.LiveView == null)
                return;
            state.LiveScope = scope;
            state.LiveView.Refresh();
        }
        else
        {
            state.EventScope = scope;
            state.EventView.Refresh();
        }
        UpdateP1ScopePresentation(state);
    }

    private bool IsP1ItemInSelectedScope(object item, WorkbenchViewScope scope)
    {
        if (scope == WorkbenchViewScope.AllIeds)
            return true;

        var selectedId = SelectedDevice?.DeviceId;
        if (string.IsNullOrWhiteSpace(selectedId))
            return false;

        return item switch
        {
            Iec61850MonitorPoint point => point.DeviceId.Equals(selectedId, StringComparison.OrdinalIgnoreCase),
            Iec61850EventEntry entry => entry.DeviceId.Equals(selectedId, StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private void WorkbenchP1_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var state = _workbenchP1;
        if (state == null || e.PropertyName != nameof(SelectedDevice))
            return;

        if (SelectedDevice == null)
        {
            if (state.EventScope == WorkbenchViewScope.SelectedIed)
                state.EventScope = WorkbenchViewScope.AllIeds;
            if (state.LiveScope == WorkbenchViewScope.SelectedIed)
                state.LiveScope = WorkbenchViewScope.AllIeds;
        }

        if (state.EventScope == WorkbenchViewScope.SelectedIed || SelectedDevice == null)
            state.EventView.Refresh();
        if (state.LiveView != null &&
            (state.LiveScope == WorkbenchViewScope.SelectedIed || SelectedDevice == null))
        {
            state.LiveView.Refresh();
        }

        RebindP1CommandState(state);
        UpdateP1ScopePresentation(state);
    }

    private void UpdateP1ScopePresentation(WorkbenchP1State state)
    {
        var selectedName = SelectedDevice?.Name;
        var hasSelected = !string.IsNullOrWhiteSpace(selectedName);

        state.EventAllButton.ToolTip = "Show events from all IEDs";
        state.EventSelectedButton.IsEnabled = hasSelected;
        state.EventSelectedButton.ToolTip = hasSelected
            ? $"Show only {selectedName} events"
            : "Select an IED in IED Explorer first";
        ApplyP1ScopeButtonState(state.EventAllButton, state.EventScope == WorkbenchViewScope.AllIeds);
        ApplyP1ScopeButtonState(state.EventSelectedButton, state.EventScope == WorkbenchViewScope.SelectedIed);

        if (state.LiveAllButton == null || state.LiveSelectedButton == null)
            return;

        state.LiveAllButton.ToolTip = "Show live values from all monitored IEDs";
        state.LiveSelectedButton.IsEnabled = hasSelected;
        state.LiveSelectedButton.ToolTip = hasSelected
            ? $"Show only {selectedName} live values"
            : "Select an IED in IED Explorer first";
        ApplyP1ScopeButtonState(state.LiveAllButton, state.LiveScope == WorkbenchViewScope.AllIeds);
        ApplyP1ScopeButtonState(state.LiveSelectedButton, state.LiveScope == WorkbenchViewScope.SelectedIed);
    }

    private void ApplyP1ScopeButtonState(Button button, bool selected)
    {
        // Inactive buttons fall all the way back to MiniChipButton so their radius,
        // gradient, hover and pressed states remain identical to the rest of ARSAS.
        button.ClearValue(Control.BackgroundProperty);
        button.ClearValue(Control.BorderBrushProperty);
        button.ClearValue(Control.ForegroundProperty);

        if (!selected)
            return;

        button.Background = TryFindResource("SoftBlue") as Brush ?? new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xFF));
        button.BorderBrush = new SolidColorBrush(Color.FromRgb(0xAF, 0xC6, 0xEC));
        button.Foreground = TryFindResource("AccentDeep") as Brush ?? new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8));
    }

    private void WorkbenchP1_Closed(object? sender, EventArgs e)
    {
        var state = _workbenchP1;
        if (state == null)
            return;

        StopP1LiveRetryTimer();
        if (_workbenchP1InstallRetryTimer != null)
        {
            _workbenchP1InstallRetryTimer.Stop();
            _workbenchP1InstallRetryTimer.Tick -= WorkbenchP1_InstallRetryTick;
            _workbenchP1InstallRetryTimer = null;
        }

        if (state.EventCompositeFilter != null && ReferenceEquals(state.EventView.Filter, state.EventCompositeFilter))
            state.EventView.Filter = state.OriginalEventFilter;
        if (state.LiveView != null && state.LiveCompositeFilter != null &&
            ReferenceEquals(state.LiveView.Filter, state.LiveCompositeFilter))
        {
            state.LiveView.Filter = state.OriginalLiveFilter;
        }

        if (state.CommandStateDevice != null)
            state.CommandStateDevice.CommandSignals.CollectionChanged -= WorkbenchP1_CommandSignalsChanged;

        state.EventAllButton.Click -= P1EventAll_Click;
        state.EventSelectedButton.Click -= P1EventSelected_Click;
        if (state.LiveAllButton != null)
            state.LiveAllButton.Click -= P1LiveAll_Click;
        if (state.LiveSelectedButton != null)
            state.LiveSelectedButton.Click -= P1LiveSelected_Click;

        GlobalLiveGrid.Loaded -= WorkbenchP1_GlobalLiveGridLoaded;
        PropertyChanged -= WorkbenchP1_PropertyChanged;
        Closed -= WorkbenchP1_Closed;

        if (_persistentWorkbench?.Splitter is { } splitter)
        {
            splitter.MouseEnter -= P1Splitter_MouseEnter;
            splitter.MouseLeave -= P1Splitter_MouseLeave;
            splitter.DragStarted -= P1Splitter_DragStarted;
            splitter.DragCompleted -= P1Splitter_DragCompleted;
        }
        _workbenchP1 = null;
    }

    private static IEnumerable<T> P1LogicalDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dependencyObject)
                continue;

            if (dependencyObject is T typed)
                yield return typed;

            foreach (var nested in P1LogicalDescendants<T>(dependencyObject))
                yield return nested;
        }
    }
}
