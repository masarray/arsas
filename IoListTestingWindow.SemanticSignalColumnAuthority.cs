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
/// Owns the operator-facing FAT SIGNAL column after the V2 column rebuild. ConfigureFatV2Columns
/// creates SIGNAL as a DataGridTextColumn bound to SignalName, so merely looking for an existing
/// DataGridTemplateColumn never touched the production column. This authority replaces the actual
/// SIGNAL column in-place with a phase-aware template while preserving width constraints.
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

        window.ApplySemanticFatSignalColumn();
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(window.ApplySemanticFatSignalColumn));
    }

    private static void SemanticFatGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid || Window.GetWindow(grid) is not IoListTestingWindow window)
            return;

        window.ApplySemanticFatSignalColumn();
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(window.ApplySemanticFatSignalColumn));
    }

    private void ApplySemanticFatSignalColumn()
    {
        if (_fatSignalsGrid == null)
            return;

        for (var index = 0; index < _fatSignalsGrid.Columns.Count; index++)
        {
            var existing = _fatSignalsGrid.Columns[index];
            if (!string.Equals(existing.Header?.ToString(), "SIGNAL", StringComparison.OrdinalIgnoreCase))
                continue;

            if (existing is DataGridTemplateColumn template &&
                ReferenceEquals(template.CellTemplate, _semanticFatSignalTemplate))
            {
                return;
            }

            var replacement = new DataGridTemplateColumn
            {
                Header = existing.Header,
                Width = existing.Width,
                MinWidth = existing.MinWidth,
                MaxWidth = existing.MaxWidth,
                CanUserResize = existing.CanUserResize,
                CanUserReorder = existing.CanUserReorder,
                CanUserSort = existing.CanUserSort,
                SortMemberPath = nameof(IoTestPointPlan.SignalName),
                IsReadOnly = true,
                CellTemplate = SemanticFatSignalTemplate
            };

            _fatSignalsGrid.Columns[index] = replacement;
            return;
        }
    }

    private static DataTemplate? _semanticFatSignalTemplate;
    private static DataTemplate SemanticFatSignalTemplate =>
        _semanticFatSignalTemplate ??= BuildSemanticFatSignalTemplate();

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
        text.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 0, 4, 0));

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
