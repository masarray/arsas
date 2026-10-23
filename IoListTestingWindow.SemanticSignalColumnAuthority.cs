using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Makes the FAT workspace SIGNAL column use the same IEC 61850 DO/DA phase-aware
/// presentation as the report preview. The V2 FAT workspace rebuilds its DataGrid columns
/// during Window.Loaded, so this authority deliberately reapplies the semantic template one
/// Loaded dispatcher turn later. Row virtualization/recycling can therefore never fall back
/// to the raw DO-only SignalName (A/A/A, ThdA/ThdA/ThdA, etc.). Raw IEC identities remain
/// unchanged.
/// </summary>
public partial class IoListTestingWindow
{
    [ModuleInitializer]
    internal static void RegisterSemanticFatSignalColumnAuthority()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(SemanticFatWindow_Loaded),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(SemanticFatGrid_Loaded),
            handledEventsToo: true);
    }

    private static void SemanticFatWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window)
            return;

        // InstallFatV2WorkspaceUx/ConfigureP0StableFatColumns run on the same Window.Loaded
        // route. Defer one turn so our template is the final production column authority.
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(window.ApplySemanticFatSignalColumn));
    }

    private static void SemanticFatGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid || Window.GetWindow(grid) is not IoListTestingWindow window)
            return;

        window.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(window.ApplySemanticFatSignalColumn));
    }

    private void ApplySemanticFatSignalColumn()
    {
        if (_fatSignalsGrid == null)
            return;

        foreach (var column in _fatSignalsGrid.Columns.OfType<DataGridTemplateColumn>())
        {
            if (!string.Equals(column.Header?.ToString(), "SIGNAL", StringComparison.OrdinalIgnoreCase))
                continue;

            column.CellTemplate = BuildSemanticFatSignalTemplate();
        }
    }

    private static DataTemplate BuildSemanticFatSignalTemplate()
    {
#pragma warning disable CS0618 // FrameworkElementFactory remains the WPF programmatic DataTemplate API.
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
