using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ArIED61850Tester.Services;
using Microsoft.Win32;

namespace ArIED61850Tester;

/// <summary>
/// Native evidence export entry point. Export consumes the immutable report snapshot
/// created from the already-loaded FAT state; it never creates a second IED session.
/// </summary>
public partial class MainWindow
{
    private DispatcherTimer? _nativeFatExportInstallRetry;
    private Button? _nativeFatExportPdfButton;
    private bool _nativeFatExportInstalled;

    [ModuleInitializer]
    internal static void RegisterNativeFatExport()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(NativeFatExport_MainWindowLoaded),
            handledEventsToo: true);
    }

    private static void NativeFatExport_MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._nativeFatExportInstalled)
            return;

        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(window.TryInstallNativeFatExport));
    }

    private void TryInstallNativeFatExport()
    {
        if (_nativeFatExportInstalled || !IsLoaded)
            return;

        if (!_nativeFatInstalled || _nativeFatSearchBox?.Parent is not WrapPanel toolbar)
        {
            _nativeFatExportInstallRetry ??= new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromMilliseconds(190)
            };
            _nativeFatExportInstallRetry.Tick -= NativeFatExportInstallRetry_Tick;
            _nativeFatExportInstallRetry.Tick += NativeFatExportInstallRetry_Tick;
            _nativeFatExportInstallRetry.Start();
            return;
        }

        _nativeFatExportInstallRetry?.Stop();
        _nativeFatExportInstalled = true;
        _nativeFatExportPdfButton = CreateNativeFatButton(
            "Export PDF",
            NativeFatExportPdf_Click,
            "Export an immutable native FAT evidence PDF for the selected IED. Show historical controls whether historical signal rows are included.");

        var historyIndex = _nativeFatShowHistoricalCheck == null
            ? toolbar.Children.Count
            : toolbar.Children.IndexOf(_nativeFatShowHistoricalCheck);
        if (historyIndex < 0)
            historyIndex = toolbar.Children.Count;
        toolbar.Children.Insert(historyIndex, _nativeFatExportPdfButton);

        Closed += NativeFatExport_MainWindowClosed;
    }

    private void NativeFatExportInstallRetry_Tick(object? sender, EventArgs e)
    {
        _nativeFatExportInstallRetry?.Stop();
        TryInstallNativeFatExport();
    }

    private async void NativeFatExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_nativeFatExportPdfButton == null)
            return;

        await EnsureNativeFatLoadedAsync(forceReconcile: false);
        var device = SelectedDevice;
        var state = _nativeFatCurrentState;
        if (device == null || state == null ||
            !state.DeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            SetNativeFatStatus("Select an IED with a loaded FAT state before exporting.");
            return;
        }

        var includeHistorical = _nativeFatShowHistoricalCheck?.IsChecked == true;
        var snapshot = NativeFatReportSnapshotBuilder.Build(device, state);
        var dialog = new SaveFileDialog
        {
            Title = "Export ARSAS native FAT evidence PDF",
            Filter = "PDF evidence report (*.pdf)|*.pdf",
            FileName = $"{SafeNativeFatFileName(device.Name)}_FAT_{DateTime.Now:yyyyMMdd_HHmm}.pdf",
            AddExtension = true,
            DefaultExt = ".pdf",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var previousContent = _nativeFatExportPdfButton.Content;
        try
        {
            _nativeFatExportPdfButton.IsEnabled = false;
            _nativeFatExportPdfButton.Content = "Exporting…";
            SetNativeFatStatus("Saving current FAT state and building evidence PDF…");

            // Persist the exact state used to construct the snapshot before delivering
            // an external evidence file. The snapshot itself remains frozen thereafter.
            await SaveNativeFatStateAsync(state);
            await Task.Run(() => NativeFatPdfReportService.Save(
                dialog.FileName,
                snapshot,
                includeHistorical));

            SetNativeFatStatus($"PDF exported · {Path.GetFileName(dialog.FileName)}");
            AddLog(
                "INFO",
                "Native FAT",
                $"Native FAT PDF exported for {device.Name}: {dialog.FileName}" +
                (includeHistorical ? " (historical included)" : string.Empty));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            AddLog("WARN", "Native FAT", $"PDF export failed: {ex.Message}");
            SetNativeFatStatus("PDF export failed. Native FAT state remains saved.");
            MessageBox.Show(
                this,
                ex.Message,
                "Native FAT PDF export failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _nativeFatExportPdfButton.Content = previousContent ?? "Export PDF";
            _nativeFatExportPdfButton.IsEnabled = true;
        }
    }

    private static string SafeNativeFatFileName(string? value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "IED" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = new string(source.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim()
            .Trim('.');
        return string.IsNullOrWhiteSpace(result) ? "IED" : result;
    }

    private void NativeFatExport_MainWindowClosed(object? sender, EventArgs e)
    {
        _nativeFatExportInstallRetry?.Stop();
        if (_nativeFatExportPdfButton != null)
            _nativeFatExportPdfButton.Click -= NativeFatExportPdf_Click;
        Closed -= NativeFatExport_MainWindowClosed;
    }
}
