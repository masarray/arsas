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

    // Lucide Settings/Cog geometry adapted to the application's existing 24 px
    // outline-icon convention.
    private static readonly Geometry GearGeometry = Geometry.Parse(
        "M12.22,2 H11.78 A2,2 0 0 0 9.78,4 V4.18 " +
        "A2,2 0 0 1 8.78,5.91 L8.62,6 A2,2 0 0 1 6.62,6 L6.44,5.91 " +
        "A2,2 0 0 0 3.71,6.64 L3.59,6.85 A2,2 0 0 0 4.32,9.58 L4.5,9.69 " +
        "A2,2 0 0 1 5.5,11.42 V11.58 A2,2 0 0 1 4.5,13.31 L4.32,13.42 " +
        "A2,2 0 0 0 3.59,16.15 L3.71,16.36 A2,2 0 0 0 6.44,17.09 L6.62,17 " +
        "A2,2 0 0 1 8.62,17 L8.78,17.09 A2,2 0 0 1 9.78,18.82 V19 " +
        "A2,2 0 0 0 11.78,21 H12.22 A2,2 0 0 0 14.22,19 V18.82 " +
        "A2,2 0 0 1 15.22,17.09 L15.38,17 A2,2 0 0 1 17.38,17 L17.56,17.09 " +
        "A2,2 0 0 0 20.29,16.36 L20.41,16.15 A2,2 0 0 0 19.68,13.42 L19.5,13.31 " +
        "A2,2 0 0 1 18.5,11.58 V11.42 A2,2 0 0 1 19.5,9.69 L19.68,9.58 " +
        "A2,2 0 0 0 20.41,6.85 L20.29,6.64 A2,2 0 0 0 17.56,5.91 L17.38,6 " +
        "A2,2 0 0 1 15.38,6 L15.22,5.91 A2,2 0 0 1 14.22,4.18 V4 " +
        "A2,2 0 0 0 12.22,2 Z M12,15 A3,3 0 1 0 12,9 A3,3 0 0 0 12,15 Z");

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
            StrokeThickness = 1.8,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.Uniform
        };

        button.Content = new Viewbox
        {
            Width = 13,
            Height = 13,
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
