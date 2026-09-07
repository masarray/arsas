using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Field-facing presentation fixes for the Engineering workspace. Source/evidence values
/// remain untouched while operator-facing timestamp, command-header and signal-name
/// presentation is normalized.
/// </summary>
internal static class MainWindowFieldPresentationFix
{
    private const string IedTimestampHeader = "IED Timestamp";

    [ModuleInitializer]
    internal static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));
        EventManager.RegisterClassHandler(
            typeof(TextBlock),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(FieldPresentation_TextBlockLoaded),
            handledEventsToo: true);
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
            return;

        if (window.FindName("MainTabs") is TabControl tabs)
        {
            tabs.SelectionChanged -= MainTabs_SelectionChanged;
            tabs.SelectionChanged += MainTabs_SelectionChanged;
        }

        Apply(window);
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => Apply(window)));
    }

    private static void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabControl tabs || !ReferenceEquals(e.Source, tabs))
            return;

        if (Window.GetWindow(tabs) is MainWindow window)
        {
            window.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() => Apply(window)));
        }
    }

    private static void Apply(MainWindow window)
    {
        ApplyIedTimestampColumns(window);
        ApplyDarkCommandHeaderContrast(window);
    }

    private static void ApplyIedTimestampColumns(MainWindow window)
    {
        foreach (var grid in VisualDescendants<DataGrid>(window))
        {
            foreach (var column in grid.Columns.OfType<DataGridTextColumn>())
            {
                if (!string.Equals(column.Header?.ToString(), IedTimestampHeader, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (column.Binding is Binding existing &&
                    ReferenceEquals(existing.Converter, RoundedIedTimestampConverter.Instance))
                {
                    continue;
                }

                column.Binding = new Binding("DeviceTimestamp")
                {
                    Mode = BindingMode.OneWay,
                    Converter = RoundedIedTimestampConverter.Instance
                };

                var style = new Style(typeof(TextBlock), column.ElementStyle);
                style.Setters.Add(new Setter(
                    FrameworkElement.ToolTipProperty,
                    new Binding("DeviceTimestamp")
                    {
                        Mode = BindingMode.OneWay,
                        Converter = FullIedTimestampTooltipConverter.Instance
                    }));
                style.Setters.Add(new Setter(ToolTipService.ShowDurationProperty, 60000));
                column.ElementStyle = style;
            }
        }
    }

    private static void ApplyDarkCommandHeaderContrast(MainWindow window)
    {
        if (window.FindName("CommandPanelExpander") is not Expander expander || expander.Header is not DependencyObject header)
            return;

        expander.Foreground = Brushes.White;
        foreach (var text in VisualDescendants<TextBlock>(header).Prepend(header as TextBlock).OfType<TextBlock>())
            text.Foreground = Brushes.White;
    }

    /// <summary>
    /// Both active operator surfaces bind their signal column to SignalName. Engineering
    /// points carry IecTelegram; FAT points carry ObjectReference. Replace only the rendered
    /// text binding with a semantic MultiBinding so phase context is visible without changing
    /// IEC identity, report binding, FCDA membership or PointKey.
    /// </summary>
    private static void FieldPresentation_TextBlockLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock text || text.DataContext == null)
            return;

        var binding = BindingOperations.GetBinding(text, TextBlock.TextProperty);
        if (!string.Equals(binding?.Path?.Path, "SignalName", StringComparison.Ordinal))
            return;

        var referencePath = ResolveSemanticReferencePath(text.DataContext.GetType());
        if (referencePath == null)
            return;

        var semanticBinding = CreateSemanticSignalBinding(referencePath);
        BindingOperations.SetBinding(text, TextBlock.TextProperty, semanticBinding);
        BindingOperations.SetBinding(text, FrameworkElement.ToolTipProperty, CreateSemanticSignalBinding(referencePath));
    }

    private static MultiBinding CreateSemanticSignalBinding(string referencePath)
    {
        var result = new MultiBinding
        {
            Mode = BindingMode.OneWay,
            Converter = SemanticSignalNameConverter.Instance
        };
        result.Bindings.Add(new Binding("SignalName") { Mode = BindingMode.OneWay });
        result.Bindings.Add(new Binding(referencePath) { Mode = BindingMode.OneWay });
        return result;
    }

    private static string? ResolveSemanticReferencePath(Type dataContextType)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        if (dataContextType.GetProperty("ObjectReference", flags) != null)
            return "ObjectReference";
        if (dataContextType.GetProperty("IecTelegram", flags) != null)
            return "IecTelegram";
        if (dataContextType.GetProperty("DisplayReference", flags) != null)
            return "DisplayReference";
        return null;
    }

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T self)
            yield return self;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            foreach (var descendant in VisualDescendants<T>(child))
                yield return descendant;
        }
    }

    private sealed class RoundedIedTimestampConverter : IValueConverter
    {
        internal static readonly RoundedIedTimestampConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => Iec61850TimestampPresentation.FormatMilliseconds(value?.ToString());

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    private sealed class FullIedTimestampTooltipConverter : IValueConverter
    {
        internal static readonly FullIedTimestampTooltipConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value?.ToString()?.Trim() ?? string.Empty;
            if (text.Length == 0 || text == "-")
                return text.Length == 0 ? "-" : text;

            if (DateTime.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                    out var dateTime))
            {
                return dateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
            }

            if (DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var dateTimeOffset))
            {
                return dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss.fffffff zzz", CultureInfo.InvariantCulture);
            }

            return text;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    private sealed class SemanticSignalNameConverter : IMultiValueConverter
    {
        internal static readonly SemanticSignalNameConverter Instance = new();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var preferred = values.Length > 0 && values[0] != DependencyProperty.UnsetValue
                ? values[0]?.ToString()
                : string.Empty;
            var reference = values.Length > 1 && values[1] != DependencyProperty.UnsetValue
                ? values[1]?.ToString()
                : string.Empty;
            return IoFatSignalDisplayNameFormatter.Format(preferred, reference);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
