using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Bench-facing FAT UX corrections. FAT deliberately disables ToolTips so hover creation
/// cannot compete with report-backed updates on relay benches; Engineering ToolTips live in
/// MainWindow and are untouched. The FAT action strip is kept in one compact adaptive row.
/// </summary>
public partial class IoListTestingWindow
{
    private static readonly bool P0BenchUxClassHandlerRegistered = RegisterP0BenchUxClassHandler();
    private bool _p0BenchUxInstalled;
    private WrapPanel? _p0PrimaryHeaderActions;
    private WrapPanel? _p0SecondaryHeaderActions;

    private static bool RegisterP0BenchUxClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P0BenchUxLoaded));
        return true;
    }

    private static void P0BenchUxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window || window._p0BenchUxInstalled)
            return;

        window._p0BenchUxInstalled = true;
        window.Closed += window.P0BenchUxClosed;

        // FAT-only. Do not touch MainWindow/Engineering ToolTips.
        ToolTipService.SetIsEnabled(window, false);

        // Final FAT V2 schema is installed before first visible render.
        window.InstallFatV2WorkspaceUx();
        window.ApplyP0BenchUx();
    }

    private void P0BenchUxClosed(object? sender, EventArgs e)
        => Closed -= P0BenchUxClosed;

    private void ApplyP0BenchUx()
    {
        ConfigureP0StableFatColumns();
        ConfigureP0AdaptiveHeaderActions();
        ConfigureP2CompactHeader();
    }

    private void ConfigureP0StableFatColumns()
    {
        if (_fatSignalsGrid == null)
            return;

        // Removed signals remain in the immutable project/evidence model so they can be
        // restored from the dedicated Removed Signals UX, but they are not active FAT rows.
        // Collapse them at the row-container layer so a removed point consumes no grid space
        // and reappears automatically as soon as IsIncludedInFat becomes true again.
        var activeFatRowStyle = new Style(typeof(DataGridRow), _fatSignalsGrid.RowStyle);
        activeFatRowStyle.Triggers.Add(new DataTrigger
        {
            Binding = new Binding(nameof(IoTestPointPlan.IsIncludedInFat)),
            Value = false,
            Setters =
            {
                new Setter(UIElement.VisibilityProperty, Visibility.Collapsed)
            }
        });
        _fatSignalsGrid.RowStyle = activeFatRowStyle;

        foreach (var column in _fatSignalsGrid.Columns.OfType<DataGridTemplateColumn>())
        {
            var header = column.Header?.ToString()?.Trim() ?? string.Empty;
            if (header.Equals("LIVE VALUE", StringComparison.OrdinalIgnoreCase))
            {
                column.CellTemplate = BuildP0LiveValueTemplate();
                continue;
            }

            if (header.Equals("VALUE 1", StringComparison.OrdinalIgnoreCase))
            {
                column.CellTemplate = BuildP0EvidenceValueTemplate(FatValueSlot.Value1);
                continue;
            }

            if (header.Equals("VALUE 2", StringComparison.OrdinalIgnoreCase))
                column.CellTemplate = BuildP0EvidenceValueTemplate(FatValueSlot.Value2);
        }
    }

    private static DataTemplate BuildP0LiveValueTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);

        var value = new FrameworkElementFactory(typeof(TextBlock));
        value.SetBinding(TextBlock.TextProperty, new Binding("Runtime.CurrentValue")
        {
            Converter = P0FatCanonicalValueConverter.Instance,
            Mode = BindingMode.OneWay
        });
        value.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        value.SetValue(TextBlock.FontSizeProperty, 12.0);
        value.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(30, 60, 100)));
        panel.AppendChild(value);

        var quality = new FrameworkElementFactory(typeof(TextBlock));
        quality.SetBinding(TextBlock.TextProperty, new Binding("Runtime.CurrentQuality") { Mode = BindingMode.OneWay });
        quality.SetValue(TextBlock.FontSizeProperty, 9.5);
        quality.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(105, 121, 143)));
        panel.AppendChild(quality);

        return new DataTemplate { VisualTree = panel };
    }

    private static DataTemplate BuildP0EvidenceValueTemplate(FatValueSlot slot)
    {
        var isValue1 = slot == FatValueSlot.Value1;
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
        panel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1));

        var value = new FrameworkElementFactory(typeof(TextBlock));
        value.SetBinding(TextBlock.TextProperty, new Binding(isValue1
            ? nameof(IoTestPointPlan.Value1Text)
            : nameof(IoTestPointPlan.Value2Text))
        {
            Converter = P0FatCanonicalValueConverter.Instance,
            Mode = BindingMode.OneWay
        });
        value.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        value.SetValue(TextBlock.FontSizeProperty, 11.4);
        value.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        panel.AppendChild(value);

        var timestamp = new FrameworkElementFactory(typeof(TextBlock));
        timestamp.SetBinding(TextBlock.TextProperty, new Binding(isValue1
            ? nameof(IoTestPointPlan.Value1RelayTimestampText)
            : nameof(IoTestPointPlan.Value2RelayTimestampText)));
        timestamp.SetValue(TextBlock.FontSizeProperty, 8.8);
        timestamp.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Cascadia Mono, Consolas"));
        timestamp.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(112, 126, 145)));
        panel.AppendChild(timestamp);

        return new DataTemplate { VisualTree = panel };
    }

    private void ConfigureP0AdaptiveHeaderActions()
    {
        if (_p0PrimaryHeaderActions != null || WorkspacePreviewToggle.Parent is not StackPanel actionPanel)
            return;

        var children = actionPanel.Children.Cast<UIElement>().ToArray();
        if (children.Length == 0)
            return;

        actionPanel.Children.Clear();
        actionPanel.Orientation = Orientation.Vertical;
        actionPanel.HorizontalAlignment = HorizontalAlignment.Right;
        actionPanel.VerticalAlignment = VerticalAlignment.Center;

        _p0PrimaryHeaderActions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0)
        };

        // Retained as an empty compatibility panel because P2 helpers reference it. All FAT
        // actions/status now share the one visible compact row instead of forcing a second row.
        _p0SecondaryHeaderActions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0)
        };

        foreach (var child in children)
            _p0PrimaryHeaderActions.Children.Add(child);

        actionPanel.Children.Add(_p0PrimaryHeaderActions);
    }

    private bool IsP0SecondaryHeaderAction(UIElement element) => false;
}

public sealed class P0FatCanonicalValueConverter : IValueConverter
{
    public static P0FatCanonicalValueConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => MainWindow.P0CanonicalLiveValue(value?.ToString());

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
