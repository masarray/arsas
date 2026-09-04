using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Adds a dedicated Lucide-style gear button to every IED card. The existing pencil
/// remains the direct Edit Signals shortcut; the gear is the explicit entry point for
/// the reusable IED Actions chooser (Static DataSet, Select Signals, RCB Engineering,
/// COMTRADE and Browse Offline).
/// </summary>
internal static class IedCardActionsGearPolicy
{
    private const string GearUid = "ARSAS.IedActionsGear";
    private const string GearToolTip = "IED Actions — Static DataSet, Select Signals, RCB Engineering, COMTRADE, Browse Offline";
    private const double GearOpticalSize = 16d;

    // Exact Lucide Settings outline supplied by the product owner, translated from the
    // SVG 24x24 path into equivalent absolute WPF arc commands. The center circle is
    // the SVG <circle cx="12" cy="12" r="3"/> expressed as a second geometry figure.
    // Keeping the canonical Lucide proportions plus a 16 px optical box prevents this
    // circular icon from looking smaller than the neighboring 14 px Play/Pencil/Save glyphs.
    private static readonly Geometry GearGeometry = Geometry.Parse(
        "M9.671,4.136 " +
        "A2.34,2.34 0 0 1 14.33,4.136 " +
        "A2.34,2.34 0 0 0 17.649,6.051 " +
        "A2.34,2.34 0 0 1 19.979,10.084 " +
        "A2.34,2.34 0 0 0 19.979,13.915 " +
        "A2.34,2.34 0 0 1 17.649,17.948 " +
        "A2.34,2.34 0 0 0 14.33,19.863 " +
        "A2.34,2.34 0 0 1 9.671,19.863 " +
        "A2.34,2.34 0 0 0 6.351,17.948 " +
        "A2.34,2.34 0 0 1 4.021,13.915 " +
        "A2.34,2.34 0 0 0 4.021,10.084 " +
        "A2.34,2.34 0 0 1 6.35,6.051 " +
        "A2.34,2.34 0 0 0 9.671,4.136 " +
        "M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12");

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(ListBoxItem),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnIedCardLoaded));
    }

    private static void OnIedCardLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not ListBoxItem item || item.DataContext is not Iec61850MonitorDevice)
            return;

        // Wait until the DataTemplate and its final card width are both available.
        // The former implementation injected a sixth fixed-width button into a
        // five-slot bar; on the compact IED card that placed the gear beyond the
        // visible card edge even though the button existed in the visual tree.
        item.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => EnsureGearButton(item)));
    }

    private static void EnsureGearButton(ListBoxItem item)
    {
        if (item.DataContext is not Iec61850MonitorDevice device)
            return;

        var actionBar = FindVisualChildren<UniformGrid>(item)
            .FirstOrDefault(grid =>
            {
                var buttons = grid.Children.OfType<Button>().ToArray();
                return buttons.Length >= 3 &&
                       buttons.All(button => button.Tag is Iec61850MonitorDevice) &&
                       buttons.Any(button => ReferenceEquals(button.Tag, device));
            });

        if (actionBar == null)
            return;

        if (actionBar.Children.OfType<Button>().All(button => button.Uid != GearUid))
            actionBar.Children.Add(CreateGearButton(device));

        NormalizeActionBar(actionBar);
    }

    private static Button CreateGearButton(Iec61850MonitorDevice device)
    {
        var button = new Button
        {
            Uid = GearUid,
            Width = double.NaN,
            MinWidth = 0,
            Height = 27,
            Margin = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Tag = device,
            ToolTip = GearToolTip,
            FocusVisualStyle = null
        };

        if (Application.Current.TryFindResource("IedIconButton") is Style buttonStyle)
            button.Style = buttonStyle;

        // Local layout values deliberately override the style's 31 px fixed width.
        // Every action gets one equal-width UniformGrid cell, so six actions remain
        // inside the compact card instead of clipping the gear at the right edge.
        button.Width = double.NaN;
        button.MinWidth = 0;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;

        var iconStroke = new SolidColorBrush(Color.FromRgb(49, 93, 191));
        iconStroke.Freeze();
        var icon = new System.Windows.Shapes.Path
        {
            Data = GearGeometry,
            Stroke = iconStroke,
            Fill = Brushes.Transparent,
            StrokeThickness = 2d,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };

        button.Content = new Viewbox
        {
            Width = GearOpticalSize,
            Height = GearOpticalSize,
            Stretch = Stretch.Uniform,
            Child = icon
        };
        button.Click += GearButton_Click;
        return button;
    }

    private static void NormalizeActionBar(UniformGrid actionBar)
    {
        var buttons = actionBar.Children.OfType<Button>().ToArray();
        if (buttons.Length == 0)
            return;

        actionBar.Rows = 1;
        actionBar.Columns = buttons.Length;
        actionBar.HorizontalAlignment = HorizontalAlignment.Stretch;

        foreach (var button in buttons)
        {
            button.Width = double.NaN;
            button.MinWidth = 0;
            button.Height = 27;
            button.Margin = new Thickness(0);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        actionBar.InvalidateMeasure();
        actionBar.InvalidateArrange();
        actionBar.UpdateLayout();
    }

    private static async void GearButton_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button button || button.Tag is not Iec61850MonitorDevice device)
            return;

        if (Window.GetWindow(button) is not MainWindow mainWindow)
            return;

        args.Handled = true;
        await mainWindow.OpenIedWorkspaceActionsAsync(device);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
