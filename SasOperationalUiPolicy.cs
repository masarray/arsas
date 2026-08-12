using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Installs the lightweight ballistic navigation treatment. Discovered signal collections
/// are deliberately never filtered or mutated here: presentation policy must not delete
/// measurement, protection, or vendor-specific points from the live IEC 61850 model.
/// </summary>
internal static class SasOperationalUiPolicy
{
    private static readonly string[] NavigationButtonNames =
    {
        "NavExplorerButton", "NavLiveButton", "NavEventsButton", "NavGooseButton", "NavDiagnosticsButton"
    };

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Window window || window.GetType().Name != "MainWindow")
            return;

        window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ApplyBallisticNavigation(window)));
    }

    private static void ApplyBallisticNavigation(Window window)
    {
        if (window.FindName("WorkflowNavShell") is not Border shell)
            return;

        shell.Width = 760;
        shell.Height = 56;
        shell.Padding = new Thickness(5);
        shell.CornerRadius = new CornerRadius(20);
        shell.Background = Brush("#D8E2F0");
        shell.BorderBrush = Brush("#B7C6DA");
        shell.BorderThickness = new Thickness(1);
        shell.ClipToBounds = false;

        if (window.FindName("WorkflowPill") is UIElement legacyPill)
            legacyPill.Visibility = Visibility.Collapsed;
        if (window.FindName("WorkflowNavGrid") is Grid navGrid)
            navGrid.ClipToBounds = false;

        var buttons = NavigationButtonNames
            .Select(name => window.FindName(name) as Button)
            .Where(button => button is not null)
            .Cast<Button>()
            .ToArray();
        if (buttons.Length == 0)
            return;

        var template = BuildBallisticTemplate();
        foreach (var button in buttons)
        {
            button.Template = template;
            button.Height = 38;
            button.Margin = new Thickness(2);
            button.Padding = new Thickness(12, 0, 12, 0);
            button.BorderThickness = new Thickness(1);
            button.Cursor = Cursors.Hand;
            button.FontSize = 12.8;
            button.PreviewMouseLeftButtonUp -= OnNavigationClick;
            button.PreviewMouseLeftButtonUp += OnNavigationClick;
        }

        if (window.FindName("MainTabs") is TabControl tabs)
        {
            tabs.SelectionChanged -= OnTabSelectionChanged;
            tabs.SelectionChanged += OnTabSelectionChanged;
            UpdateNavigation(buttons, tabs.SelectedIndex, pulse: false);
        }
        else
        {
            UpdateNavigation(buttons, 0, pulse: false);
        }
    }

    private static void OnTabSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (sender is not TabControl tabs || !ReferenceEquals(args.Source, tabs))
            return;

        var window = Window.GetWindow(tabs);
        if (window is null)
            return;
        var buttons = NavigationButtonNames
            .Select(name => window.FindName(name) as Button)
            .Where(button => button is not null)
            .Cast<Button>()
            .ToArray();
        UpdateNavigation(buttons, tabs.SelectedIndex, pulse: true);
    }

    private static void OnNavigationClick(object sender, MouseButtonEventArgs args)
    {
        if (sender is Button button)
            Pulse(button);
    }

    private static void UpdateNavigation(IReadOnlyList<Button> buttons, int selectedIndex, bool pulse)
    {
        selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, buttons.Count - 1));
        for (var index = 0; index < buttons.Count; index++)
        {
            var selected = index == selectedIndex;
            buttons[index].BeginAnimation(UIElement.OpacityProperty, null);
            buttons[index].Opacity = 1;
            buttons[index].Foreground = selected ? Brushes.White : Brush("#42526B");
            buttons[index].Background = selected ? AccentGradient() : Brush("#FBFDFF");
            buttons[index].BorderBrush = selected ? Brush("#7FAAFF") : Brush("#B7C6DA");
            buttons[index].FontWeight = selected ? FontWeights.SemiBold : FontWeights.Medium;
            buttons[index].Effect = null;
        }

        if (pulse && buttons.Count > 0)
            Pulse(buttons[selectedIndex]);
    }

    private static void Pulse(Button button)
        => button.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0.76, 1, TimeSpan.FromMilliseconds(82))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            },
            HandoffBehavior.SnapshotAndReplace);

    private static ControlTemplate BuildBallisticTemplate()
    {
        const string template = """
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             TargetType="{x:Type Button}">
              <Grid SnapsToDevicePixels="True">
                <Border x:Name="Chrome"
                        Background="{TemplateBinding Background}"
                        BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}"
                        CornerRadius="14"/>
                <Border x:Name="InteractionSurface" Background="Transparent"
                        BorderBrush="Transparent" BorderThickness="1" CornerRadius="14"
                        IsHitTestVisible="False"/>
                <ContentPresenter x:Name="Label" HorizontalAlignment="Center" VerticalAlignment="Center"
                                  Margin="{TemplateBinding Padding}" RecognizesAccessKey="True"
                                  TextElement.Foreground="{TemplateBinding Foreground}"/>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                  <Setter TargetName="InteractionSurface" Property="Background" Value="#247AA7E8"/>
                  <Setter TargetName="InteractionSurface" Property="BorderBrush" Value="#88AFC7E8"/>
                </Trigger>
                <Trigger Property="IsPressed" Value="True">
                  <Setter TargetName="InteractionSurface" Property="Background" Value="#4874A8EA"/>
                  <Setter TargetName="Label" Property="Opacity" Value="0.80"/>
                </Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True">
                  <Setter TargetName="InteractionSurface" Property="BorderBrush" Value="#3B82F6"/>
                  <Setter TargetName="InteractionSurface" Property="BorderThickness" Value="2"/>
                </Trigger>
                <Trigger Property="IsEnabled" Value="False">
                  <Setter Property="Opacity" Value="0.45"/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """;
        return (ControlTemplate)XamlReader.Parse(template);
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush AccentGradient()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#3B82F6"), 0));
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#4F46E5"), 1));
        brush.Freeze();
        return brush;
    }

}
