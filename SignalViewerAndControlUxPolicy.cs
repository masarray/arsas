using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Presentation-only policy for dense signal viewers, the workflow navigation shell,
/// command actions, and compact IED-card visuals. IEC 61850 runtime behavior is unchanged.
/// </summary>
internal static class SignalViewerAndControlUxPolicy
{
    private static readonly Lazy<Style> OpenCommandStyle = new(() => BuildCommandStyle(
        start: "#178A58",
        middle: "#087247",
        end: "#045434",
        border: "#69D2A1",
        shadow: "#075D3C"));

    private static readonly Lazy<Style> CloseCommandStyle = new(() => BuildCommandStyle(
        start: "#E04B4B",
        middle: "#B6252D",
        end: "#7E1720",
        border: "#FF9A9A",
        shadow: "#8F2028"));

    private static readonly Lazy<Style> TransparentIedIconButtonStyle =
        new(BuildTransparentIedIconButtonStyle);

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnDataGridLoaded));
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnButtonLoaded));
        EventManager.RegisterClassHandler(
            typeof(Path),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnPathLoaded));
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
    }

    private static void OnDataGridLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not DataGrid grid)
            return;

        var bindingPath = grid.GetBindingExpression(ItemsControl.ItemsSourceProperty)?.ParentBinding.Path?.Path;
        if (!string.Equals(bindingPath, "SelectedDevice.Points", StringComparison.Ordinal) &&
            !string.Equals(bindingPath, "GlobalPoints", StringComparison.Ordinal))
        {
            return;
        }

        grid.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => ApplySignalViewerLayout(grid, bindingPath)));
    }

    private static void ApplySignalViewerLayout(DataGrid grid, string bindingPath)
    {
        ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(grid, ScrollBarVisibility.Auto);
        grid.CanUserResizeColumns = false;
        grid.ClipToBounds = true;

        if (string.Equals(bindingPath, "SelectedDevice.Points", StringComparison.Ordinal))
        {
            SetColumn(grid, "Signal", 1.05, 120);
            SetColumn(grid, "IEC Telegram", 1.55, 180);
            SetColumn(grid, "Value", 0.72, 90);
            SetColumn(grid, "Quality", 0.62, 82);
            SetColumn(grid, "IED Timestamp", 1.05, 135);
            SetColumn(grid, "Acquisition", 1.00, 130);
            return;
        }

        SetColumn(grid, "IED", 0.62, 90);
        SetColumn(grid, "Signal", 1.05, 145);
        SetColumn(grid, "IEC Telegram", 1.72, 220);
        SetColumn(grid, "Value", 0.68, 86);
        SetColumn(grid, "Quality", 0.72, 88);
        SetColumn(grid, "IED Timestamp", 1.02, 140);
        SetColumn(grid, "Acquisition", 1.00, 135);
    }

    private static void SetColumn(DataGrid grid, string header, double star, double minimum)
    {
        var column = grid.Columns.FirstOrDefault(candidate =>
            string.Equals(candidate.Header?.ToString(), header, StringComparison.Ordinal));
        if (column is null)
            return;

        column.MinWidth = minimum;
        column.Width = new DataGridLength(star, DataGridLengthUnitType.Star);
        column.CanUserResize = false;
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Window window || window.GetType().Name != "MainWindow")
            return;

        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                ApplyNavigationSpacing(window);
                ApplyWindowLayoutSafety(window);
                ApplyLoadedVisualPolicies(window);
            }));
    }

    private static void ApplyNavigationSpacing(Window window)
    {
        if (window.FindName("WorkflowNavShell") is Border shell)
        {
            shell.Width = 672;
            shell.Height = 46;
            shell.Padding = new Thickness(5);
            shell.ClipToBounds = false;
        }

        if (window.FindName("WorkflowNavGrid") is Grid navGrid)
            navGrid.ClipToBounds = false;

        if (window.FindName("WorkflowPill") is Border pill)
        {
            pill.Height = 34;
            pill.VerticalAlignment = VerticalAlignment.Center;
            pill.ClipToBounds = false;
        }

        foreach (var name in new[]
                 {
                     "NavExplorerButton",
                     "NavLiveButton",
                     "NavEventsButton",
                     "NavDiagnosticsButton"
                 })
        {
            if (window.FindName(name) is not Button button)
                continue;

            button.Margin = new Thickness(4, 2, 4, 2);
            button.Padding = new Thickness(11, 0, 11, 0);
            button.ClipToBounds = false;
        }
    }

    private static void ApplyWindowLayoutSafety(Window window)
    {
        window.MinHeight = Math.Max(window.MinHeight, 780);

        if (window.Content is not Grid root)
            return;

        var margin = root.Margin;
        root.Margin = new Thickness(
            margin.Left,
            margin.Top,
            margin.Right,
            Math.Max(margin.Bottom, 16));
        root.ClipToBounds = false;
    }

    private static void OnButtonLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Button button)
            return;

        button.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => ApplyButtonPresentation(button)));
    }

    private static void ApplyButtonPresentation(Button button)
    {
        var parameter = button.CommandParameter?.ToString();
        if (string.Equals(parameter, "Open [01]", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parameter, "False", StringComparison.OrdinalIgnoreCase))
        {
            button.Style = OpenCommandStyle.Value;
            return;
        }

        if (string.Equals(parameter, "Closed [10]", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parameter, "True", StringComparison.OrdinalIgnoreCase))
        {
            button.Style = CloseCommandStyle.Value;
            return;
        }

        if (Application.Current?.TryFindResource("IedIconButton") is not Style originalStyle ||
            !ReferenceEquals(button.Style, originalStyle))
        {
            return;
        }

        button.Style = TransparentIedIconButtonStyle.Value;
        ApplyIedActionIconTone(button);
    }

    private static void ApplyIedActionIconTone(Button button)
    {
        var path = FindVisualDescendant<Path>(button);
        if (path is null)
            return;

        if (GeometryMatches(path, "LucideSquare"))
        {
            path.Stroke = Brush("#D97706");
            return;
        }

        if (GeometryMatches(path, "LucideX"))
        {
            path.Stroke = Brush("#DC2626");
            return;
        }

        if (GeometryMatches(path, "LucidePlay"))
            path.Stroke = Brush("#16A34A");
    }

    private static bool GeometryMatches(Path path, string resourceKey)
    {
        if (Application.Current?.TryFindResource(resourceKey) is not Geometry target ||
            path.Data is null)
        {
            return false;
        }

        return ReferenceEquals(path.Data, target) ||
               string.Equals(path.Data.ToString(), target.ToString(), StringComparison.Ordinal);
    }

    private static void OnPathLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Path path ||
            !string.Equals(path.Name, "RelayDeviceIcon", StringComparison.Ordinal))
        {
            return;
        }

        path.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => ApplyRelayIconLayout(path)));
    }

    private static void ApplyRelayIconLayout(Path path)
    {
        path.Stretch = Stretch.Uniform;

        if (path.Effect is DropShadowEffect shadow)
        {
            var compactShadow = shadow.IsFrozen ? (DropShadowEffect)shadow.CloneCurrentValue() : shadow;
            compactShadow.BlurRadius = 8;
            compactShadow.Opacity = 0.38;
            compactShadow.ShadowDepth = 0;

            if (!ReferenceEquals(compactShadow, shadow))
                path.Effect = compactShadow;
        }

        if (VisualTreeHelper.GetParent(path) is not Viewbox viewbox)
            return;

        viewbox.Width = 46;
        viewbox.Height = 46;
        viewbox.Stretch = Stretch.Uniform;
        viewbox.HorizontalAlignment = HorizontalAlignment.Center;
        viewbox.VerticalAlignment = VerticalAlignment.Center;

        if (VisualTreeHelper.GetParent(viewbox) is Grid iconHost)
        {
            iconHost.Width = 54;
            iconHost.Height = 54;
            iconHost.Margin = new Thickness(0, 0, 6, 0);
            iconHost.ClipToBounds = false;
        }
    }

    private static void ApplyLoadedVisualPolicies(DependencyObject root)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);

            if (child is Button button)
                ApplyButtonPresentation(button);
            else if (child is Path path &&
                     string.Equals(path.Name, "RelayDeviceIcon", StringComparison.Ordinal))
                ApplyRelayIconLayout(path);

            ApplyLoadedVisualPolicies(child);
        }
    }

    private static T? FindVisualDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                return match;

            var nested = FindVisualDescendant<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static Style BuildTransparentIedIconButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 31d));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 31d));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        style.Setters.Add(new Setter(UIElement.FocusableProperty, false));
        style.Setters.Add(new Setter(Control.TemplateProperty, BuildTransparentIedIconButtonTemplate()));
        style.Seal();
        return style;
    }

    private static ControlTemplate BuildTransparentIedIconButtonTemplate()
    {
        const string template = """
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             TargetType="{x:Type Button}">
              <Border x:Name="Chrome"
                      Background="{TemplateBinding Background}"
                      BorderBrush="{TemplateBinding BorderBrush}"
                      BorderThickness="{TemplateBinding BorderThickness}"
                      CornerRadius="9">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                  <Setter TargetName="Chrome" Property="Background" Value="#CFFFFFFF"/>
                  <Setter TargetName="Chrome" Property="BorderBrush" Value="#8EAFE3"/>
                </Trigger>
                <Trigger Property="IsPressed" Value="True">
                  <Setter TargetName="Chrome" Property="Background" Value="#A6DCEBFF"/>
                  <Setter TargetName="Chrome" Property="Opacity" Value="0.84"/>
                </Trigger>
                <Trigger Property="IsEnabled" Value="False">
                  <Setter TargetName="Chrome" Property="Opacity" Value="0.32"/>
                  <Setter Property="Cursor" Value="Arrow"/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """;
        return (ControlTemplate)XamlReader.Parse(template);
    }

    private static Style BuildCommandStyle(
        string start,
        string middle,
        string end,
        string border,
        string shadow)
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Gradient(start, middle, end)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush(border)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(15, 7, 15, 7)));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 66d));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 33d));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        style.Setters.Add(new Setter(Control.TemplateProperty, BuildCommandTemplate(shadow)));
        style.Seal();
        return style;
    }

    private static ControlTemplate BuildCommandTemplate(string shadow)
    {
        var template = $$"""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             TargetType="{x:Type Button}">
              <Grid SnapsToDevicePixels="True">
                <Border x:Name="Chrome"
                        Background="{TemplateBinding Background}"
                        BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}"
                        CornerRadius="11">
                  <Border.Effect>
                    <DropShadowEffect BlurRadius="12" ShadowDepth="3" Opacity="0.26" Color="{{shadow}}"/>
                  </Border.Effect>
                  <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"
                                    Margin="{TemplateBinding Padding}"
                                    TextElement.Foreground="{TemplateBinding Foreground}"/>
                </Border>
                <Border x:Name="InteractionSurface" Background="Transparent" CornerRadius="11"
                        BorderBrush="Transparent" BorderThickness="1" IsHitTestVisible="False"/>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                  <Setter TargetName="InteractionSurface" Property="Background" Value="#2FFFFFFF"/>
                  <Setter TargetName="InteractionSurface" Property="BorderBrush" Value="#8AFFFFFF"/>
                </Trigger>
                <Trigger Property="IsPressed" Value="True">
                  <Setter TargetName="Chrome" Property="Opacity" Value="0.80"/>
                  <Setter TargetName="InteractionSurface" Property="Background" Value="#18FFFFFF"/>
                </Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True">
                  <Setter TargetName="InteractionSurface" Property="BorderBrush" Value="White"/>
                  <Setter TargetName="InteractionSurface" Property="BorderThickness" Value="2"/>
                </Trigger>
                <Trigger Property="IsEnabled" Value="False">
                  <Setter TargetName="Chrome" Property="Opacity" Value="0.38"/>
                  <Setter Property="Cursor" Value="Arrow"/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """;
        return (ControlTemplate)XamlReader.Parse(template);
    }

    private static LinearGradientBrush Gradient(string start, string middle, string end)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop(Color(start), 0));
        brush.GradientStops.Add(new GradientStop(Color(middle), 0.58));
        brush.GradientStops.Add(new GradientStop(Color(end), 1));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush(Color(color));
        brush.Freeze();
        return brush;
    }

    private static Color Color(string value)
        => (Color)ColorConverter.ConvertFromString(value);
}
