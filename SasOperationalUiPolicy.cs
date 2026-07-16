using System.Collections;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

/// <summary>
/// Installs the lightweight ballistic navigation treatment and enforces the operator-facing
/// SAS point profile on every SignalDefinition collection displayed by a DataGrid. The full
/// typed discovery/SCL models are not modified.
/// </summary>
internal static class SasOperationalUiPolicy
{
    private static readonly ConditionalWeakTable<object, CollectionSubscription> Subscriptions = new();
    private static readonly string[] NavigationButtonNames =
    {
        "NavExplorerButton", "NavLiveButton", "NavEventsButton", "NavGooseButton", "NavDiagnosticsButton"
    };

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnDataGridLoaded));
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
    }

    private static void OnDataGridLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not DataGrid grid || grid.ItemsSource is null)
            return;

        var source = grid.ItemsSource is ICollectionView view ? view.SourceCollection : grid.ItemsSource;
        if (source is null || !LooksLikeSignalCollection(source))
            return;

        SchedulePrune(source, grid.Dispatcher);
        if (source is INotifyCollectionChanged changed && !Subscriptions.TryGetValue(source, out _))
            Subscriptions.Add(source, new CollectionSubscription(source, changed, grid.Dispatcher));
    }

    private static bool LooksLikeSignalCollection(object source)
    {
        var type = source.GetType();
        if (type.GetInterfaces().Any(interfaceType =>
                interfaceType.IsGenericType &&
                interfaceType.GetGenericArguments().Length == 1 &&
                interfaceType.GetGenericArguments()[0] == typeof(SignalDefinition)))
            return true;

        return source is IEnumerable enumerable && enumerable.Cast<object?>().FirstOrDefault(item => item is not null) is SignalDefinition;
    }

    private static void SchedulePrune(object source, Dispatcher dispatcher)
        => dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() => Prune(source)));

    private static void Prune(object source)
    {
        if (source is not IList list || list.IsReadOnly || list.IsFixedSize)
            return;

        for (var index = list.Count - 1; index >= 0; index--)
        {
            if (list[index] is SignalDefinition signal && !SasOperationalSignalPolicy.IsVisible(signal))
                list.RemoveAt(index);
        }
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

    private sealed class CollectionSubscription
    {
        private readonly object _source;
        private readonly Dispatcher _dispatcher;
        private bool _scheduled;

        public CollectionSubscription(object source, INotifyCollectionChanged changed, Dispatcher dispatcher)
        {
            _source = source;
            _dispatcher = dispatcher;
            changed.CollectionChanged += OnCollectionChanged;
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (_scheduled)
                return;
            _scheduled = true;
            _dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
            {
                _scheduled = false;
                Prune(_source);
            }));
        }
    }
}
