using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Keeps IEC 61850 identity and UI presentation separate. The raw object/reference fields
/// remain untouched; only SignalName text rendered in Engineering/FAT is phase-aware.
/// </summary>
public partial class MainWindow
{
    private static readonly bool FieldPresentationFixClassHandlersRegistered = RegisterFieldPresentationFixClassHandlers();

    private static bool RegisterFieldPresentationFixClassHandlers()
    {
        EventManager.RegisterClassHandler(
            typeof(TextBlock),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(FieldPresentation_TextBlockLoaded),
            handledEventsToo: true);
        return true;
    }

    private static void FieldPresentation_TextBlockLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock text || text.DataContext == null)
            return;

        // Both active operator surfaces bind their signal column to SignalName:
        // Engineering points carry IecTelegram, FAT rows carry ObjectReference. Replace
        // only that presentation binding with a semantic MultiBinding. Virtualized/recycled
        // rows therefore keep following their DataContext without mutating the source model.
        var binding = BindingOperations.GetBinding(text, TextBlock.TextProperty);
        if (!string.Equals(binding?.Path?.Path, "SignalName", StringComparison.Ordinal))
            return;

        var referencePath = ResolveSemanticReferencePath(text.DataContext.GetType());
        if (referencePath == null)
            return;

        var semanticBinding = new MultiBinding
        {
            Mode = BindingMode.OneWay,
            Converter = SemanticSignalNameConverter.Instance
        };
        semanticBinding.Bindings.Add(new Binding("SignalName") { Mode = BindingMode.OneWay });
        semanticBinding.Bindings.Add(new Binding(referencePath) { Mode = BindingMode.OneWay });
        BindingOperations.SetBinding(text, TextBlock.TextProperty, semanticBinding);

        var toolTipBinding = new MultiBinding
        {
            Mode = BindingMode.OneWay,
            Converter = SemanticSignalNameConverter.Instance
        };
        toolTipBinding.Bindings.Add(new Binding("SignalName") { Mode = BindingMode.OneWay });
        toolTipBinding.Bindings.Add(new Binding(referencePath) { Mode = BindingMode.OneWay });
        BindingOperations.SetBinding(text, FrameworkElement.ToolTipProperty, toolTipBinding);
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

    private sealed class SemanticSignalNameConverter : IMultiValueConverter
    {
        public static readonly SemanticSignalNameConverter Instance = new();

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
