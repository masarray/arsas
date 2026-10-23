using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

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
            // Older/saved FAT workspaces can contain a complete generic Value 1 / Value 2
            // pair while Runtime.State still reflects the earlier transition-only contract.
            // Re-assess only those complete generic digital pairs; raw evidence is untouched.
            RefreshCurrentPairVerdicts(plan);

            if (FatIedList.ItemContainerGenerator.ContainerFromItem(plan) is not ListBoxItem container)
                continue;

            var badge = FindNamedVisual<Border>(container, "StateBadge");
            var text = FindNamedVisual<TextBlock>(container, "StateText");
            if (badge == null || text == null)
                continue;

            // The FAT card must reflect the live Engineering runtime, not the last copied
            // IoTestIedPlan flags. That removes the old Refresh dependency after an FO/network
            // loss: as soon as the monitor marks the transport down, the card follows it.
            var device = ResolveCommissioningRuntimeDevice(plan);
            var state = plan.IsPreparing
                ? "CONNECTING"
                : device != null
                    ? device.IsConnected
                        ? "ONLINE"
                        : device.IsMonitoring
                            ? "RECONNECTING"
                            : "OFFLINE"
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
            badge.Background = ConnectionBadgeBrushFromHex(palette.Background);
            badge.BorderBrush = ConnectionBadgeBrushFromHex(palette.Border);
            var liveDetail = device == null ? plan.LiveStatusText : device.Status;
            badge.ToolTip = string.IsNullOrWhiteSpace(liveDetail)
                ? state
                : $"{state} · {liveDetail}";
            text.Text = state;
            text.Foreground = ConnectionBadgeBrushFromHex(palette.Foreground);

            if (FindNamedVisual<Control>(container, "RelayIcon") is { } relayIcon)
                relayIcon.Foreground = ConnectionBadgeBrushFromHex(palette.Foreground);
        }

        // Keep Boolean presentation canonical without rewriting relay evidence or persisted
        // RawValue. SetCurrentValue preserves the existing WPF binding, so a new sample can
        // still replace the cell normally on the next runtime update.
        if (_fatSignalsGrid != null)
            NormalizeFatBooleanPresentation(_fatSignalsGrid);
    }

    private Iec61850MonitorDevice? ResolveCommissioningRuntimeDevice(IoTestIedPlan plan)
    {
        if (Owner is not MainWindow engineeringWindow)
            return null;

        if (!string.IsNullOrWhiteSpace(plan.LiveDeviceId))
        {
            var byId = engineeringWindow.Devices.FirstOrDefault(device =>
                device.DeviceId.Equals(plan.LiveDeviceId, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
                return byId;
        }

        return engineeringWindow.Devices.FirstOrDefault(device =>
                   device.IpAddress.Equals(plan.IpAddress, StringComparison.OrdinalIgnoreCase) &&
                   (device.Name.Equals(plan.IedName, StringComparison.OrdinalIgnoreCase) ||
                    device.SclIedName.Equals(plan.IedName, StringComparison.OrdinalIgnoreCase)))
               ?? engineeringWindow.Devices.FirstOrDefault(device =>
                   device.IpAddress.Equals(plan.IpAddress, StringComparison.OrdinalIgnoreCase));
    }

    private static void RefreshCurrentPairVerdicts(IoTestIedPlan plan)
    {
        foreach (var point in plan.TestPoints)
        {
            if (point.CaptureMode != FatCaptureMode.AutomaticTransition ||
                point.Runtime.Value1Evidence == null ||
                point.Runtime.Value2Evidence == null ||
                point.Runtime.State is IoTestPointState.Passed or IoTestPointState.Review or IoTestPointState.Failed)
            {
                continue;
            }

            FatCurrentEvidenceAssessmentService.Apply(point);
        }
    }

    private static void NormalizeFatBooleanPresentation(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is TextBlock textBlock)
            {
                if (textBlock.Text.Equals("true", StringComparison.OrdinalIgnoreCase))
                    textBlock.SetCurrentValue(TextBlock.TextProperty, "True");
                else if (textBlock.Text.Equals("false", StringComparison.OrdinalIgnoreCase))
                    textBlock.SetCurrentValue(TextBlock.TextProperty, "False");
            }

            NormalizeFatBooleanPresentation(child);
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

    private static Brush ConnectionBadgeBrushFromHex(string value)
        => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));

    private sealed record BadgePalette(string Background, string Border, string Foreground);
}
