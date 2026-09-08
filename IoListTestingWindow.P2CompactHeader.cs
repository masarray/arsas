using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// P2 bench-facing header compaction.
///
/// P0 already separates operational actions from evidence/status actions. P2 only tightens
/// that existing hierarchy so the FAT header remains readable on normal engineering laptop
/// widths. Full evidence detail stays in the existing tooltips; protocol, evidence and grid
/// behavior are intentionally untouched.
/// </summary>
public partial class IoListTestingWindow
{
    private void ConfigureP2CompactHeader()
    {
        if (_p0PrimaryHeaderActions == null || _p0SecondaryHeaderActions == null)
            return;

        _p0PrimaryHeaderActions.Margin = new Thickness(0);
        _p0SecondaryHeaderActions.Margin = new Thickness(0, 3, 0, 0);

        foreach (var button in _p0PrimaryHeaderActions.Children.OfType<Button>())
            ApplyP2CompactButtonMetrics(button, secondary: false);

        foreach (var button in _p0SecondaryHeaderActions.Children.OfType<Button>())
            ApplyP2CompactButtonMetrics(button, secondary: true);

        WorkspacePreviewToggle.Content = "Preview";
        if (_cleanSessionButton != null)
            _cleanSessionButton.Content = "Clean FAT";

        ApplyP2CompactStatusMetrics(_clockSyncGlobalStatusText, 118, FontWeights.Medium);
        ApplyP2CompactStatusMetrics(_clockSyncEvidenceText, 188, FontWeights.Normal);

        // The selected-IED subtitle used to render the verbose LiveStatusText and was
        // routinely clipped by the operational buttons. Rebind only this presentation line
        // to a compact status; keep the complete legacy summary as its tooltip.
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(ConfigureP2SelectedIedSubtitle));
    }

    private void ConfigureP2SelectedIedSubtitle()
    {
        var text = FindBoundTextBlock(this, nameof(SelectedIedSummary));
        if (text == null)
            return;

        var compact = new MultiBinding
        {
            Mode = BindingMode.OneWay,
            Converter = CompactSelectedIedSummaryConverter.Instance
        };
        compact.Bindings.Add(new Binding("SelectedIed.IpAddress") { Mode = BindingMode.OneWay });
        compact.Bindings.Add(new Binding("SelectedIed.EnabledCount") { Mode = BindingMode.OneWay });
        compact.Bindings.Add(new Binding("SelectedIed.LiveStatusText") { Mode = BindingMode.OneWay });
        BindingOperations.SetBinding(text, TextBlock.TextProperty, compact);
        BindingOperations.SetBinding(
            text,
            FrameworkElement.ToolTipProperty,
            new Binding(nameof(SelectedIedSummary)) { Mode = BindingMode.OneWay });

        text.FontSize = 10.2;
        text.TextWrapping = TextWrapping.NoWrap;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.MaxWidth = 330;
    }

    private static TextBlock? FindBoundTextBlock(DependencyObject root, string bindingPath)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is TextBlock text)
            {
                var binding = BindingOperations.GetBinding(text, TextBlock.TextProperty);
                if (string.Equals(binding?.Path?.Path, bindingPath, StringComparison.Ordinal))
                    return text;
            }

            var nested = FindBoundTextBlock(child, bindingPath);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static void ApplyP2CompactButtonMetrics(Button button, bool secondary)
    {
        button.Padding = secondary
            ? new Thickness(8, 5, 8, 5)
            : new Thickness(9, 6, 9, 6);
        button.Margin = new Thickness(0, 0, 5, 0);
        button.MinWidth = 0;
        button.MinHeight = secondary ? 27 : 29;
        if (secondary)
        {
            button.FontSize = 10.4;
            button.FontWeight = FontWeights.Medium;
        }
    }

    private static void ApplyP2CompactStatusMetrics(
        TextBlock? text,
        double maxWidth,
        FontWeight fontWeight)
    {
        if (text == null)
            return;

        text.MaxWidth = maxWidth;
        text.TextWrapping = TextWrapping.NoWrap;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.FontWeight = fontWeight;
        text.FontSize = 10.2;
    }

    private sealed class CompactSelectedIedSummaryConverter : IMultiValueConverter
    {
        public static readonly CompactSelectedIedSummaryConverter Instance = new();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var ip = Value(values, 0);
            var count = Value(values, 1);
            var status = CompactLiveStatus(Value(values, 2));

            if (string.IsNullOrWhiteSpace(ip))
                return "Select an imported IED";

            return $"{ip} · {count} pts · {status}";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static string Value(object[] values, int index)
            => index < values.Length && values[index] != DependencyProperty.UnsetValue
                ? values[index]?.ToString()?.Trim() ?? string.Empty
                : string.Empty;

        private static string CompactLiveStatus(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "OFFLINE";

            if (value.Contains("Monitoring", StringComparison.OrdinalIgnoreCase))
            {
                if (value.Contains("Static", StringComparison.OrdinalIgnoreCase))
                    return "MON · Static DS";
                if (value.Contains("Report", StringComparison.OrdinalIgnoreCase))
                    return "MON · Report";
                return "MON";
            }

            if (value.Contains("Reconnect", StringComparison.OrdinalIgnoreCase))
                return "RECONNECT";
            if (value.Contains("Offline", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("Disconnect", StringComparison.OrdinalIgnoreCase))
                return "OFFLINE";
            if (value.Contains("Connected", StringComparison.OrdinalIgnoreCase))
                return "CONNECTED";

            return value.Length <= 18 ? value : value[..18] + "…";
        }
    }
}
