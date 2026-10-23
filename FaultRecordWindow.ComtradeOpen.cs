using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
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

            StatusText = $"Opening {Path.GetFileName(cfgPath)} in the native COMTRADE workspace…";
            ShowToast("Loading COMTRADE record with native ArdIrec core…", ToastKind.Information);

            // ArdIrec's reference DatReader eagerly decodes the record while opening. Keep that work
            // off WPF's dispatcher thread so large field records do not freeze the Fault Records UI.
            var nativeOpen = await Task.Run(() =>
            {
                var opened = ArdIrecNativeBridge.TryOpen(cfgPath, out var record, out var error);
                return (Opened: opened, Record: record, Error: error);
            }).ConfigureAwait(true);

            if (nativeOpen.Opened && nativeOpen.Record is not null)
            {
                try
                {
                    var workspace = new ComtradeWorkspaceWindow(nativeOpen.Record)
                    {
                        Owner = this
                    };
                    workspace.Show();
                    StatusText = $"Opened {Path.GetFileName(cfgPath)} in the ARSAS native COMTRADE workspace.";
                    ShowToast("COMTRADE record opened natively.", ToastKind.Success);
                    return;
                }
                catch
                {
                    nativeOpen.Record.Dispose();
                    throw;
                }
            }

            var nativeError = string.IsNullOrWhiteSpace(nativeOpen.Error)
                ? "The ArdIrec native COMTRADE bridge could not open this record."
                : nativeOpen.Error;

            StatusText = $"Native COMTRADE workspace could not open {Path.GetFileName(cfgPath)}: {nativeError}";
            ShowToast("Native COMTRADE workspace unavailable.", ToastKind.Error);
            MessageBox.Show(
                this,
                nativeError,
                "Native COMTRADE workspace",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or SEHException or BadImageFormatException)
        {
            StatusText = $"Native COMTRADE startup failed for {row.RecordName}: {ex.Message}";
            ShowToast("Native COMTRADE workspace startup failed.", ToastKind.Error);
            MessageBox.Show(
                this,
                ex.Message,
                "Native COMTRADE workspace startup failed",
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
