using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using AR.Iec61850.FaultRecords;
using ArIED61850Tester.Services;
using Microsoft.Win32;

namespace ArIED61850Tester;

public partial class FaultRecordWindow : Window, INotifyPropertyChanged
{
    private readonly FaultRecordTransferClient _client = new();
    private readonly string _host;
    private readonly int _port;
    private CancellationTokenSource? _operationCancellation;
    private string _remoteDirectory = string.Empty;
    private string _destinationDirectory;
    private string _statusText = "Ready to scan the relay file store.";
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

        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FaultRecordRow> Records { get; } = new();

    public string DeviceName { get; }
    public string EndpointText => $"{_host}:{_port}";

    public string RemoteDirectory
    {
        get => _remoteDirectory;
        set => Set(ref _remoteDirectory, value ?? string.Empty);
    }

    public string DestinationDirectory
    {
        get => _destinationDirectory;
        set => Set(ref _destinationDirectory, value ?? string.Empty);
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

    public bool CanDownload => !IsBusy && Records.Any(row => row.IsSelected && row.Record.IsComplete);

    public string SelectionSummary
    {
        get
        {
            var selected = Records.Count(row => row.IsSelected);
            return $"{Records.Count:N0} record(s) • {selected:N0} selected";
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
        StatusText = $"Connecting a dedicated IEC 61850 file-transfer session to {EndpointText}…";

        try
        {
            await _client.ConnectAsync(_host, _port, _operationCancellation!.Token);
            StatusText = string.IsNullOrWhiteSpace(RemoteDirectory)
                ? "Scanning the remote file-store root and bounded subdirectories…"
                : $"Scanning '{RemoteDirectory.Trim()}' and bounded subdirectories…";

            var catalog = await _client.DiscoverAsync(
                RemoteDirectory,
                _operationCancellation.Token);

            Records.Clear();
            foreach (var record in catalog.Records)
            {
                var row = new FaultRecordRow(record);
                row.PropertyChanged += RecordRow_PropertyChanged;
                Records.Add(row);
            }

            StatusText = catalog.Records.Count == 0
                ? "No supported fault-record files were found. Try a known remote directory such as COMTRADE or DR."
                : $"{catalog.Summary} Select one or more complete records to download.";

            if (catalog.Diagnostics.Count > 0)
                StatusText += $" {catalog.Diagnostics.Count} bounded browse diagnostic(s) were reported.";

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

        if (dialog.ShowDialog(this) == true)
            DestinationDirectory = dialog.FolderName;
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
            return;

        var selected = Records
            .Where(row => row.IsSelected && row.Record.IsComplete)
            .ToArray();
        if (selected.Length == 0)
        {
            StatusText = "Select at least one complete COMTRADE record.";
            return;
        }

        if (string.IsNullOrWhiteSpace(DestinationDirectory))
        {
            StatusText = "Choose a local destination directory before downloading.";
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
                    row.Status = "Downloaded";
                    row.LocalDirectory = result.DestinationDirectory;
                }
                else
                {
                    failedRecords++;
                    row.Status = "Failed";
                    row.Detail = result.Message;
                }

                ProgressValue = ((index + 1d) / selected.Length) * 100d;
            }

            StatusText = failedRecords == 0
                ? $"Downloaded {completedRecords} record(s), {FormatBytes(downloadedBytes)}, to '{DestinationDirectory}'."
                : $"Downloaded {completedRecords} record(s); {failedRecords} failed. Select a failed row to review its status.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Fault-record download cancelled. Partial temporary files were cleaned up.";
        }
        catch (Exception ex)
        {
            StatusText = $"Fault-record download failed: {ex.Message}";
        }
        finally
        {
            IsIndeterminate = false;
            IsBusy = false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        StatusText = "Cancelling the current file operation…";
        _operationCancellation?.Cancel();
    }

    private void RecordRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FaultRecordRow.IsSelected))
            RaiseSelectionState();
    }

    private void RaiseSelectionState()
    {
        Raise(nameof(SelectionSummary));
        Raise(nameof(CanDownload));
    }

    private void ResetOperationCancellation()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
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
}

public sealed class FaultRecordRow : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _status;
    private string _detail = string.Empty;
    private string _localDirectory = string.Empty;

    public FaultRecordRow(Iec61850FaultRecordSet record)
    {
        Record = record ?? throw new ArgumentNullException(nameof(record));
        _status = record.IsComplete ? "Ready" : "Incomplete";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Iec61850FaultRecordSet Record { get; }
    public string RecordName => Record.BaseName;
    public string RemoteDirectory => string.IsNullOrWhiteSpace(Record.RemoteDirectory) ? "/" : Record.RemoteDirectory;
    public string Completeness => Record.Completeness;
    public string ModifiedText => Record.LastModifiedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "-";
    public string SizeText => Record.HasUnknownSize ? "Unknown" : FormatBytes(Record.KnownSizeBytes);
    public string FilesText => string.Join(", ", Record.Files.Select(file => file.Extension.TrimStart('.').ToUpperInvariant()));

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
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
