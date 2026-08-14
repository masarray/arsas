using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private void QueueExplorerSignalGridLayout()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(ConfigureExplorerSignalGridForCompactFit));
    }

    private void ConfigureExplorerSignalGridForCompactFit()
    {
        var signalGrid = FindExplorerVisualChildren<DataGrid>(MainTabs)
            .FirstOrDefault(grid =>
            {
                if (grid.Columns.Count != 6)
                    return false;

                var headers = grid.Columns
                    .Select(column => column.Header?.ToString() ?? string.Empty)
                    .ToArray();

                return headers.SequenceEqual(new[]
                {
                    "Signal",
                    "IEC Telegram",
                    "Value",
                    "Quality",
                    "IED Timestamp",
                    "Acquisition"
                });
            });

        if (signalGrid == null)
            return;

        ScrollViewer.SetHorizontalScrollBarVisibility(signalGrid, ScrollBarVisibility.Disabled);
        signalGrid.CanUserResizeColumns = false;
        signalGrid.FrozenColumnCount = 0;

        var weights = new[] { 1.00, 1.55, 0.82, 0.68, 1.15, 1.05 };
        var minimums = new[] { 90d, 140d, 80d, 70d, 135d, 115d };

        for (var index = 0; index < signalGrid.Columns.Count; index++)
        {
            signalGrid.Columns[index].MinWidth = minimums[index];
            signalGrid.Columns[index].Width = new DataGridLength(weights[index], DataGridLengthUnitType.Star);
        }

        ConfigureExplorerTimestampPresentation(signalGrid);
    }

    private static void ConfigureExplorerTimestampPresentation(DataGrid signalGrid)
    {
        if (signalGrid.Columns.Count <= 4 || signalGrid.Columns[4] is not DataGridTextColumn timestampColumn)
            return;

        // Display only is rounded to nearest millisecond. The monitor point keeps the
        // original full-resolution timestamp for evidence, search and hover detail.
        timestampColumn.Binding = new Binding(nameof(Iec61850MonitorPoint.DeviceTimestamp))
        {
            Converter = ExplorerTimestampMillisecondsConverter.Instance,
            Mode = BindingMode.OneWay
        };

        var timestampTextStyle = new Style(typeof(TextBlock), timestampColumn.ElementStyle);
        timestampTextStyle.Setters.Add(new Setter(
            FrameworkElement.ToolTipProperty,
            new Binding(nameof(Iec61850MonitorPoint.DeviceTimestamp))
            {
                Converter = ExplorerTimestampFullPrecisionToolTipConverter.Instance,
                Mode = BindingMode.OneWay
            }));
        timestampTextStyle.Setters.Add(new Setter(ToolTipService.ShowDurationProperty, 30000));
        timestampTextStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        timestampColumn.ElementStyle = timestampTextStyle;

        // Scope the premium tooltip styling to this live-value DataGrid so unrelated
        // application tooltips retain their established appearance.
        signalGrid.Resources[typeof(ToolTip)] = BuildExplorerTimestampToolTipStyle();
    }

    private static Style BuildExplorerTimestampToolTipStyle()
    {
        var style = new Style(typeof(ToolTip));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 11.4));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Medium));
        style.Setters.Add(new Setter(ToolTip.PlacementProperty, PlacementMode.Mouse));
        style.Setters.Add(new Setter(ToolTip.HorizontalOffsetProperty, 10d));
        style.Setters.Add(new Setter(ToolTip.VerticalOffsetProperty, 12d));

        var template = new ControlTemplate(typeof(ToolTip));
        var chrome = new FrameworkElementFactory(typeof(Border));
        chrome.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(35, 49, 59)));
        chrome.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(91, 111, 123)));
        chrome.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        chrome.SetValue(Border.PaddingProperty, new Thickness(11, 8, 11, 8));
        chrome.SetValue(Border.EffectProperty, new DropShadowEffect
        {
            BlurRadius = 16,
            ShadowDepth = 4,
            Opacity = 0.20,
            Color = Color.FromRgb(15, 23, 42)
        });

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, false);
        chrome.AppendChild(content);
        template.VisualTree = chrome;
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private static IEnumerable<T> FindExplorerVisualChildren<T>(DependencyObject? root)
        where T : DependencyObject
    {
        if (root == null)
            yield break;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
                yield return typed;

            foreach (var descendant in FindExplorerVisualChildren<T>(child))
                yield return descendant;
        }
    }
}

internal sealed class ExplorerTimestampMillisecondsConverter : IValueConverter
{
    public static ExplorerTimestampMillisecondsConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => Iec61850TimestampPresentation.FormatMilliseconds(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class ExplorerTimestampFullPrecisionToolTipConverter : IValueConverter
{
    public static ExplorerTimestampFullPrecisionToolTipConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = (value as string)?.Trim();
        return string.IsNullOrWhiteSpace(text) || text == "-"
            ? "IED TIMESTAMP · FULL PRECISION\nNo timestamp available"
            : $"IED TIMESTAMP · FULL PRECISION\n{text}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
