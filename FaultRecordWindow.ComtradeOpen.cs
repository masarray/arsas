using System.IO;
using System.Windows;
using System.Windows.Controls;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class FaultRecordWindow
{
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
                // A successful Process.Start only proves Windows created a process. Qt/QML or
                // graphics-runtime failures can still close the viewer immediately. Give the
                // process a short bounded startup window and report that failure back to the
                // operator instead of making the Open button appear inert.
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
