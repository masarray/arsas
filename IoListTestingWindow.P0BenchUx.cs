using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Bench-facing P0 UX corrections that are intentionally presentation-only.
///
/// - LIVE/VALUE1/VALUE2 always render canonical Boolean text without mutating raw evidence.
/// - Normal per-cell Capture buttons are removed from the template; Recapture remains the
///   explicit operator correction path while FatAutoCaptureCoordinator owns normal capture.
/// - Primary session actions and secondary evidence/status actions use two adaptive rows.
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
        window.ContentRendered += window.P0BenchUxContentRendered;
        window.Closed += window.P0BenchUxClosed;
        window.Dispatcher.BeginInvoke(
            new Action(window.ApplyP0BenchUx),
            DispatcherPriority.ContextIdle);
    }

    private void P0BenchUxContentRendered(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(new Action(ApplyP0BenchUx), DispatcherPriority.ContextIdle);

    private void P0BenchUxClosed(object? sender, EventArgs e)
    {
        ContentRendered -= P0BenchUxContentRendered;
        Closed -= P0BenchUxClosed;
    }

    private void ApplyP0BenchUx()
    {
        ConfigureP0StableFatColumns();
        ConfigureP0AdaptiveHeaderActions();
    }

    private void ConfigureP0StableFatColumns()
    {
        if (_fatSignalsGrid == null)
            return;

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
        value.SetBinding(FrameworkElement.ToolTipProperty, new Binding(isValue1
            ? nameof(IoTestPointPlan.Value1EvidenceToolTip)
            : nameof(IoTestPointPlan.Value2EvidenceToolTip)));
        panel.AppendChild(value);

        var timestamp = new FrameworkElementFactory(typeof(TextBlock));
        timestamp.SetBinding(TextBlock.TextProperty, new Binding(isValue1
            ? nameof(IoTestPointPlan.Value1RelayTimestampText)
            : nameof(IoTestPointPlan.Value2RelayTimestampText)));
        timestamp.SetValue(TextBlock.FontSizeProperty, 8.8);
        timestamp.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Cascadia Mono, Consolas"));
        timestamp.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(112, 126, 145)));
        panel.AppendChild(timestamp);

        // Intentionally no normal Capture button. Automatic capture is the normal path;
        // multi-row/context Recapture remains available for explicit evidence correction.
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
            VerticalAlignment = VerticalAlignment.Center
        };
        _p0SecondaryHeaderActions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 0)
        };

        foreach (var child in children)
        {
            if (IsP0SecondaryHeaderAction(child))
                _p0SecondaryHeaderActions.Children.Add(child);
            else
                _p0PrimaryHeaderActions.Children.Add(child);
        }

        actionPanel.Children.Add(_p0PrimaryHeaderActions);
        if (_p0SecondaryHeaderActions.Children.Count > 0)
            actionPanel.Children.Add(_p0SecondaryHeaderActions);
    }

    private bool IsP0SecondaryHeaderAction(UIElement element)
        => ReferenceEquals(element, WorkspacePreviewToggle) ||
           ReferenceEquals(element, _timeSyncEvidenceButton) ||
           ReferenceEquals(element, _comtradeEvidenceButton) ||
           ReferenceEquals(element, _cleanSessionButton) ||
           ReferenceEquals(element, _clockSyncGlobalStatusText) ||
           ReferenceEquals(element, _clockSyncEvidenceText);
}

public sealed class P0FatCanonicalValueConverter : IValueConverter
{
    public static P0FatCanonicalValueConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => MainWindow.P0CanonicalLiveValue(value?.ToString());

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
