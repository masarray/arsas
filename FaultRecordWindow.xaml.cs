using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AR.Iec61850.FaultRecords;
using ArIED61850Tester.Services;
using Microsoft.Win32;

namespace ArIED61850Tester;

public partial class FaultRecordWindow : Window, INotifyPropertyChanged
{
    private readonly FaultRecordTransferClient _client = new();
    private readonly DispatcherTimer _toastTimer = new();
    private readonly string _host;
    private readonly int _port;
    private CancellationTokenSource? _operationCancellation;
    private string _remoteDirectory = string.Empty;
    private string _destinationDirectory;
    private string _statusText = "Opening the relay file store…";
    private bool _isBusy;
    private bool _isIndeterminate;
    private double _progressValue;

    public FaultRecordWindow(string deviceName, string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        InitializeComponent();

        DeviceName = string.IsNullOrWhiteSpace(deviceName) ? "IEC 61850 IED" : deviceName.Trim();
        _host = host.Trim();
        _port = port <= 0 ? 102 : port;
        _destinationDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "ARSAS Fault Records");

        _toastTimer.Interval = TimeSpan.FromSeconds(2.8);
        _toastTimer.Tick += ToastTimer_Tick;
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FaultRecordRow> Records { get; } = new();

    public string DeviceName { get; }
    public string EndpointText => $"{_host}:{_port}";

    // Retained internally for protocol diagnostics and compatibility. The compact UI
    // intentionally scans from the relay file-store root without exposing this field.
    public string RemoteDirectory
    {
        get => _remoteDirectory;
        set => Set(ref _remoteDirectory, value ?? string.Empty);
    }

