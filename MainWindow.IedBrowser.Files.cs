using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AR.Iec61850.FaultRecords;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

/// <summary>
/// M2.5 isolated Files workspace. The existing fault-record client owns both
/// the FileDirectory association and bounded complete-bundle transfer. Browser
/// never downloads on navigation/selection and never opens a partial record.
/// </summary>
public partial class MainWindow
{
    private IedBrowserFileCatalog? _iedBrowserFileCatalog;
    private CancellationTokenSource? _iedBrowserFileTransferOperation;
    private static string BrowserRecordDestinationRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "ARSAS Fault Records");

    private void UpdateIedBrowserFilesView(IedBrowserTreeRow? page,
        AR.Iec61850.Discovery.LiveIedModelDiscoveryDocument? model)
    {
        if (IedBrowserFilesGrid == null) return;
        var isFiles = page?.Key == "category/files";
        if (!isFiles)
        {
            // Do not let a transfer complete/open a Viewer after navigating
            // away from Files while the same IED stays selected.
            _iedBrowserFileTransferOperation?.Cancel();
            IedBrowserFileTransferOverlay.Visibility = Visibility.Collapsed;
        }
        IedBrowserDetailList.Visibility = isFiles ? Visibility.Collapsed : Visibility.Visible;
        IedBrowserFilesGrid.Visibility = isFiles ? Visibility.Visible : Visibility.Collapsed;
        if (!isFiles) return;

        // Do not merge SCL-only files with live file identities of different
        // sources as though they were the same verified MMS directory entry.
        var live = _iedBrowserFileCatalog?.Files ?? Array.Empty<IedBrowserRemoteFile>();
        // Index live identities once: never run a linear search through
        // 20,000 FileDirectory entries for every SCL-declared file row.
        var livePaths = live.Select(file => file.RemotePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inferred = model?.FileDirectory.Entries
            .Where(file => !string.IsNullOrWhiteSpace(file.Path))
            .Where(file => !livePaths.Contains(file.Path))
            .Select(file => new IedBrowserRemoteFile(file.Name, file.Path,
                file.SizeBytes, DateTimeOffset.TryParse(file.LastModified,
                    out var modified) ? modified : null))
            ?? Enumerable.Empty<IedBrowserRemoteFile>();
        IedBrowserFilesGrid.ItemsSource = live.Concat(inferred)
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.RemotePath, StringComparer.Ordinal)
            .ToArray();
    }

    private async void IedBrowserFile_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGridRow { DataContext: IedBrowserRemoteFile file })
            return;
        e.Handled = true;
        await TransferOrOpenIedBrowserFileAsync(file, openAfterDownload: true);
    }

    private async void IedBrowserFileDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: IedBrowserRemoteFile file })
            return;
        e.Handled = true;
        await TransferOrOpenIedBrowserFileAsync(file, openAfterDownload: false);
    }

    private void IedBrowserCancelFileTransfer_Click(object sender, RoutedEventArgs e)
        => _iedBrowserFileTransferOperation?.Cancel();

    private void ShowBrowserFileTransfer(string message, bool indeterminate, double progress = 0)
    {
        if (IedBrowserFileTransferOverlay is null) return;
        IedBrowserFileTransferOverlay.Visibility = Visibility.Visible;
        IedBrowserFileTransferText.Text = message;
        IedBrowserFileTransferProgress.IsIndeterminate = indeterminate;
        IedBrowserFileTransferProgress.Value = Math.Clamp(progress, 0d, 100d);
    }

    private async Task TransferOrOpenIedBrowserFileAsync(
        IedBrowserRemoteFile file, bool openAfterDownload)
    {
        var device = SelectedDevice;
        if (device is null || device.IsDemo || device.IsBusy ||
            _iedBrowserFileTransferOperation is not null ||
            _iedBrowserReadOperation is not null || _iedBrowserFilesLoading)
            return;

        if (_iedBrowserFileCatalog is not { } catalog ||
            !catalog.TryGetRecord(file.RemotePath, out var record, out var reason))
        {
            IedBrowserDetailStatus.Text = _iedBrowserFileCatalog is null
                ? "Read the IED FileDirectory first. SCL file references are not download proof."
                : reason;
            return;
        }

        if (!openAfterDownload && IedBrowserLocalRecordEvidence.TryFindComplete(
                record, BrowserRecordDestinationRoot, out var prior))
        {
            IedBrowserDetailStatus.Text = $"Already downloaded: {prior}";
            return;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_applicationCancellation.Token);
        cts.CancelAfter(TimeSpan.FromMinutes(10));
        _iedBrowserFileTransferOperation = cts;
        var source = device;
        var currentRecord = record;
        ShowBrowserFileTransfer($"Preparing {currentRecord.BaseName}…", indeterminate: true);
        try
        {
            string localDirectory;
            if (IedBrowserLocalRecordEvidence.TryFindComplete(
                    currentRecord, BrowserRecordDestinationRoot, out var existing))
                localDirectory = existing; // no re-transfer of verified package
            else
            {
                if (string.IsNullOrWhiteSpace(source.IpAddress))
                    throw new InvalidOperationException("Bind an MMS IP address before downloading fault records.");

                var progress = new Progress<Iec61850FaultRecordDownloadProgress>(item =>
                {
                    if (!ReferenceEquals(_iedBrowserFileTransferOperation, cts) ||
                        cts.IsCancellationRequested) return;
                    var fraction = item.ExpectedBytes is > 0
                        ? item.BytesTransferred / (double)item.ExpectedBytes.Value
                        : item.TotalFiles > 0
                            ? item.CompletedFiles / (double)item.TotalFiles : 0d;
                    ShowBrowserFileTransfer(
                        $"{currentRecord.BaseName} · {item.CompletedFiles}/{item.TotalFiles} files · {item.BytesTransferred:N0} bytes",
                        indeterminate: item.ExpectedBytes is not > 0 && item.TotalFiles == 0,
                        progress: fraction * 100d);
                });
                await using var transfer = new FaultRecordTransferClient();
                await transfer.ConnectAsync(source.IpAddress, source.Port, cts.Token);
                var result = await transfer.DownloadAsync(
                    currentRecord, BrowserRecordDestinationRoot, progress, cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                if (!IedBrowserLocalRecordEvidence.ValidateFresh(currentRecord, result,
                        out var validationError))
                    throw new InvalidDataException(validationError);
                localDirectory = result.DestinationDirectory;
            }

            cts.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(source, SelectedDevice) ||
                MainTabs.SelectedIndex != IedBrowserWorkspaceIndex ||
                _iedBrowserNavigation.Current?.Key != "category/files")
                return; // never open a stale previous-IED or previous-panel result

            if (!openAfterDownload)
            {
                IedBrowserDetailStatus.Text = $"Download complete · {currentRecord.BaseName}";
                return;
            }
            if (!ArdIrecViewerLauncher.TryResolveComtradeCfg(
                    localDirectory, currentRecord.BaseName, out var cfgPath, out var resolveError))
                throw new InvalidDataException(resolveError);

            ShowBrowserFileTransfer($"Opening {Path.GetFileName(cfgPath)} in native COMTRADE Viewer…",
                indeterminate: true);
            // Decoding large COMTRADE records is native and potentially slow:
            // never block the WPF UI dispatcher or the monitoring association.
            var opened = await Task.Run(() =>
            {
                var ok = ArdIrecNativeBridge.TryOpen(cfgPath, out var nativeRecord, out var error);
                return (ok, nativeRecord, error);
            }, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested ||
                !ReferenceEquals(source, SelectedDevice) ||
                MainTabs.SelectedIndex != IedBrowserWorkspaceIndex ||
                _iedBrowserNavigation.Current?.Key != "category/files")
            {
                opened.nativeRecord?.Dispose();
                return;
            }
            if (!opened.ok || opened.nativeRecord is null)
                throw new InvalidDataException(opened.error);

            try
            {
                var workspace = new ComtradeWorkspaceWindow(opened.nativeRecord) { Owner = this };
                workspace.Show();
            }
            catch
            {
                opened.nativeRecord.Dispose();
                throw;
            }
            IedBrowserDetailStatus.Text = $"Opened native COMTRADE · {currentRecord.BaseName}";
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(source, SelectedDevice))
                IedBrowserDetailStatus.Text = "File transfer cancelled · no partial COMTRADE package opened.";
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(source, SelectedDevice))
                IedBrowserDetailStatus.Text = $"COMTRADE: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_iedBrowserFileTransferOperation, cts))
            {
                _iedBrowserFileTransferOperation = null;
                IedBrowserFileTransferOverlay.Visibility = Visibility.Collapsed;
            }
            cts.Dispose();
        }
    }
}
