using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AR.Iec61850.FaultRecords;

namespace ArIED61850Tester;

/// <summary>
/// Provides one operator selection model for both first-time downloads and re-downloads.
/// FaultRecordRow.IsSelected is the only selection authority. A downloaded row remains
/// selectable; its local state changes transfer semantics to a staged, validated, atomic
/// replacement so a known-good local COMTRADE package is never deleted before its fresh copy
/// is ready to commit.
/// </summary>
public partial class FaultRecordWindow
{
    private readonly Dictionary<FaultRecordRow, PropertyChangedEventHandler> _redownloadRowHandlers = new();
    private Button? _smartDownloadButton;
    private TextBlock? _smartSelectionSummary;
    private bool _redownloadUxInstalled;
    private bool _smartDownloadRunning;

    private void InstallRedownloadUx()
    {
        if (_redownloadUxInstalled)
            return;

        _redownloadUxInstalled = true;

        ToastHost.HorizontalAlignment = HorizontalAlignment.Center;
        ToastHost.VerticalAlignment = VerticalAlignment.Center;
        ToastHost.Margin = new Thickness(0);

        FaultRecordsGrid.LoadingRow += RedownloadUx_LoadingRow;
        Records.CollectionChanged += RedownloadUx_RecordsChanged;
        PropertyChanged += RedownloadUx_WindowPropertyChanged;
        Closed += RedownloadUx_Closed;

        foreach (var row in Records)
            AttachRedownloadRow(row);

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                ResolveSmartDownloadControls();
                ConfigureVisibleRecordRows();
                UpdateSmartSelectionUi();
            }));
    }

    private void RedownloadUx_Closed(object? sender, EventArgs e)
    {
        FaultRecordsGrid.LoadingRow -= RedownloadUx_LoadingRow;
        Records.CollectionChanged -= RedownloadUx_RecordsChanged;
        PropertyChanged -= RedownloadUx_WindowPropertyChanged;

        if (_smartDownloadButton != null)
        {
            _smartDownloadButton.PreviewMouseLeftButtonDown -= SmartDownloadButton_PreviewMouseLeftButtonDown;
            _smartDownloadButton.PreviewKeyDown -= SmartDownloadButton_PreviewKeyDown;
        }

        foreach (var pair in _redownloadRowHandlers)
            pair.Key.PropertyChanged -= pair.Value;
        _redownloadRowHandlers.Clear();
    }

    private void RedownloadUx_WindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IsBusy) or nameof(CanDownload) or nameof(SelectionSummary))
            UpdateSmartSelectionUi();
    }

    private void RedownloadUx_RecordsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var row in e.OldItems.OfType<FaultRecordRow>())
                DetachRedownloadRow(row);
        }

        if (e.NewItems != null)
        {
            foreach (var row in e.NewItems.OfType<FaultRecordRow>())
                AttachRedownloadRow(row);
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var pair in _redownloadRowHandlers.ToArray())
                DetachRedownloadRow(pair.Key);
            foreach (var row in Records)
                AttachRedownloadRow(row);
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                ResolveSmartDownloadControls();
                ConfigureVisibleRecordRows();
                UpdateSmartSelectionUi();
            }));
    }

    private void AttachRedownloadRow(FaultRecordRow row)
    {
        if (_redownloadRowHandlers.ContainsKey(row))
            return;

        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName is nameof(FaultRecordRow.IsSelected) or
                nameof(FaultRecordRow.LocalState) or
                nameof(FaultRecordRow.Status) or
                nameof(FaultRecordRow.CanSelectForDownload))
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.DataBind,
                    new Action(() =>
                    {
                        ConfigureRecordRow(row);
                        UpdateSmartSelectionUi();
                    }));
            }
        };

        _redownloadRowHandlers[row] = handler;
        row.PropertyChanged += handler;
    }

    private void DetachRedownloadRow(FaultRecordRow row)
    {
        if (_redownloadRowHandlers.Remove(row, out var handler))
            row.PropertyChanged -= handler;
    }

    private void RedownloadUx_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => ConfigureDataGridRow(e.Row)));
    }

    private void ConfigureVisibleRecordRows()
    {
        FaultRecordsGrid.UpdateLayout();
        foreach (var row in Records)
            ConfigureRecordRow(row);
    }

    private void ConfigureRecordRow(FaultRecordRow row)
    {
        if (FaultRecordsGrid.ItemContainerGenerator.ContainerFromItem(row) is DataGridRow gridRow)
            ConfigureDataGridRow(gridRow);
    }

    private void ConfigureDataGridRow(DataGridRow gridRow)
    {
        if (gridRow.DataContext is not FaultRecordRow row)
            return;

        var checkBox = FindVisualDescendants<CheckBox>(gridRow).FirstOrDefault();
        if (checkBox == null)
            return;

        BindingOperations.SetBinding(
            checkBox,
            ToggleButton.IsCheckedProperty,
            new Binding(nameof(FaultRecordRow.IsSelected))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        BindingOperations.SetBinding(
            checkBox,
            UIElement.IsEnabledProperty,
            new Binding(nameof(FaultRecordRow.CanSelectForDownload))
            {
                Mode = BindingMode.OneWay
            });
        checkBox.ToolTip = row.LocalState == FaultRecordLocalState.Downloaded
            ? "Already downloaded. Select to fetch a fresh relay copy and atomically replace the existing local package."
            : null;
    }

    private void ResolveSmartDownloadControls()
    {
        if (_smartDownloadButton == null)
        {
            _smartDownloadButton = FindVisualDescendants<Button>(this)
                .FirstOrDefault(button =>
                    string.Equals(button.Content?.ToString(), "Download selected", StringComparison.Ordinal));

            if (_smartDownloadButton != null)
            {
                BindingOperations.ClearBinding(_smartDownloadButton, UIElement.IsEnabledProperty);
                _smartDownloadButton.PreviewMouseLeftButtonDown += SmartDownloadButton_PreviewMouseLeftButtonDown;
                _smartDownloadButton.PreviewKeyDown += SmartDownloadButton_PreviewKeyDown;
            }
        }

        if (_smartSelectionSummary == null)
        {
            _smartSelectionSummary = FindVisualDescendants<TextBlock>(this)
                .FirstOrDefault(textBlock =>
                {
                    var expression = BindingOperations.GetBindingExpression(textBlock, TextBlock.TextProperty);
                    return string.Equals(
                        expression?.ParentBinding.Path?.Path,
                        nameof(SelectionSummary),
                        StringComparison.Ordinal);
                });

            if (_smartSelectionSummary != null)
                BindingOperations.ClearBinding(_smartSelectionSummary, TextBlock.TextProperty);
        }
    }

    private void SmartDownloadButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        e.Handled = true;
        StartSmartDownload();
    }

    private void SmartDownloadButton_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space))
            return;

        e.Handled = true;
        StartSmartDownload();
    }

    private async void StartSmartDownload()
    {
        if (_smartDownloadRunning || IsBusy)
            return;

        _smartDownloadRunning = true;
        try
        {
            await RunSmartDownloadAsync().ConfigureAwait(true);
        }
        finally
        {
            _smartDownloadRunning = false;
            UpdateSmartSelectionUi();
        }
    }

    private async Task RunSmartDownloadAsync()
    {
        var selected = Records
            .Where(row => row.IsSelected && row.CanSelectForDownload)
            .ToArray();

        if (selected.Length == 0)
        {
            StatusText = "Select at least one available fault record.";
            ShowToast("Select a record first.", ToastKind.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(DestinationDirectory))
        {
            StatusText = "Choose a local destination directory before downloading.";
            ShowToast("Choose a download folder first.", ToastKind.Information);
            return;
        }

        ResetOperationCancellation();
        IsBusy = true;
        IsIndeterminate = false;
        ProgressValue = 0;
        UpdateSmartSelectionUi();

        var completedRecords = 0;
        var overwrittenRecords = 0;
        var failedRecords = 0;
        long downloadedBytes = 0;

        try
        {
            Directory.CreateDirectory(DestinationDirectory);
            await _client.ConnectAsync(_host, _port, _operationCancellation!.Token);

            for (var index = 0; index < selected.Length; index++)
            {
                _operationCancellation.Token.ThrowIfCancellationRequested();
                var row = selected[index];
                var previousDirectory = row.LocalDirectory;
                var overwriteExisting = row.LocalState == FaultRecordLocalState.Downloaded &&
                                        !string.IsNullOrWhiteSpace(previousDirectory) &&
                                        Directory.Exists(previousDirectory);
                var stagingRoot = overwriteExisting
                    ? Path.Combine(Path.GetFullPath(DestinationDirectory), $".arsas-redownload-{Guid.NewGuid():N}")
                    : DestinationDirectory;

                row.Status = "Downloading";
                row.Detail = string.Empty;
                StatusText = overwriteExisting
                    ? $"Downloading a fresh copy of {row.RecordName} before overwrite ({index + 1}/{selected.Length})…"
                    : $"Downloading {row.RecordName} ({index + 1}/{selected.Length})…";

                var recordIndex = index;
                var progress = new Progress<Iec61850FaultRecordDownloadProgress>(item =>
                {
                    var withinRecord = item.ExpectedBytes is > 0
                        ? Math.Clamp(item.BytesTransferred / (double)item.ExpectedBytes.Value, 0d, 1d)
                        : item.TotalFiles > 0
                            ? Math.Clamp(item.CompletedFiles / (double)item.TotalFiles, 0d, 1d)
                            : 0d;
                    ProgressValue = ((recordIndex + withinRecord) / selected.Length) * 100d;
                    StatusText = $"{row.RecordName}: {FormatBytes(item.BytesTransferred)} transferred, file {item.CompletedFiles}/{item.TotalFiles}.";
                });

                try
                {
                    if (overwriteExisting)
                        Directory.CreateDirectory(stagingRoot);

                    var result = await _client.DownloadAsync(
                        row.Record,
                        stagingRoot,
                        progress,
                        _operationCancellation.Token);

                    if (!result.IsSuccess)
                    {
                        failedRecords++;
                        row.Status = "Failed";
                        row.Detail = result.Message;
                        continue;
                    }

                    ValidateFreshRecordDirectory(row.Record, result.DestinationDirectory);
                    var committedDirectory = CommitFreshRecordDirectory(
                        previousDirectory,
                        result.DestinationDirectory,
                        DestinationDirectory,
                        row.RecordName);

                    completedRecords++;
                    if (overwriteExisting)
                        overwrittenRecords++;
                    downloadedBytes += result.BytesTransferred;
                    row.MarkDownloaded(committedDirectory);
                    row.IsSelected = false;
                    ConfigureRecordRow(row);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // A single corrupt/missing COMTRADE package must not abort the remaining
                    // selected records. The old downloaded package is untouched until commit;
                    // first-time partial files remain honestly detectable as Local partial.
                    failedRecords++;
                    row.Status = "Failed";
                    row.Detail = $"{ex.GetType().Name}: {ex.Message}";
                }
                finally
                {
                    if (overwriteExisting)
                        TryRemoveDirectory(stagingRoot);
                    ProgressValue = ((index + 1d) / selected.Length) * 100d;
                }
            }

            RefreshLocalDownloadStates(preserveFailureStatus: true);
            StatusText = failedRecords == 0
                ? overwrittenRecords > 0
                    ? $"Downloaded {completedRecords:N0} record(s); atomically replaced {overwrittenRecords:N0} existing local record(s)."
                    : $"Downloaded {completedRecords:N0} record(s), {FormatBytes(downloadedBytes)}, to '{DestinationDirectory}'."
                : $"Downloaded {completedRecords:N0} record(s); {failedRecords:N0} failed. Failed rows remain selected for retry.";

            if (failedRecords == 0)
            {
                var toast = completedRecords == 1
                    ? overwrittenRecords == 1
                        ? $"File downloaded and replaced safely — {selected[0].RecordName}"
                        : $"File downloaded — {selected[0].RecordName}"
                    : overwrittenRecords > 0
                        ? $"{completedRecords:N0} downloaded; {overwrittenRecords:N0} replaced safely."
                        : $"{completedRecords:N0} fault records downloaded successfully.";
                ShowToast(toast, ToastKind.Success);
            }
            else if (completedRecords > 0)
            {
                ShowToast($"{completedRecords:N0} downloaded, {failedRecords:N0} failed.", ToastKind.Warning);
            }
            else
            {
                ShowToast("Download failed. Failed rows remain selected for retry.", ToastKind.Error);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Fault-record download cancelled. Staged temporary files were cleaned up.";
            ShowToast("Download cancelled safely.", ToastKind.Information);
        }
        catch (Exception ex)
        {
            StatusText = $"Fault-record download failed before record processing: {ex.Message}";
            ShowToast("Download failed. Review transfer diagnostics.", ToastKind.Error);
        }
        finally
        {
            IsIndeterminate = false;
            IsBusy = false;
            RaiseSelectionState();
            ConfigureVisibleRecordRows();
            UpdateSmartSelectionUi();
        }
    }

    private static void ValidateFreshRecordDirectory(
        Iec61850FaultRecordSet record,
        string freshDirectory)
    {
        if (string.IsNullOrWhiteSpace(freshDirectory) || !Directory.Exists(freshDirectory))
            throw new InvalidDataException("The relay transfer did not produce a record directory.");

        if (record.Files.Count == 0)
            throw new InvalidDataException("The relay record contains no transferable files.");

        foreach (var remoteFile in record.Files)
        {
            var localPath = Path.Combine(freshDirectory, SanitizeLocalFileName(remoteFile.Name));
            if (!File.Exists(localPath))
            {
                throw new InvalidDataException(
                    $"Fresh record validation failed because '{remoteFile.Name}' is missing.");
            }

            if (remoteFile.SizeBytes is not > 0)
                continue;

            var actualBytes = new FileInfo(localPath).Length;
            if (actualBytes != remoteFile.SizeBytes.Value)
            {
                throw new InvalidDataException(
                    $"Fresh record validation failed for '{remoteFile.Name}': expected {remoteFile.SizeBytes.Value:N0} bytes, got {actualBytes:N0}.");
            }
        }
    }

    private static string CommitFreshRecordDirectory(
        string previousDirectory,
        string freshDirectory,
        string destinationRoot,
        string recordName)
    {
        if (string.IsNullOrWhiteSpace(previousDirectory) ||
            !Directory.Exists(previousDirectory) ||
            PathsMatch(previousDirectory, freshDirectory))
        {
            return freshDirectory;
        }

        if (!IsUnderDownloadRoot(previousDirectory, destinationRoot) ||
            !IsUnderDownloadRoot(freshDirectory, destinationRoot))
        {
            throw new InvalidOperationException(
                "The previous or fresh record directory is outside the selected download root.");
        }

        var backupDirectory = Path.Combine(
            Path.GetFullPath(destinationRoot),
            $".arsas-{SanitizeRedownloadName(recordName)}-{Guid.NewGuid():N}.previous");
        Directory.Move(previousDirectory, backupDirectory);

        try
        {
            Directory.Move(freshDirectory, previousDirectory);
        }
        catch
        {
            if (!Directory.Exists(previousDirectory) && Directory.Exists(backupDirectory))
            {
                try
                {
                    Directory.Move(backupDirectory, previousDirectory);
                }
                catch
                {
                    // The hidden backup remains recoverable if rollback cannot complete.
                }
            }

            throw;
        }

        TryRemoveDirectory(backupDirectory);
        return previousDirectory;
    }

    private static bool IsUnderDownloadRoot(string candidate, string root)
    {
        var rootFullPath = Path.GetFullPath(root);
        var candidateFullPath = Path.GetFullPath(candidate);
        var rootPrefix = rootFullPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootFullPath
            : rootFullPath + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return candidateFullPath.StartsWith(rootPrefix, comparison);
    }

    private static bool PathsMatch(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var leftFullPath = Path.GetFullPath(left)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rightFullPath = Path.GetFullPath(right)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return leftFullPath.Equals(rightFullPath, comparison);
    }

    private static string SanitizeRedownloadName(string value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "fault-record" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var characters = source
            .Select(character => character < ' ' || invalid.Contains(character) ? '_' : character)
            .ToArray();
        var result = new string(characters).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(result) ? "fault-record" : result;
    }

    private static void TryRemoveDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void UpdateSmartSelectionUi()
    {
        ResolveSmartDownloadControls();

        var selected = Records.Count(row => row.IsSelected && row.CanSelectForDownload);
        var downloaded = Records.Count(row => row.LocalState == FaultRecordLocalState.Downloaded);

        if (_smartSelectionSummary != null)
        {
            _smartSelectionSummary.Text =
                $"{Records.Count:N0} record(s) • {selected:N0} selected • {downloaded:N0} downloaded";
        }

        if (_smartDownloadButton != null)
            _smartDownloadButton.IsEnabled = !IsBusy && !_smartDownloadRunning && selected > 0;
    }

    private void HideStartupScanToast()
    {
        _toastTimer.Stop();
        ToastHost.BeginAnimation(OpacityProperty, null);
        ToastTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        ToastHost.Visibility = Visibility.Collapsed;
        ToastHost.Opacity = 0;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? child)
        where T : DependencyObject
    {
        var current = child;
        while (current != null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root == null)
            yield break;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;

            foreach (var nested in FindVisualDescendants<T>(child))
                yield return nested;
        }
    }
}
