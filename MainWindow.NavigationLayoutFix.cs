using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Owns the responsive geometry of the MainWindow workflow header.
///
/// The original XAML used a 760 px shell split into six equal columns while the
/// selection pill moved in hard-coded 150 px steps. That was barely large enough for
/// short labels and clipped "IEC 61850 Explorer" / "GOOSE Subscriber" once the center
/// workspace switch and live connection/status chips were also present. This behavior
/// keeps the header single-line at normal desktop sizes, deliberately compacts labels
/// at smaller widths, and derives the selection pill from the real nav cell width.
/// </summary>
internal static class MainWindowNavigationLayoutFix
{
    private const double WideBreakpoint = 1700d;
    private const double MediumBreakpoint = 1380d;
    private const double WideNavWidth = 990d;
    private const double MediumNavWidth = 900d;
    private const double CompactNavWidth = 720d;

    private static readonly string[] FullLabels =
    [
        "IEC 61850 Explorer",
        "Live Monitor",
        "Event Log",
        "Alarm Annunciator",
        "GOOSE Subscriber"
    ];

    private static readonly string[] CompactLabels =
    [
        "Explorer",
        "Live",
        "Events",
        "Alarm",
        "GOOSE"
    ];

    [ModuleInitializer]
    internal static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            Button.ClickEvent,
            new RoutedEventHandler(OnMainWindowButtonClick),
            handledEventsToo: true);
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
            return;

        // Loaded may be raised again after window hide/show (e.g. IO List FAT switch).
        // Remove first so the responsive hooks always exist exactly once.
        window.SizeChanged -= MainWindow_SizeChanged;
        window.SizeChanged += MainWindow_SizeChanged;

        if (window.FindName("MainTabs") is TabControl tabs)
        {
            tabs.SelectionChanged -= MainTabs_SelectionChanged;
            tabs.SelectionChanged += MainTabs_SelectionChanged;
        }

        ApplyResponsiveLayout(window);
        QueuePillCorrection(window, animate: false);

        // WorkspaceModeSwitch is installed by a separate Loaded class handler. Run one
        // deferred pass so its dynamically inserted controls are included regardless of
        // module/class-handler registration order.
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                ApplyResponsiveLayout(window);
                PositionPill(window, animate: false);
            }));
    }

    private static void OnMainWindowButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || e.Source is not Button button)
            return;

        if (button.Name is not ("NavExplorerButton" or "NavLiveButton" or "NavEventsButton" or "NavAlarmButton" or "NavGooseButton" or "NavDiagnosticsButton"))
            return;

        // A repeated click on the already-selected tab does not raise SelectionChanged,
        // but the legacy click handler still writes its fixed 150 px animation target.
        // Correct after the routed Click has fully returned in both cases.
        QueuePillCorrection(window, animate: true);
    }

    private static void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not MainWindow window)
            return;

        ApplyResponsiveLayout(window);
        QueuePillCorrection(window, animate: false);
    }

    private static void WorkspaceModeChild_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement element || Window.GetWindow(element) is not MainWindow window)
            return;

        // The FAT button can change to "... LOADED" after the window is already shown.
        // Re-apply the current breakpoint so that state text is compacted intentionally
        // instead of making the top bar overflow.
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => ApplyResponsiveLayout(window)));
    }

    private static void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabControl tabs || !ReferenceEquals(e.Source, tabs))
            return;

        if (Window.GetWindow(tabs) is MainWindow window)
        {
            // MainWindow.UpdateNavigationVisuals historically writes a 150 px target.
            // Correct after the originating click/selection handler has returned so the
            // responsive cell geometry remains authoritative without a large-file patch.
            QueuePillCorrection(window, animate: true);
        }
    }

    private static void ApplyResponsiveLayout(MainWindow window)
    {
        if (window.FindName("WorkflowNavShell") is not Border shell ||
            window.FindName("WorkflowNavGrid") is not Grid grid)
            return;

        var availableWidth = window.ActualWidth > 0d
            ? window.ActualWidth
            : !double.IsNaN(window.Width) && window.Width > 0d ? window.Width : 1480d;
        var wide = availableWidth >= WideBreakpoint;
        var medium = availableWidth >= MediumBreakpoint;
        var shellWidth = wide ? WideNavWidth : medium ? MediumNavWidth : CompactNavWidth;

        shell.Width = shellWidth;
        shell.MinWidth = shellWidth;
        shell.Height = 60;
        shell.Padding = new Thickness(5, 6, 5, 6);
        shell.ClipToBounds = false;
        grid.ClipToBounds = false;

        var labels = wide ? FullLabels : CompactLabels;
        var buttons = GetNavigationButtons(window);
        for (var index = 0; index < buttons.Length; index++)
        {
            var button = buttons[index];
            if (button == null)
                continue;

            // Diagnostics owns a StackPanel containing its text plus the red alert
            // badge. Never replace that Content tree while changing layout density.
            if (index < labels.Length)
                button.Content = labels[index];

            button.MinHeight = 40;
            button.MinWidth = 0;
            button.Margin = new Thickness(1);
            button.Padding = wide
                ? new Thickness(10, 7, 10, 7)
                : new Thickness(6, 7, 6, 7);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalAlignment = VerticalAlignment.Stretch;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            button.ClipToBounds = false;
        }

        UpdatePillGeometry(window, shellWidth);
        ApplyWorkspaceSwitchDensity(window, wide, medium);
    }

    private static Button?[] GetNavigationButtons(MainWindow window)
        =>
        [
            window.FindName("NavExplorerButton") as Button,
            window.FindName("NavLiveButton") as Button,
            window.FindName("NavEventsButton") as Button,
            window.FindName("NavAlarmButton") as Button,
            window.FindName("NavGooseButton") as Button,
            window.FindName("NavDiagnosticsButton") as Button
        ];

    private static void UpdatePillGeometry(MainWindow window, double shellWidth)
    {
        if (window.FindName("WorkflowPill") is not Border pill)
            return;

        // Border padding owns 10 px horizontally. The nav grid itself is divided into
        // five equal star columns, so this is the exact width used by each button cell.
        var contentWidth = Math.Max(0d, shellWidth - 10d);
        var cellWidth = contentWidth / 6d;
        pill.Width = Math.Max(1d, cellWidth - 2d);
        pill.Height = 36;
        pill.HorizontalAlignment = HorizontalAlignment.Left;
        pill.VerticalAlignment = VerticalAlignment.Center;
        pill.ClipToBounds = false;
    }

    private static void ApplyWorkspaceSwitchDensity(MainWindow window, bool wide, bool medium)
    {
        // WorkspaceModeSwitch is inserted dynamically into header column 1. At wide
        // desktop widths retain the descriptive labels. At compact widths reduce only
        // those redundant mode labels; the actual workspace functions remain present.
        if (window.Content is not Grid root)
            return;

        var header = root.Children.OfType<Grid>().FirstOrDefault(child => Grid.GetRow(child) == 0);
        if (header == null)
            return;

        var modeShell = header.Children.OfType<FrameworkElement>()
            .FirstOrDefault(child => Equals(child.Tag, "ARSAS_WORKSPACE_MODE_SWITCH")) as Border;
        if (modeShell?.Child is not StackPanel modes)
            return;

        modeShell.Margin = new Thickness(wide ? 10 : 6, 0, wide ? 10 : 6, 0);

        if (modes.Children.Count > 0 && modes.Children[0] is Border engineering &&
            engineering.Child is TextBlock engineeringText)
        {
            engineeringText.Text = medium ? "ENGINEERING" : "ENG";
            engineering.Padding = new Thickness(medium ? 12 : 9, 7, medium ? 12 : 9, 7);
        }

        if (modes.Children.Count > 1 && modes.Children[1] is Button fatButton)
        {
            fatButton.SizeChanged -= WorkspaceModeChild_SizeChanged;
            fatButton.SizeChanged += WorkspaceModeChild_SizeChanged;

            // Do not overwrite the LOADED state used by WorkspaceModeSwitch; compact it
            // while preserving that state signal.
            var loaded = fatButton.Content?.ToString()?.Contains("LOADED", StringComparison.OrdinalIgnoreCase) == true;
            fatButton.Content = medium
                ? loaded ? "IO LIST FAT · LOADED" : "IO LIST FAT"
                : loaded ? "FAT · LOADED" : "FAT";
            fatButton.Padding = new Thickness(medium ? 12 : 9, 7, medium ? 12 : 9, 7);
        }
    }

    private static void QueuePillCorrection(MainWindow window, bool animate)
    {
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => PositionPill(window, animate)));
    }

    private static void PositionPill(MainWindow window, bool animate)
    {
        if (window.FindName("WorkflowNavShell") is not Border shell ||
            window.FindName("WorkflowPill") is not Border pill ||
            window.FindName("MainTabs") is not TabControl tabs)
            return;

        var translate = window.FindName("WorkflowPillTranslate") as TranslateTransform;
        if (translate == null && pill.RenderTransform is TransformGroup group)
            translate = group.Children.OfType<TranslateTransform>().LastOrDefault();
        if (translate == null)
            return;

        var contentWidth = Math.Max(0d, shell.ActualWidth - shell.Padding.Left - shell.Padding.Right);
        if (contentWidth <= 0d)
            contentWidth = Math.Max(0d, shell.Width - shell.Padding.Left - shell.Padding.Right);

        var cellWidth = contentWidth / 6d;
        var target = Math.Clamp(tabs.SelectedIndex, 0, 5) * cellWidth;
        pill.Width = Math.Max(1d, cellWidth - 2d);

        translate.BeginAnimation(TranslateTransform.XProperty, null);
        if (!animate)
        {
            translate.X = target;
            return;
        }

        var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        translate.BeginAnimation(TranslateTransform.XProperty, animation);
    }
}
