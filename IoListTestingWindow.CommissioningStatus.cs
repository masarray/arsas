using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester;

public partial class IoListTestingWindow
{
    private DispatcherTimer? _commissioningConnectionBadgeTimer;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (_commissioningConnectionBadgeTimer != null)
            return;

        _commissioningConnectionBadgeTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _commissioningConnectionBadgeTimer.Tick += CommissioningConnectionBadgeTimer_Tick;
        _commissioningConnectionBadgeTimer.Start();
        Closed += (_, _) =>
        {
            if (_commissioningConnectionBadgeTimer == null)
                return;
            _commissioningConnectionBadgeTimer.Stop();
            _commissioningConnectionBadgeTimer.Tick -= CommissioningConnectionBadgeTimer_Tick;
            _commissioningConnectionBadgeTimer = null;
        };
    }

    private void CommissioningConnectionBadgeTimer_Tick(object? sender, EventArgs e)
    {
        foreach (var plan in FatIedList.Items.OfType<IoTestIedPlan>())
        {
            if (FatIedList.ItemContainerGenerator.ContainerFromItem(plan) is not ListBoxItem container)
                continue;

            var badge = FindNamedVisual<Border>(container, "StateBadge");
            var text = FindNamedVisual<TextBlock>(container, "StateText");
            if (badge == null || text == null)
                continue;

            var state = plan.IsPreparing
                ? "CONNECTING"
                : plan.IsLiveConnected
                    ? "ONLINE"
                    : plan.IsLiveMonitoring
                        ? "RECONNECTING"
                        : "OFFLINE";

            var palette = state switch
            {
                "ONLINE" => new BadgePalette("#EAF8F1", "#8FD1B1", "#16845A"),
                "RECONNECTING" => new BadgePalette("#FFF8E8", "#E9C46A", "#A56D00"),
                "CONNECTING" => new BadgePalette("#EDF3FF", "#9CB8F5", "#315DBF"),
                _ => new BadgePalette("#FFF1F2", "#F0B7BC", "#C53A45")
            };

            // Connection health is independent from FAT PASS/FAIL. A previous XAML
            // visibility binding hid StateBadge when all rows passed; a local value here
            // intentionally keeps ONLINE / RECONNECTING / OFFLINE visible at all times.
            badge.Visibility = Visibility.Visible;
            badge.Background = BrushFromHex(palette.Background);
            badge.BorderBrush = BrushFromHex(palette.Border);
            badge.ToolTip = string.IsNullOrWhiteSpace(plan.LiveStatusText)
                ? state
                : $"{state} · {plan.LiveStatusText}";
            text.Text = state;
            text.Foreground = BrushFromHex(palette.Foreground);

            if (FindNamedVisual<Control>(container, "RelayIcon") is { } relayIcon)
                relayIcon.Foreground = BrushFromHex(palette.Foreground);
        }
    }

    private static T? FindNamedVisual<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T element && element.Name.Equals(name, StringComparison.Ordinal))
                return element;
            var nested = FindNamedVisual<T>(child, name);
            if (nested != null)
                return nested;
        }
        return null;
    }

    private static Brush BrushFromHex(string value)
        => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));

    private sealed record BadgePalette(string Background, string Border, string Foreground);
}
