using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Lightweight presentation policy for the two operator signal viewers, ballistic navigation
/// spacing, and the existing position-action button visuals. Runtime behavior is unchanged.
/// </summary>
internal static class SignalViewerAndControlUxPolicy
{
    private static readonly Lazy<Style> OpenCommandStyle = new(() => BuildCommandStyle(
        start: "#158A57",
        middle: "#075D3C",
        end: "#06472F",
        border: "#63C69A",
        shadow: "#075D3C"));

    private static readonly Lazy<Style> CloseCommandStyle = new(() => BuildCommandStyle(
        start: "#D34747",
        middle: "#A6242B",
        end: "#781B22",
        border: "#F08B8B",
        shadow: "#8F2028"));

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
            new Action(() => ApplyNavigationSpacing(window)));
    }

    private static void ApplyNavigationSpacing(Window window)
    {
        if (window.FindName("WorkflowNavShell") is Border shell)
        {
            shell.Width = 672;
            shell.Padding = new Thickness(5);
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

            button.Margin = new Thickness(4, 1, 4, 1);
            button.Padding = new Thickness(11, 0, 11, 0);
        }
    }

    private static void OnButtonLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Button button || button.CommandParameter is not string parameter)
            return;

        if (string.Equals(parameter, "Open [01]", StringComparison.OrdinalIgnoreCase))
        {
            button.Style = OpenCommandStyle.Value;
            return;
        }

        if (string.Equals(parameter, "Closed [10]", StringComparison.OrdinalIgnoreCase))
            button.Style = CloseCommandStyle.Value;
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
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 64d));
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
                    <DropShadowEffect BlurRadius="11" ShadowDepth="3" Opacity="0.22" Color="{{shadow}}"/>
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
                  <Setter TargetName="InteractionSurface" Property="Background" Value="#26FFFFFF"/>
                  <Setter TargetName="InteractionSurface" Property="BorderBrush" Value="#66FFFFFF"/>
                </Trigger>
                <Trigger Property="IsPressed" Value="True">
                  <Setter TargetName="Chrome" Property="Opacity" Value="0.82"/>
                  <Setter TargetName="InteractionSurface" Property="Background" Value="#16FFFFFF"/>
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
