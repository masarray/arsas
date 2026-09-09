using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// P1 panel-geometry refinement for the persistent workbench.
///
/// The visible panes keep real breathing room, but resize affordances stay visually quiet:
/// the gutter itself is empty and a thin luminous guide appears only on hover/drag. The
/// Command Dock gets the same interaction language and can be resized vertically without
/// changing any IEC 61850 command semantics.
/// </summary>
public partial class MainWindow
{
    private P1PanelGeometryState? _p1PanelGeometry;
    private DispatcherTimer? _p1PanelGeometryInstallTimer;

    private sealed class P1PanelGeometryState
    {
        public required RowDefinition DockGutterRow { get; init; }
        public required RowDefinition DockRow { get; init; }
        public required GridSplitter DockSplitter { get; init; }
        public required Border DockGuide { get; init; }
        public bool DockDragging { get; set; }
        public Dictionary<int, double> DockHeightByWorkspace { get; } = new()
        {
            // First paint should expose a complete command row without forcing the operator
            // to resize the dock. Dragged heights remain authoritative for the session.
            [0] = 248,
            [1] = 224,
            [2] = 252,
            [3] = 224,
            [4] = 224,
            [5] = 224
        };
    }

    [ModuleInitializer]
    internal static void RegisterWorkbenchP1PanelGeometry()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P1PanelGeometry_MainWindowLoaded),
            true);
    }

    private static void P1PanelGeometry_MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            window.QueueP1PanelGeometryInstall();
    }

    private void QueueP1PanelGeometryInstall()
    {
        if (_p1PanelGeometry != null || !IsLoaded)
            return;

        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(TryInstallP1PanelGeometry));
    }

    private void TryInstallP1PanelGeometry()
    {
        if (_p1PanelGeometry != null || !IsLoaded)
            return;

        if (_persistentWorkbench == null || _workbenchP1 == null)
        {
            EnsureP1PanelGeometryInstallTimer();
            return;
        }

        var p0 = _persistentWorkbench;
        var p1 = _workbenchP1;
        var shell = p0.Shell;
        if (shell.RowDefinitions.Count != 2 ||
            !ReferenceEquals(CommandPanelExpander.Parent, shell))
        {
            return;
        }

        // Vertical Explorer/workspace gutter: real spacing with no permanent rule.
        // The transparent hit target overlaps the gutter so resize remains easy.
        if (shell.ColumnDefinitions.Count >= 2)
            shell.ColumnDefinitions[1].Width = new GridLength(10);

        p0.Splitter.Width = 14;
        p0.Splitter.Background = Brushes.Transparent;
        p0.Splitter.ToolTip = "Drag to resize IED Explorer";
        p1.SplitterGuide.Width = 2;
        p1.SplitterGuide.Background = Brushes.White;
        p1.SplitterGuide.Effect = CreateP1SplitterGlow();
        p1.SplitterGuide.Margin = new Thickness(0, 6, 0, 6);

        // P0 used row 1 directly for the command Expander. Insert a real gutter row so the
        // dock can use the same quiet resizer language as the Explorer.
        var dockRow = shell.RowDefinitions[1];
        var dockGutterRow = new RowDefinition { Height = new GridLength(0) };
        shell.RowDefinitions.Insert(1, dockGutterRow);

        CommandPanelExpander.Margin = new Thickness(0);
        Grid.SetRow(CommandPanelExpander, 2);
        Grid.SetRowSpan(CommandPanelExpander, 1);

        foreach (UIElement child in shell.Children)
        {
            if (Grid.GetColumn(child) == 0)
                Grid.SetRowSpan(child, 3);
        }
        Grid.SetRowSpan(p0.Splitter, 3);
        Grid.SetRowSpan(p1.SplitterGuide, 3);

        var dockGuide = new Border
        {
            Height = 2,
            Background = Brushes.White,
            CornerRadius = new CornerRadius(1),
            Opacity = 0,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 7, 0),
            Effect = CreateP1SplitterGlow()
        };
        Grid.SetColumn(dockGuide, 2);
        Grid.SetRow(dockGuide, 1);
        Panel.SetZIndex(dockGuide, 55);

        var dockSplitter = new GridSplitter
        {
            Height = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            ResizeDirection = GridResizeDirection.Rows,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ShowsPreview = true,
            Focusable = false,
            Cursor = Cursors.SizeNS,
            ToolTip = "Drag to resize Command Dock"
        };
        Grid.SetColumn(dockSplitter, 2);
        Grid.SetRow(dockSplitter, 1);
        Panel.SetZIndex(dockSplitter, 60);

        shell.Children.Add(dockGuide);
        shell.Children.Add(dockSplitter);

        var state = new P1PanelGeometryState
        {
            DockGutterRow = dockGutterRow,
            DockRow = dockRow,
            DockSplitter = dockSplitter,
            DockGuide = dockGuide
        };
        _p1PanelGeometry = state;

        dockSplitter.MouseEnter += P1DockSplitter_MouseEnter;
        dockSplitter.MouseLeave += P1DockSplitter_MouseLeave;
        dockSplitter.DragStarted += P1DockSplitter_DragStarted;
        dockSplitter.DragCompleted += P1DockSplitter_DragCompleted;
        CommandPanelExpander.Expanded += P1DockExpander_ExpandedCollapsed;
        CommandPanelExpander.Collapsed += P1DockExpander_ExpandedCollapsed;
        CommandPanelExpander.IsVisibleChanged += P1DockExpander_IsVisibleChanged;
        MainTabs.SelectionChanged += P1PanelGeometry_MainTabsSelectionChanged;
        Closed += P1PanelGeometry_Closed;

        ApplyP1DockGeometry(state, MainTabs.SelectedIndex);
    }

    private static DropShadowEffect CreateP1SplitterGlow()
        => new()
        {
            Color = Color.FromRgb(0xC8, 0xE4, 0xFF),
            BlurRadius = 9,
            ShadowDepth = 0,
            Opacity = 0.86
        };

    private void EnsureP1PanelGeometryInstallTimer()
    {
        if (_p1PanelGeometryInstallTimer != null)
            return;

        _p1PanelGeometryInstallTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _p1PanelGeometryInstallTimer.Tick += P1PanelGeometryInstallTimer_Tick;
        _p1PanelGeometryInstallTimer.Start();
    }

    private void P1PanelGeometryInstallTimer_Tick(object? sender, EventArgs e)
    {
        if (_p1PanelGeometry != null || !IsLoaded)
        {
            StopP1PanelGeometryInstallTimer();
            return;
        }

        if (_persistentWorkbench != null && _workbenchP1 != null)
        {
            StopP1PanelGeometryInstallTimer();
            TryInstallP1PanelGeometry();
        }
    }

    private void StopP1PanelGeometryInstallTimer()
    {
        if (_p1PanelGeometryInstallTimer == null)
            return;

        _p1PanelGeometryInstallTimer.Stop();
        _p1PanelGeometryInstallTimer.Tick -= P1PanelGeometryInstallTimer_Tick;
        _p1PanelGeometryInstallTimer = null;
    }

    private void P1DockSplitter_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_p1PanelGeometry != null)
            AnimateP1SplitterGuide(_p1PanelGeometry.DockGuide, 0.96, 105);
    }

    private void P1DockSplitter_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_p1PanelGeometry is { DockDragging: false } state)
            AnimateP1SplitterGuide(state.DockGuide, 0, 145);
    }

    private void P1DockSplitter_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (_p1PanelGeometry == null)
            return;

        _p1PanelGeometry.DockDragging = true;
        AnimateP1SplitterGuide(_p1PanelGeometry.DockGuide, 1, 65);
    }

    private void P1DockSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        var state = _p1PanelGeometry;
        if (state == null)
            return;

        state.DockDragging = false;
        if (CommandPanelExpander.IsExpanded && CommandPanelExpander.IsVisible)
        {
            var measured = state.DockRow.ActualHeight;
            if (measured > 90)
                state.DockHeightByWorkspace[MainTabs.SelectedIndex] = measured;
        }

        var target = state.DockSplitter.IsMouseOver ? 0.96 : 0;
        AnimateP1SplitterGuide(state.DockGuide, target, 130);
    }

    private void P1DockExpander_ExpandedCollapsed(object sender, RoutedEventArgs e)
    {
        if (_p1PanelGeometry != null)
            ApplyP1DockGeometry(_p1PanelGeometry, MainTabs.SelectedIndex);
    }

    private void P1DockExpander_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_p1PanelGeometry != null)
            ApplyP1DockGeometry(_p1PanelGeometry, MainTabs.SelectedIndex);
    }

    private void P1PanelGeometry_MainTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_p1PanelGeometry == null || !ReferenceEquals(e.Source, MainTabs))
            return;

        // P0 applies each workspace's expand/collapse preference on the same event. Run one
        // dispatcher turn later so row geometry follows that authoritative dock state.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (_p1PanelGeometry != null)
                ApplyP1DockGeometry(_p1PanelGeometry, MainTabs.SelectedIndex);
        }));
    }

    private void ApplyP1DockGeometry(P1PanelGeometryState state, int workspaceIndex)
    {
        var dockActive = CommandPanelExpander.IsVisible && CommandPanelExpander.IsExpanded;
        if (!dockActive)
        {
            state.DockGutterRow.Height = new GridLength(0);
            state.DockSplitter.Visibility = Visibility.Collapsed;
            state.DockGuide.Opacity = 0;
            state.DockRow.MinHeight = 0;
            state.DockRow.MaxHeight = double.PositiveInfinity;
            state.DockRow.Height = GridLength.Auto;
            return;
        }

        var availableHeight = Math.Max(520, _persistentWorkbench?.Shell.ActualHeight ?? ActualHeight);
        var preferred = state.DockHeightByWorkspace.TryGetValue(workspaceIndex, out var stored)
            ? stored
            : 224d;
        preferred = Math.Clamp(preferred, 126d, Math.Min(430d, availableHeight * 0.48d));

        state.DockGutterRow.Height = new GridLength(10);
        state.DockSplitter.Visibility = Visibility.Visible;
        state.DockRow.MinHeight = 112;
        state.DockRow.MaxHeight = Math.Min(440, availableHeight * 0.52d);
        state.DockRow.Height = new GridLength(preferred);
    }

    private void P1PanelGeometry_Closed(object? sender, EventArgs e)
    {
        var state = _p1PanelGeometry;
        StopP1PanelGeometryInstallTimer();
        if (state == null)
            return;

        state.DockSplitter.MouseEnter -= P1DockSplitter_MouseEnter;
        state.DockSplitter.MouseLeave -= P1DockSplitter_MouseLeave;
        state.DockSplitter.DragStarted -= P1DockSplitter_DragStarted;
        state.DockSplitter.DragCompleted -= P1DockSplitter_DragCompleted;
        CommandPanelExpander.Expanded -= P1DockExpander_ExpandedCollapsed;
        CommandPanelExpander.Collapsed -= P1DockExpander_ExpandedCollapsed;
        CommandPanelExpander.IsVisibleChanged -= P1DockExpander_IsVisibleChanged;
        MainTabs.SelectionChanged -= P1PanelGeometry_MainTabsSelectionChanged;
        Closed -= P1PanelGeometry_Closed;
        _p1PanelGeometry = null;
    }
}
