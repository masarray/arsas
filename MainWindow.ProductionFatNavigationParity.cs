using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Extends the existing six-destination responsive nav contract to the dynamically-added
/// production FAT destination. The legacy MainWindow navigation code clamps index 6 to 5;
/// this late correction keeps Diagnostics from remaining highlighted while FAT is active
/// and moves the same selection pill into the seventh equal-width cell.
/// </summary>
internal static class MainWindowProductionFatNavigationParity
{
    private static readonly string[] NavigationButtonNames =
    [
        "NavExplorerButton",
        "NavLiveButton",
        "NavEventsButton",
        "NavAlarmButton",
        "NavGooseButton",
        "NavDiagnosticsButton",
        "NavNativeFatButton"
    ];

    [ModuleInitializer]
    internal static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            Button.ClickEvent,
            new RoutedEventHandler(OnButtonClick),
            handledEventsToo: true);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
            return;

        window.SizeChanged -= Window_SizeChanged;
        window.SizeChanged += Window_SizeChanged;
        if (window.FindName("MainTabs") is TabControl tabs)
        {
            tabs.SelectionChanged -= Tabs_SelectionChanged;
            tabs.SelectionChanged += Tabs_SelectionChanged;
        }
        Queue(window, animate: false);
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || e.Source is not Button button ||
            !NavigationButtonNames.Contains(button.Name, StringComparer.Ordinal))
        {
            return;
        }
        Queue(window, animate: true);
    }

    private static void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabControl tabs || !ReferenceEquals(e.Source, tabs) ||
            Window.GetWindow(tabs) is not MainWindow window)
        {
            return;
        }
        Queue(window, animate: true);
    }

    private static void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is MainWindow window)
            Queue(window, animate: false);
    }

    private static void Queue(MainWindow window, bool animate)
    {
        // Existing MainWindow + responsive-layout handlers still perform their historical
        // six-slot correction. ApplicationIdle deliberately runs after those handlers so
        // the seven-slot Engineering shell is the final visual authority.
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => Apply(window, animate)));
    }

    private static void Apply(MainWindow window, bool animate)
    {
        if (window.FindName("MainTabs") is not TabControl tabs || tabs.Items.Count < 7 ||
            window.FindName("WorkflowNavGrid") is not Grid grid ||
            window.FindName("WorkflowNavShell") is not Border shell ||
            window.FindName("WorkflowPill") is not Border pill)
        {
            return;
        }

        var fatButton = grid.Children
            .OfType<Button>()
            .FirstOrDefault(button => button.Name.Equals("NavNativeFatButton", StringComparison.Ordinal));
        var buttons = new Button?[]
        {
            window.FindName("NavExplorerButton") as Button,
            window.FindName("NavLiveButton") as Button,
            window.FindName("NavEventsButton") as Button,
            window.FindName("NavAlarmButton") as Button,
            window.FindName("NavGooseButton") as Button,
            window.FindName("NavDiagnosticsButton") as Button,
            fatButton
        };
        if (buttons.Any(button => button == null))
            return;

        while (grid.ColumnDefinitions.Count < 7)
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        foreach (var column in grid.ColumnDefinitions.Take(7))
            column.Width = new GridLength(1, GridUnitType.Star);

        // FAT must use the exact same button style and density as its siblings.
        var reference = buttons[5]!;
        var muted = window.TryFindResource("Muted") as Brush ?? new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A));
        var selectedIndex = Math.Clamp(tabs.SelectedIndex, 0, 6);
        for (var index = 0; index < buttons.Length; index++)
        {
            var button = buttons[index]!;
            button.Style ??= reference.Style;
            button.MinHeight = reference.MinHeight;
            button.MinWidth = 0;
            button.Margin = reference.Margin;
            button.Padding = reference.Padding;
            button.HorizontalContentAlignment = reference.HorizontalContentAlignment;
            button.VerticalAlignment = reference.VerticalAlignment;
            button.VerticalContentAlignment = reference.VerticalContentAlignment;
            button.Foreground = index == selectedIndex ? Brushes.White : muted;
        }

        var contentWidth = Math.Max(0d, shell.ActualWidth - shell.Padding.Left - shell.Padding.Right);
        if (contentWidth <= 0d)
            contentWidth = Math.Max(0d, shell.Width - shell.Padding.Left - shell.Padding.Right);
        var cellWidth = contentWidth / 7d;
        pill.Width = Math.Max(1d, cellWidth - 2d);
        pill.Height = 36;

        var translate = window.FindName("WorkflowPillTranslate") as TranslateTransform;
        if (translate == null && pill.RenderTransform is TransformGroup group)
            translate = group.Children.OfType<TranslateTransform>().LastOrDefault();
        if (translate == null)
            return;

        var target = selectedIndex * cellWidth;
        translate.BeginAnimation(TranslateTransform.XProperty, null);
        if (!animate)
        {
            translate.X = target;
            return;
        }

        translate.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(190))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            });
    }
}
