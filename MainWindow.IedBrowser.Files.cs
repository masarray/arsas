using System.ComponentModel;
using System.Diagnostics;
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
    private IedBrowserFileCatalog? _iedBrowserShownCatalog;
    private AR.Iec61850.Discovery.LiveIedModelDiscoveryDocument? _iedBrowserShownFileModel;
    private Iec61850MonitorDevice? _iedBrowserShownFileOwner;
    private bool _iedBrowserUpdatingFileSelection;
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
        IedBrowserFilesPanel.Visibility = isFiles ? Visibility.Visible : Visibility.Collapsed;
        if (!isFiles) return;
        if (ReferenceEquals(_iedBrowserShownFileOwner, SelectedDevice) &&
            ReferenceEquals(_iedBrowserShownFileModel, model) &&
            ReferenceEquals(_iedBrowserShownCatalog, _iedBrowserFileCatalog) &&
            IedBrowserFilesGrid.ItemsSource is not null)
        {
            RefreshIedBrowserFileSelection();
            return; // never rebuild the grid for a reporting/live-value tick
        }
        var previousPath = ReferenceEquals(_iedBrowserShownFileOwner, SelectedDevice)
            ? (IedBrowserFilesGrid.SelectedItem as IedBrowserRemoteFile)?.RemotePath
            : null;

        // Do not merge SCL-only files with live file identities of different
        // sources as though they were the same verified MMS directory entry.
        var live = _iedBrowserFileCatalog?.DisplayRows ??
            Array.Empty<IedBrowserRemoteFile>();
        // A verified bundle covers ALL of its engine-declared members;
        // otherwise SCL fallback could reintroduce CFG/DAT as duplicate
        // separate rows after we collapsed the live COMTRADE record.
        var livePaths = _iedBrowserFileCatalog?.Files
            .Select(file => file.RemotePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inferred = model?.FileDirectory.Entries
            .Where(file => !string.IsNullOrWhiteSpace(file.Path))
            .Where(file => !livePaths.Contains(file.Path))
            .Select(file => new IedBrowserRemoteFile(file.Name, file.Path,
                file.SizeBytes, DateTimeOffset.TryParse(file.LastModified,
                    out var modified) ? modified : null))
            ?? Enumerable.Empty<IedBrowserRemoteFile>();
        var rows = live.Concat(inferred)
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.RemotePath, StringComparer.Ordinal)
            .ToArray();
        _iedBrowserUpdatingFileSelection = true;
        try
        {
            IedBrowserFilesGrid.ItemsSource = rows;
            _iedBrowserShownFileOwner = SelectedDevice;
            _iedBrowserShownFileModel = model;
            _iedBrowserShownCatalog = _iedBrowserFileCatalog;
            // First file is visibly selected on initial browse, with a stable
            // identity-based restore across refresh. Never carry another IED's
            // previous row selection into this workspace.
            IedBrowserFilesGrid.SelectedItem = rows.FirstOrDefault(row =>
                previousPath != null &&
                string.Equals(row.RemotePath, previousPath, StringComparison.Ordinal)) ??
                rows.FirstOrDefault();
        }
        finally { _iedBrowserUpdatingFileSelection = false; }
        RefreshIedBrowserFileSelection();
    }

    private void IedBrowserFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_iedBrowserUpdatingFileSelection && ReferenceEquals(sender, IedBrowserFilesGrid))
            RefreshIedBrowserFileSelection();
    }

    private void RefreshIedBrowserFileSelection()
    {
        if (IedBrowserFileSelectionText == null || IedBrowserFilesDownloadSelectedButton == null)
            return;
        var selected = IedBrowserFilesGrid.SelectedItem as IedBrowserRemoteFile;
        var canDownload = selected is { IsDirectory: false } &&
            _iedBrowserFileCatalog?.TryGetRecord(selected.RemotePath, out _, out _) == true;
        IedBrowserFilesDownloadSelectedButton.IsEnabled = canDownload &&
            _iedBrowserFileTransferOperation is null;
        var members = canDownload && selected is { IsBundle: true } &&
            _iedBrowserFileCatalog!.TryGetRecord(selected.RemotePath,
                out var verifiedRecord, out _)
            ? " · " + string.Join(" + ", verifiedRecord.Files
                .Select(member => member.Extension.TrimStart('.').ToUpperInvariant())
                .Where(ext => ext.Length > 0)
                .Distinct(StringComparer.Ordinal))
            : "";
        IedBrowserFileSelectionText.Text = selected is null
            ? "No file selected · Click to select · Double-click to open"
            : selected.IsBundle
                ? $"Selected record: {selected.Name} · {selected.BundleMemberCount} files{members} · Double-click to open"
                : $"Selected: {selected.Name} · Browse-only unless a complete record is verified";
        IedBrowserFileSelectionText.ToolTip = selected?.RemotePath;
    }

    private void IedBrowserFile_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow { DataContext: IedBrowserRemoteFile file })
            IedBrowserFilesGrid.SelectedItem = file;
    }

    private async void IedBrowserDownloadSelected_Click(object sender, RoutedEventArgs e)
    {
        if (IedBrowserFilesGrid.SelectedItem is IedBrowserRemoteFile file)
            await TransferOrOpenIedBrowserFileAsync(file, openAfterDownload: false);
    }

    private void IedBrowserFile_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        // DataGrid recycling binds a row to different FileDirectory entries.
        // Install/rebind its contextual action on each actual row load, not in
        // a column-level XAML setter or against an earlier SelectedItem.
        if (e.Row.DataContext is not IedBrowserRemoteFile file) return;
        var download = new MenuItem
        {
            Header = "Download complete record", Tag = file,
            IsEnabled = !file.IsDirectory &&
                _iedBrowserFileCatalog?.TryGetRecord(file.RemotePath, out _, out _) == true
        };
        download.Click += IedBrowserFileDownload_Click;
        e.Row.ContextMenu = new ContextMenu { Items = { download } };
    }

    private async void IedBrowserFile_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGridRow { DataContext: IedBrowserRemoteFile file })
            return;
        IedBrowserFilesGrid.SelectedItem = file;
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

    private static void OpenIedBrowserDownloadedFolder(string directory)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("Verified COMTRADE folder no longer exists.");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Explorer is required to reveal the folder.");
        using var explorer = Process.Start(new ProcessStartInfo
        {
            FileName = Path.GetFullPath(directory),
            UseShellExecute = true,
            Verb = "open"
        });
        if (explorer is null)
            throw new Win32Exception("Windows Explorer could not open the COMTRADE folder.");
    }

    private async Task TransferOrOpenIedBrowserFileAsync(
        IedBrowserRemoteFile file, bool openAfterDownload)
    {
        var device = SelectedDevice;
        if (device is null || device.IsDemo || device.IsBusy ||
            _iedBrowserFileTransferOperation is not null ||
            _iedBrowserReadOperation is not null || _iedBrowserFilesLoading)
            return;

        var reason = "Read the IED FileDirectory first.";
        if (_iedBrowserFileCatalog is not { } catalog ||
            !catalog.TryGetRecord(file.RemotePath, out var record, out reason))
        {
            IedBrowserDetailStatus.Text = _iedBrowserFileCatalog is null
                ? "Read the IED FileDirectory first. SCL file references are not download proof."
                : reason;
            return;
        }

        if (string.IsNullOrWhiteSpace(device.IpAddress))
        {
            IedBrowserDetailStatus.Text = "An MMS IP address is required for verified file downloads.";
            return;
        }
        var destination = IedBrowserRecordProvenanceCache.DeviceRoot(
            BrowserRecordDestinationRoot, device.Name, device.IpAddress, device.Port);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_applicationCancellation.Token);
        cts.CancelAfter(TimeSpan.FromMinutes(10));
        _iedBrowserFileTransferOperation = cts;
        RefreshIedBrowserFileSelection();
        var source = device;
        var currentRecord = record;
        ShowBrowserFileTransfer($"Preparing {currentRecord.BaseName}…", indeterminate: true);
        try
        {
            string localDirectory;
            // Fingerprint complete local files off the dispatcher before reuse.
            // A cached package MUST match this IED+endpoint, MMS record identity,
            // timestamp and SHA-256 for every associated remote member.
            var cached = await Task.Run(() =>
            {
                var found = IedBrowserRecordProvenanceCache.TryFindVerified(
                    currentRecord, destination, out var existingPath);
                return (found, existingPath);
            }, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (cached.found)
                localDirectory = cached.existingPath;
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
                    currentRecord, destination, progress, cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                if (!IedBrowserLocalRecordEvidence.ValidateFresh(currentRecord, result,
                        out var validationError))
                    throw new InvalidDataException(validationError);
                localDirectory = result.DestinationDirectory;
                // Atomic local provenance sidecar, created only for a completely
                // validated package. Unknown/stale source metadata cannot be
                // treated as a reusable download on a later session.
                await Task.Run(() => IedBrowserRecordProvenanceCache.RecordVerifiedTransfer(
                    currentRecord, destination, localDirectory), cts.Token);
            }

            cts.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(source, SelectedDevice) ||
                MainTabs.SelectedIndex != IedBrowserWorkspaceIndex ||
                _iedBrowserNavigation.Current?.Key != "category/files")
                return; // never open a stale previous-IED or previous-panel result

            if (!openAfterDownload)
            {
                // Operator explicitly chose Download rather than Open. Reveal the
                // actual COMPLETE record folder in Windows Explorer, never a
                // process-launchable remote path or a partial staging directory.
                OpenIedBrowserDownloadedFolder(localDirectory);
                IedBrowserDetailStatus.Text = $"Downloaded · {currentRecord.BaseName} · {localDirectory}";
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
                RefreshIedBrowserFileSelection();
            }
            cts.Dispose();
        }
    }
}