    public string DestinationDirectory
    {
        get => _destinationDirectory;
        set
        {
            if (!Set(ref _destinationDirectory, value ?? string.Empty))
                return;

            RefreshLocalDownloadStates();
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value ?? string.Empty);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value))
                return;

            Raise(nameof(IsNotBusy));
            Raise(nameof(CanDownload));
        }
    }

    public bool IsNotBusy => !IsBusy;

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => Set(ref _isIndeterminate, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => Set(ref _progressValue, Math.Clamp(value, 0d, 100d));
    }

    public bool CanDownload =>
        !IsBusy && Records.Any(row => row.IsSelected && row.CanSelectForDownload);

    public string SelectionSummary
    {
        get
        {
            var selected = Records.Count(row => row.IsSelected && row.CanSelectForDownload);
            var downloaded = Records.Count(row => row.LocalState == FaultRecordLocalState.Downloaded);
            return $"{Records.Count:N0} record(s) • {selected:N0} selected • {downloaded:N0} downloaded";
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
        => await ScanAsync().ConfigureAwait(true);

    private async void Scan_Click(object sender, RoutedEventArgs e)
        => await ScanAsync().ConfigureAwait(true);

    private async Task ScanAsync()
    {
        if (IsBusy)
            return;

        ResetOperationCancellation();
        IsBusy = true;
        IsIndeterminate = true;
        ProgressValue = 0;
        StatusText = $"Connecting to {EndpointText} and reading fault records…";

        try
        {
            await _client.ConnectAsync(_host, _port, _operationCancellation!.Token);
            StatusText = "Scanning the relay file store…";

            var catalog = await _client.DiscoverAsync(
                remoteDirectory: null,
                _operationCancellation.Token);

            foreach (var existing in Records)
                existing.PropertyChanged -= RecordRow_PropertyChanged;
            Records.Clear();

            foreach (var record in catalog.Records)
            {
                var row = new FaultRecordRow(record);
                row.PropertyChanged += RecordRow_PropertyChanged;
                Records.Add(row);
            }

            RefreshLocalDownloadStates();
            var downloaded = Records.Count(row => row.LocalState == FaultRecordLocalState.Downloaded);
            StatusText = catalog.Records.Count == 0
                ? "No supported fault records were found in the relay file store."
                : $"Found {catalog.Records.Count:N0} fault record(s); {downloaded:N0} already exist in the selected local folder.";

            if (catalog.Diagnostics.Count > 0)
                StatusText += $" {catalog.Diagnostics.Count:N0} bounded browse diagnostic(s) were reported.";

            ProgressValue = 100;
            RaiseSelectionState();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Fault-record scan cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Fault-record scan failed: {ex.Message}";
            ShowToast("Could not scan fault records. Open diagnostics for details.", ToastKind.Error);
        }
        finally
        {
            IsIndeterminate = false;
            IsBusy = false;
        }
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose fault-record download folder",
            InitialDirectory = Directory.Exists(DestinationDirectory)
                ? DestinationDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
            return;

        DestinationDirectory = dialog.FolderName;
        var downloaded = Records.Count(row => row.LocalState == FaultRecordLocalState.Downloaded);
        StatusText = $"Local folder updated. {downloaded:N0} discovered record(s) already exist there.";
        ShowToast("Download folder updated and local files rechecked.", ToastKind.Information);
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
            return;

        var checkedRows = Records
            .Where(row => row.IsSelected && row.Record.Files.Count > 0)
            .ToArray();
        var selected = checkedRows
            .Where(row => row.CanSelectForDownload)
            .ToArray();
        var skippedRecords = checkedRows.Length - selected.Length;

        if (selected.Length == 0)
        {
            StatusText = skippedRecords > 0
                ? "The selected record already exists locally. Choose another record to download."
                : "Select at least one available fault record.";
            ShowToast(
                skippedRecords > 0 ? "Already downloaded — no duplicate was created." : "Select a record first.",
                ToastKind.Information);
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
        var completedRecords = 0;
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
                row.Status = "Downloading";
                row.Detail = string.Empty;
                StatusText = $"Downloading {row.RecordName} ({index + 1}/{selected.Length})…";

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

                var result = await _client.DownloadAsync(
                    row.Record,
                    DestinationDirectory,
                    progress,
                    _operationCancellation.Token);

                if (result.IsSuccess)
                {
                    completedRecords++;
                    downloadedBytes += result.BytesTransferred;
                    row.MarkDownloaded(result.DestinationDirectory);
                }
                else
                {
                    failedRecords++;
                    row.Status = "Failed";
                    row.Detail = result.Message;
                }

                ProgressValue = ((index + 1d) / selected.Length) * 100d;
            }

            RefreshLocalDownloadStates(preserveFailureStatus: true);
            StatusText = failedRecords == 0
                ? $"Downloaded {completedRecords:N0} record(s), {FormatBytes(downloadedBytes)}, to '{DestinationDirectory}'."
                : $"Downloaded {completedRecords:N0} record(s); {failedRecords:N0} failed. Select a failed row to review diagnostics.";

            if (failedRecords == 0)
            {
                ShowToast(
                    completedRecords == 1
                        ? $"File downloaded — {selected[0].RecordName}"
                        : $"{completedRecords:N0} fault records downloaded successfully.",
                    ToastKind.Success);
            }
            else if (completedRecords > 0)
            {
                ShowToast($"{completedRecords:N0} downloaded, {failedRecords:N0} failed.", ToastKind.Warning);
            }
            else
            {
                ShowToast("Download failed. Transfer diagnostics opened automatically.", ToastKind.Error);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Fault-record download cancelled. Partial temporary files were cleaned up.";
            ShowToast("Download cancelled safely.", ToastKind.Information);
        }
        catch (Exception ex)
        {
            StatusText = $"Fault-record download failed: {ex.Message}";
            ShowToast("Download failed. Review transfer diagnostics.", ToastKind.Error);
        }
        finally
        {
            IsIndeterminate = false;
            IsBusy = false;
            RaiseSelectionState();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        StatusText = "Cancelling the current file operation…";
        _operationCancellation?.Cancel();
    }

    private void RecordRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FaultRecordRow.IsSelected) or
            nameof(FaultRecordRow.LocalState) or
            nameof(FaultRecordRow.Status))
        {
            RaiseSelectionState();
        }
    }

    private void RaiseSelectionState()
    {
        Raise(nameof(SelectionSummary));
        Raise(nameof(CanDownload));
    }

    private void RefreshLocalDownloadStates(bool preserveFailureStatus = false)
    {
        if (Records.Count == 0)
        {
            RaiseSelectionState();
            return;
        }

        foreach (var row in Records)
        {
            var snapshot = DetectLocalDownload(row.Record, DestinationDirectory);
            row.ApplyLocalState(snapshot.State, snapshot.Directory, preserveFailureStatus);
        }

        RaiseSelectionState();
    }

    private static LocalDownloadSnapshot DetectLocalDownload(
        Iec61850FaultRecordSet record,
        string destinationRoot)
    {
        if (string.IsNullOrWhiteSpace(destinationRoot) || !Directory.Exists(destinationRoot))
            return new LocalDownloadSnapshot(FaultRecordLocalState.NotDownloaded, string.Empty);

        var safeRecordName = SanitizeLocalFileName(record.BaseName);
        string[] candidates;
        try
        {
            candidates = Directory
                .EnumerateDirectories(destinationRoot, $"{safeRecordName}*", SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    var name = Path.GetFileName(path);
                    return name.Equals(safeRecordName, StringComparison.OrdinalIgnoreCase) ||
                           name.StartsWith(safeRecordName + "-", StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(path => Directory.GetLastWriteTimeUtc(path))
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new LocalDownloadSnapshot(FaultRecordLocalState.NotDownloaded, string.Empty);
        }

        string partialDirectory = string.Empty;
        foreach (var candidate in candidates)
        {
            var matched = 0;
            foreach (var remoteFile in record.Files)
            {
                var localPath = Path.Combine(candidate, SanitizeLocalFileName(remoteFile.Name));
                if (!File.Exists(localPath))
                    continue;

                try
                {
                    var length = new FileInfo(localPath).Length;
                    var valid = remoteFile.SizeBytes is > 0
                        ? length == remoteFile.SizeBytes.Value
                        : true;
                    if (valid)
                        matched++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }

            if (matched == record.Files.Count && matched > 0)
                return new LocalDownloadSnapshot(FaultRecordLocalState.Downloaded, candidate);

            if (matched > 0 && string.IsNullOrWhiteSpace(partialDirectory))
                partialDirectory = candidate;
        }

        return string.IsNullOrWhiteSpace(partialDirectory)
            ? new LocalDownloadSnapshot(FaultRecordLocalState.NotDownloaded, string.Empty)
            : new LocalDownloadSnapshot(FaultRecordLocalState.Partial, partialDirectory);
    }

    private static string SanitizeLocalFileName(string value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "fault-record" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var characters = source
            .Select(character => character < ' ' || invalid.Contains(character) ? '_' : character)
            .ToArray();
        var sanitized = new string(characters).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(sanitized) ? "fault-record" : sanitized;
    }

    private void ShowToast(string message, ToastKind kind)
    {
        _toastTimer.Stop();
        ToastHost.BeginAnimation(OpacityProperty, null);
        ToastTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);

        ToastGlyph.Text = kind switch
        {
            ToastKind.Success => "✓",
            ToastKind.Warning => "!",
            ToastKind.Error => "×",
            _ => "i"
        };
        ToastText.Text = message;
        ToastHost.Background = CreateToastBrush(kind);
        ToastHost.Visibility = Visibility.Visible;
        ToastHost.Opacity = 0;
        ToastTranslate.Y = -10;

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        ToastHost.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = easing });
        ToastTranslate.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = easing });
        _toastTimer.Start();
    }

    private void ToastTimer_Tick(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            ToastHost.Visibility = Visibility.Collapsed;
            ToastHost.Opacity = 0;
        };
        ToastHost.BeginAnimation(OpacityProperty, fade);
    }

    private static Brush CreateToastBrush(ToastKind kind)
    {
        var colors = kind switch
        {
            ToastKind.Success => (Color.FromRgb(40, 176, 112), Color.FromRgb(24, 132, 84)),
            ToastKind.Warning => (Color.FromRgb(235, 166, 48), Color.FromRgb(193, 116, 24)),
            ToastKind.Error => (Color.FromRgb(223, 82, 96), Color.FromRgb(173, 48, 64)),
            _ => (Color.FromRgb(77, 132, 226), Color.FromRgb(48, 91, 177))
        };

        return new LinearGradientBrush(colors.Item1, colors.Item2, 25);
    }

    private void ResetOperationCancellation()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        await _client.DisposeAsync();
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, bytes);
        var index = 0;
        var display = (double)value;
        while (display >= 1024d && index < suffixes.Length - 1)
        {
            display /= 1024d;
            index++;
        }

        return index == 0
            ? $"{value.ToString("N0", CultureInfo.InvariantCulture)} {suffixes[index]}"
            : $"{display.ToString("N1", CultureInfo.InvariantCulture)} {suffixes[index]}";
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        Raise(propertyName);
        return true;
    }

    private void Raise([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private readonly record struct LocalDownloadSnapshot(
        FaultRecordLocalState State,
        string Directory);

    private enum ToastKind
    {
        Information,
        Success,
        Warning,
        Error
    }
}

public enum FaultRecordLocalState
{
    NotDownloaded,
    Partial,
    Downloaded
}

public sealed class FaultRecordRow : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _status;
    private string _detail = string.Empty;
    private string _localDirectory = string.Empty;
    private FaultRecordLocalState _localState;

    public FaultRecordRow(Iec61850FaultRecordSet record)
    {
        Record = record ?? throw new ArgumentNullException(nameof(record));
        _status = DefaultRemoteStatus;
        if (!record.IsComplete && record.Files.Count > 0)
            _detail = $"Available file(s) can be downloaded; COMTRADE companion coverage: {record.Completeness}.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Iec61850FaultRecordSet Record { get; }
    public string RecordName => Record.BaseName;
    public string RemoteDirectory => string.IsNullOrWhiteSpace(Record.RemoteDirectory) ? "/" : Record.RemoteDirectory;
    public string Completeness => Record.Completeness;
    public string ModifiedText => Record.LastModifiedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "-";
    public string SizeText => Record.HasUnknownSize ? "Unknown" : FormatBytes(Record.KnownSizeBytes);
    public string FilesText => string.Join(", ", Record.Files.Select(file =>
        string.IsNullOrWhiteSpace(file.Extension)
            ? "PACKAGE"
            : file.Extension.TrimStart('.').ToUpperInvariant()));

    public bool CanSelectForDownload => Record.Files.Count > 0 && LocalState != FaultRecordLocalState.Downloaded;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            var normalized = value && CanSelectForDownload;
            if (_isSelected == normalized)
                return;

            _isSelected = normalized;
            Raise();
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value)
                return;

            _status = value;
            Raise();
        }
    }

    public string Detail
    {
        get => _detail;
        set
        {
            if (_detail == value)
                return;

            _detail = value;
            Raise();
        }
    }

    public string LocalDirectory
    {
        get => _localDirectory;
        set
        {
            if (_localDirectory == value)
                return;

            _localDirectory = value;
            Raise();
        }
    }

    public FaultRecordLocalState LocalState
    {
        get => _localState;
        private set
        {
            if (_localState == value)
                return;

            _localState = value;
            if (_localState == FaultRecordLocalState.Downloaded)
                _isSelected = false;
            Raise();
            Raise(nameof(IsSelected));
            Raise(nameof(CanSelectForDownload));
        }
    }

    public void MarkDownloaded(string localDirectory)
    {
        LocalDirectory = localDirectory ?? string.Empty;
        LocalState = FaultRecordLocalState.Downloaded;
        Status = "Downloaded";
        Detail = string.IsNullOrWhiteSpace(LocalDirectory)
            ? "The complete record exists in the selected local folder."
            : $"Downloaded to '{LocalDirectory}'.";
    }

    public void ApplyLocalState(
        FaultRecordLocalState state,
        string localDirectory,
        bool preserveFailureStatus)
    {
        LocalDirectory = localDirectory ?? string.Empty;
        LocalState = state;

        if (state == FaultRecordLocalState.Downloaded)
        {
            Status = "Downloaded";
            Detail = string.IsNullOrWhiteSpace(LocalDirectory)
                ? "The complete record exists in the selected local folder."
                : $"Already downloaded to '{LocalDirectory}'.";
            return;
        }

        if (preserveFailureStatus && Status == "Failed")
            return;

        if (Status == "Downloading")
            return;

        if (state == FaultRecordLocalState.Partial)
        {
            Status = "Local partial";
            Detail = string.IsNullOrWhiteSpace(LocalDirectory)
                ? "Some companion files exist locally, but the record is incomplete."
                : $"Some companion files exist in '{LocalDirectory}', but the local record is incomplete.";
            return;
        }

        Status = DefaultRemoteStatus;
        if (Record.IsComplete)
            Detail = string.Empty;
        else if (Record.Files.Count > 0)
            Detail = $"Available file(s) can be downloaded; COMTRADE companion coverage: {Record.Completeness}.";
    }

    private string DefaultRemoteStatus => Record.Files.Count == 0
        ? "Unavailable"
        : Record.IsComplete
            ? "Ready"
            : "Partial";

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, bytes);
        var index = 0;
        var display = (double)value;
        while (display >= 1024d && index < suffixes.Length - 1)
        {
            display /= 1024d;
            index++;
        }

        return index == 0
            ? $"{value.ToString("N0", CultureInfo.InvariantCulture)} {suffixes[index]}"
            : $"{display.ToString("N1", CultureInfo.InvariantCulture)} {suffixes[index]}";
    }

    private void Raise([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}