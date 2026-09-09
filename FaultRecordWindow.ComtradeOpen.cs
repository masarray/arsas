using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class FaultRecordWindow
{
    private bool _comtradeOpenColumnInstalled;

    private void EnsureComtradeOpenColumn()
    {
        if (_comtradeOpenColumnInstalled || FaultRecordsGrid is null)
            return;

        var buttonFactory = new FrameworkElementFactory(typeof(Button));
        buttonFactory.SetValue(ContentControl.ContentProperty, "Open");
        buttonFactory.SetValue(FrameworkElement.HeightProperty, 27d);
        buttonFactory.SetValue(FrameworkElement.MinWidthProperty, 56d);
        buttonFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 2, 4, 2));
        buttonFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        buttonFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        buttonFactory.SetValue(Control.PaddingProperty, new Thickness(10, 0, 10, 0));
        buttonFactory.SetValue(Control.FontSizeProperty, 11.5d);
        buttonFactory.SetValue(Control.FontWeightProperty, FontWeights.SemiBold);
        buttonFactory.SetValue(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(37, 82, 145)));
        buttonFactory.SetValue(Control.BackgroundProperty, Brushes.White);
        buttonFactory.SetValue(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(184, 201, 224)));
        buttonFactory.SetValue(Control.BorderThicknessProperty, new Thickness(1));
        buttonFactory.SetValue(FrameworkElement.ToolTipProperty, "Open the downloaded COMTRADE record in the ARSAS COMTRADE Viewer");
        buttonFactory.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);
        buttonFactory.SetBinding(
            UIElement.VisibilityProperty,
            new Binding(nameof(FaultRecordRow.LocalState))
            {
                Mode = BindingMode.OneWay,
                Converter = DownloadedFaultRecordVisibilityConverter.Instance
            });
        buttonFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler(OpenComtrade_Click));

        var cellTemplate = new DataTemplate
        {
            VisualTree = buttonFactory
        };

        FaultRecordsGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Open",
            Width = new DataGridLength(72),
            MinWidth = 68,
            MaxWidth = 82,
            IsReadOnly = true,
            CanUserSort = false,
            CanUserResize = false,
            CellTemplate = cellTemplate
        });

        _comtradeOpenColumnInstalled = true;
    }

    private void OpenComtrade_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: FaultRecordRow row })
            return;

        e.Handled = true;

        if (row.LocalState != FaultRecordLocalState.Downloaded)
        {
            ShowToast("Download the complete COMTRADE record before opening it.", ToastKind.Warning);
            return;
        }

        if (!ArdIrecViewerLauncher.TryResolveComtradeCfg(
                row.LocalDirectory,
                row.RecordName,
                out var cfgPath,
                out var resolveError))
        {
            StatusText = $"COMTRADE open failed for {row.RecordName}: {resolveError}";
            ShowToast(resolveError, ToastKind.Error);
            return;
        }

        if (!ArdIrecViewerLauncher.TryLaunch(cfgPath, out var launchError))
        {
            StatusText = $"COMTRADE viewer could not open {Path.GetFileName(cfgPath)}: {launchError}";
            ShowToast("COMTRADE Viewer is unavailable in this build.", ToastKind.Error);
            MessageBox.Show(
                this,
                launchError,
                "COMTRADE Viewer",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        StatusText = $"Opening {Path.GetFileName(cfgPath)} in the ARSAS COMTRADE Viewer…";
        ShowToast("Opening downloaded COMTRADE record.", ToastKind.Success);
    }
}

internal sealed class DownloadedFaultRecordVisibilityConverter : IValueConverter
{
    public static DownloadedFaultRecordVisibilityConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is FaultRecordLocalState.Downloaded ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
