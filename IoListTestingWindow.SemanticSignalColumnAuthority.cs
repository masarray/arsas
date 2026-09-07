using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Makes the FAT workspace SIGNAL column use the same IEC 61850 DO/DA phase-aware
/// presentation as the report preview. This is deliberately installed on the production
/// DataGrid template itself so row virtualization/recycling can never fall back to the raw
/// DO-only SignalName (A/A/A, ThdA/ThdA/ThdA, etc.). Raw IEC identities remain unchanged.
/// </summary>
internal static class IoListTestingWindowSemanticSignalColumnAuthority
{
    [ModuleInitializer]
    internal static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(DataGrid_Loaded),
            handledEventsToo: true);
    }

    private static void DataGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid || Window.GetWindow(grid) is not IoListTestingWindow)
            return;

        foreach (var column in grid.Columns.OfType<DataGridTemplateColumn>())
        {
            if (!string.Equals(column.Header?.ToString(), "SIGNAL", StringComparison.OrdinalIgnoreCase))
                continue;

            column.CellTemplate = BuildSemanticSignalTemplate();
        }
    }

    private static DataTemplate BuildSemanticSignalTemplate()
    {
#pragma warning disable CS0618 // FrameworkElementFactory is the supported programmatic DataTemplate path on WPF.
        var text = new FrameworkElementFactory(typeof(TextBlock));
        var semanticBinding = new Binding(".")
        {
            Mode = BindingMode.OneWay,
            Converter = FatSemanticSignalNameConverter.Instance
        };
        text.SetBinding(TextBlock.TextProperty, semanticBinding);
        text.SetBinding(FrameworkElement.ToolTipProperty, new Binding(".")
        {
            Mode = BindingMode.OneWay,
            Converter = FatSemanticSignalNameConverter.Instance
        });
        text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(TextBlock.FontSizeProperty, 12.2d);
        text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        text.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(38, 56, 79)));

        return new DataTemplate { VisualTree = text };
#pragma warning restore CS0618
    }

    private sealed class FatSemanticSignalNameConverter : IValueConverter
    {
        internal static readonly FatSemanticSignalNameConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is IoTestPointPlan point
                ? IoFatSignalDisplayNameFormatter.Format(point)
                : value?.ToString() ?? "Signal";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
