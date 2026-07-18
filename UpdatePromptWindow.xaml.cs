using System.ComponentModel;
using System.Windows;

namespace ArIED61850Tester;

public partial class UpdatePromptWindow : Window
{
    private readonly AppUpdateService _service;
    private readonly UpdateManifest _manifest;
    private CancellationTokenSource? _downloadCancellation;
    private string? _installerPath;
    private bool _isDownloading;
    private bool _installerLaunched;
    private bool _deferred;

    internal UpdatePromptWindow(AppUpdateService service, UpdateManifest manifest)
    {
        InitializeComponent();
        _service = service;
        _manifest = manifest;
        CurrentVersionText.Text = service.CurrentVersion.ToString(3);
        LatestVersionText.Text = manifest.Version;
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_isDownloading)
            return;

        _downloadCancellation?.Dispose();
        _downloadCancellation = new CancellationTokenSource();
        SetDownloadingState(true);

        try
        {
            var progress = new Progress<UpdateDownloadProgress>(value =>
            {
                DownloadProgress.Value = value.Percentage;
                StatusText.Text = $"Downloading verified installer… {FormatBytes(value.BytesReceived)} of {FormatBytes(value.TotalBytes)}";
            });

            _installerPath = await _service.DownloadInstallerAsync(
                _manifest,
                progress,
                _downloadCancellation.Token);

            DownloadProgress.Value = 100;
            StatusText.Text = "Download complete and SHA-256 verified. Opening the installer…";
            await Task.Delay(350);

            _service.LaunchInstaller(_installerPath);
            _installerLaunched = true;
            _isDownloading = false;
            Close();
            Application.Current?.Shutdown();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Download cancelled. ARSAS remains on the current version.";
            SetDownloadingState(false);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"The update could not be installed automatically: {exception.Message}";
            OpenFolderButton.Visibility = Visibility.Visible;
            SetDownloadingState(false);
        }
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        if (_isDownloading)
        {
            StatusText.Text = "Cancelling download…";
            _downloadCancellation?.Cancel();
            return;
        }

        _service.Defer(_manifest);
        _deferred = true;
        Close();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _service.OpenInstallerFolder(_installerPath);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Windows could not open the download folder: {exception.Message}";
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isDownloading)
        {
            e.Cancel = true;
            StatusText.Text = "Cancelling download…";
            _downloadCancellation?.Cancel();
            return;
        }

        if (!_installerLaunched && !_deferred)
        {
            _service.Defer(_manifest);
            _deferred = true;
        }

        _downloadCancellation?.Dispose();
        _downloadCancellation = null;
    }

    private void SetDownloadingState(bool downloading)
    {
        _isDownloading = downloading;
        ProgressPanel.Visibility = Visibility.Visible;
        InstallButton.IsEnabled = !downloading;
        LaterButton.Content = downloading ? "Cancel download" : "Later";
        LaterButton.IsEnabled = true;
        if (!downloading)
            InstallButton.Content = "Try again";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
            return "0 MB";
        return $"{bytes / 1024d / 1024d:0.0} MB";
    }
}
