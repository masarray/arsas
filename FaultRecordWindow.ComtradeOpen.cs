using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
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
        buttonFactory.SetValue(AutomationProperties.AutomationIdProperty, "FaultRecord.OpenComtrade");
        buttonFactory.SetValue(AutomationProperties.NameProperty, "Open COMTRADE record");
        buttonFactory.SetValue(FrameworkElement.HeightProperty, 27d);
        buttonFactory.SetValue(FrameworkElement.MinWidthProperty, 60d);
        buttonFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 3, 6, 3));
        buttonFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        buttonFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        buttonFactory.SetValue(Control.PaddingProperty, new Thickness(12, 0, 12, 0));
        buttonFactory.SetValue(Control.FontSizeProperty, 11.5d);
        buttonFactory.SetValue(Control.FontWeightProperty, FontWeights.SemiBold);
        buttonFactory.SetValue(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(35, 86, 153)));
        buttonFactory.SetValue(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(244, 248, 255)));
        buttonFactory.SetValue(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(166, 190, 221)));
        buttonFactory.SetValue(Control.BorderThicknessProperty, new Thickness(1));
        buttonFactory.SetValue(FrameworkElement.ToolTipProperty, "Open in COMTRADE Viewer");
        buttonFactory.SetValue(ToolTipService.InitialShowDelayProperty, 650);
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
            Header = new TextBlock
            {
                Text = "Open",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Width = new DataGridLength(82),
            MinWidth = 78,
            MaxWidth = 92,
            IsReadOnly = true,
            CanUserSort = false,
            CanUserResize = false,
            CellTemplate = cellTemplate
        });

        _comtradeOpenColumnInstalled = true;
    }

    private async void OpenComtrade_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: FaultRecordRow row } button)
            return;

        e.Handled = true;

        if (row.LocalState != FaultRecordLocalState.Downloaded)
        {
            ShowToast("Download the complete COMTRADE record before opening it.", ToastKind.Warning);
            return;
        }

        var originalContent = button.Content;
        button.IsEnabled = false;
        button.Content = "Opening…";

        try
        {
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

            StatusText = $"Opening {Path.GetFileName(cfgPath)} in the ARSAS COMTRADE Viewer…";
            ShowToast("Starting COMTRADE Viewer…", ToastKind.Information);

            if (!ArdIrecViewerLauncher.TryLaunch(cfgPath, out var process, out var launchError) || process is null)
            {
                StatusText = $"COMTRADE viewer could not open {Path.GetFileName(cfgPath)}: {launchError}";
                ShowToast("COMTRADE Viewer could not be started.", ToastKind.Error);
                MessageBox.Show(
                    this,
                    launchError,
                    "COMTRADE Viewer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            using (process)
            {
                // Process.Start only proves Windows created a process. Qt/QML or graphics-runtime
                // failures can still close the viewer immediately. Observe a short bounded startup
                // period so Open never appears to do nothing on a real workstation.
                var activated = false;
                for (var attempt = 0; attempt < 6; attempt++)
                {
                    await Task.Delay(attempt == 0 ? 350 : 220).ConfigureAwait(true);

                    process.Refresh();
                    if (process.HasExited)
                    {
                        var earlyExitError = ArdIrecViewerLauncher.DescribeEarlyExit(process, cfgPath);
                        StatusText = $"COMTRADE viewer failed for {Path.GetFileName(cfgPath)}: {earlyExitError}";
                        ShowToast("COMTRADE Viewer closed during startup.", ToastKind.Error);
                        MessageBox.Show(
                            this,
                            earlyExitError,
                            "COMTRADE Viewer startup failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;
                    }

                    if (!activated)
                        activated = ArdIrecViewerLauncher.TryActivateViewerWindow(process);
                }

                StatusText = $"Opened {Path.GetFileName(cfgPath)} in the ARSAS COMTRADE Viewer.";
                ShowToast("COMTRADE record opened.", ToastKind.Success);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            StatusText = $"COMTRADE viewer startup failed for {row.RecordName}: {ex.Message}";
            ShowToast("COMTRADE Viewer startup failed.", ToastKind.Error);
            MessageBox.Show(
                this,
                ex.Message,
                "COMTRADE Viewer startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            button.Content = originalContent;
            button.IsEnabled = true;
        }
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
