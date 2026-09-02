using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;
using Microsoft.Win32;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private FatVerificationWindow? _fatV2Window;
    private FatSclWorkspaceLaunchResult? _fatV2Launch;

    private void ScheduleFatV2LauncherInstall()
    {
        // FAT v2 remains additive to the proven workbook FAT path. Install SCL/project
        // commands after the existing first-run card has been constructed.
        Dispatcher.BeginInvoke(
            new Action(InstallFatV2Launcher),
            DispatcherPriority.Background);
    }

    private void InstallFatV2Launcher()
    {
        if (_ioListTestingLauncherCard is not Border { Child: StackPanel content })
            return;
        if (content.Children.OfType<Button>().Any(button => Equals(button.Tag, "FatV2SclLauncher")))
            return;

        var micro = content.Children.OfType<TextBlock>().FirstOrDefault();
        var title = content.Children.OfType<TextBlock>().Skip(1).FirstOrDefault();
        var description = content.Children.OfType<TextBlock>().Skip(2).FirstOrDefault();
        if (micro != null)
            micro.Text = "FAT / DATASET VERIFICATION";
        if (title != null)
            title.Text = "Run FAT from SCL or IO List";
        if (description != null)
        {
            description.Text =
                "Create FAT v2 directly from every static IEC 61850 DataSet membership, reopen a portable FAT v2 ARSAS project, or continue using the proven Excel IO List workflow.";
        }

        var sclButton = CreateLauncherButton(
            "Open SCL for FAT v2",
            "LucideFileInput",
            "PrimaryButton",
            OpenFatV2Scl_Click,
            Brushes.White,
            new Thickness(0, 0, 0, 8));
        sclButton.Tag = "FatV2SclLauncher";

        var projectButton = CreateLauncherButton(
            "Open FAT v2 ARSAS Project",
            "LucideFolderOpen",
            "SecondaryButton",
            OpenFatV2Project_Click,
            Brush("#1F2937"),
            new Thickness(0, 0, 0, 8));
        projectButton.Tag = "FatV2ProjectLauncher";

        var firstExistingButton = content.Children.OfType<Button>().FirstOrDefault();
        var insertAt = firstExistingButton == null
            ? content.Children.Count
            : content.Children.IndexOf(firstExistingButton);
        content.Children.Insert(insertAt, projectButton);
        content.Children.Insert(insertAt, sclButton);
    }

    private async void OpenFatV2Scl_Click(object sender, RoutedEventArgs e)
    {
        if (!CanOpenFatV2Workspace())
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Open IEC 61850 SCL for FAT v2",
            Filter = "IEC 61850 SCL (*.scd;*.cid;*.icd;*.iid;*.ssd)|*.scd;*.cid;*.icd;*.iid;*.ssd|XML SCL (*.xml)|*.xml|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var sourceNames = string.Join(", ", dialog.FileNames.Select(Path.GetFileName));
        SetStatus($"Creating FAT v2 from {dialog.FileNames.Length} SCL source(s)…");
        try
        {
            var launch = await new FatSclWorkspaceBootstrapService().OpenAsync(
                dialog.FileNames,
                IoTestingProjectsRoot(),
                _applicationCancellation.Token);
            ShowFatV2Workspace(launch, sourceNames);
        }
        catch (OperationCanceledException)
        {
            SetStatus("FAT v2 SCL import cancelled.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            ShowFatV2Failure(ex, "FAT v2 SCL import failed");
        }
    }

    private async void OpenFatV2Project_Click(object sender, RoutedEventArgs e)
    {
        if (!CanOpenFatV2Workspace())
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Open ARSAS FAT v2 Project",
            Filter = "ARSAS FAT v2 project (*.arsas)|*.arsas|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        SetStatus($"Opening FAT v2 project {Path.GetFileName(dialog.FileName)}…");
        try
        {
            var launch = await FatVerificationPackageService.OpenAsync(
                dialog.FileName,
                IoTestingProjectsRoot(),
                _applicationCancellation.Token);
            ShowFatV2Workspace(launch, Path.GetFileName(dialog.FileName));
        }
        catch (OperationCanceledException)
        {
            SetStatus("FAT v2 project open cancelled.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            ShowFatV2Failure(ex, "FAT v2 project open failed");
        }
    }

    private bool CanOpenFatV2Workspace()
    {
        if (_fatV2Window is { IsLoaded: true })
        {
            if (_fatV2Window.WindowState == WindowState.Minimized)
                _fatV2Window.WindowState = WindowState.Normal;
            _fatV2Window.Activate();
            return false;
        }

        if (_loadedIoFatWindow is { IsLoaded: true })
        {
            MessageBox.Show(
                this,
                "Close the active IO List FAT workspace before opening a FAT v2 workspace.",
                "FAT workspace already open",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }
        return true;
    }

    private void ShowFatV2Workspace(FatSclWorkspaceLaunchResult launch, string sourceLabel)
    {
        var digital = launch.Project.Signals.Count(signal => signal.SignalKind == FatSignalKind.Discrete);
        var analog = launch.Project.Signals.Count(signal => signal.SignalKind == FatSignalKind.Analog);
        var other = launch.Project.Signals.Count - digital - analog;
        var status =
            $"FAT v2 ready: {launch.Project.Signals.Count} static DataSet membership(s) from {launch.SourceFiles.Count} SCL source(s) — " +
            $"{digital} digital, {analog} analog, {other} other.";
        SetStatus(status);
        AddLog("INFO", "FAT v2", $"{status} Source: {sourceLabel}");

        var window = new FatVerificationWindow(launch) { Owner = this };
        _fatV2Window = window;
        _fatV2Launch = launch;
        _runtime.PointUpdated += Runtime_FatV2PointUpdated;
        SeedFatV2LiveValues(window);

        void WindowClosed(object? _, EventArgs __)
        {
            window.Closed -= WindowClosed;
            _runtime.PointUpdated -= Runtime_FatV2PointUpdated;
            try
            {
                FatVerificationPersistenceService.SaveNow(launch);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                AddLog("ERROR", "FAT v2 persistence", ex.Message);
                MarkDiagnosticAlert();
            }
            if (ReferenceEquals(_fatV2Window, window))
                _fatV2Window = null;
            if (ReferenceEquals(_fatV2Launch, launch))
                _fatV2Launch = null;
        }

        window.Closed += WindowClosed;
        window.Show();
    }

    private void ShowFatV2Failure(Exception ex, string title)
    {
        AddLog("ERROR", "FAT v2", ex.Message);
        MarkDiagnosticAlert();
        SetStatus($"{title}. The source SCL was not modified.");
        MessageBox.Show(
            this,
            ex.Message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void Runtime_FatV2PointUpdated(Iec61850PointSnapshot snapshot)
    {
        var window = _fatV2Window;
        if (window == null)
            return;

        var device = Devices.FirstOrDefault(item =>
            item.DeviceId.Equals(snapshot.Point.DeviceId, StringComparison.OrdinalIgnoreCase));
        window.ApplyLiveObservation(
            snapshot.Point.IecReference,
            FatV2IedAliases(snapshot.Point.DeviceName, device),
            snapshot.PreviousValue,
            snapshot.IsValueEdge,
            new FatLiveValueObservation(
                snapshot.Value,
                DateTimeOffset.UtcNow,
                ParseFatV2IedTimestamp(snapshot.DeviceTimestamp),
                snapshot.Quality,
                snapshot.SourceMode,
                snapshot.Sequence,
                1));
    }

    private void SeedFatV2LiveValues(FatVerificationWindow window)
    {
        foreach (var device in Devices)
        {
            foreach (var point in device.Points)
            {
                window.ApplyLiveObservation(
                    point.IecReference,
                    FatV2IedAliases(point.DeviceName, device),
                    point.Value,
                    isValueEdge: false,
                    new FatLiveValueObservation(
                        point.Value,
                        DateTimeOffset.UtcNow,
                        ParseFatV2IedTimestamp(point.DeviceTimestamp),
                        point.Quality,
                        point.SourceMode,
                        point.Sequence,
                        1));
            }
        }
    }

    private static IReadOnlyList<string> FatV2IedAliases(
        string runtimeDeviceName,
        Iec61850MonitorDevice? device)
    {
        var aliases = new[]
        {
            runtimeDeviceName,
            device?.Name,
            device?.SclIedName,
            device?.SclWorkspace?.IedName,
            device?.LiveDiscoveryModel?.IedName
        };
        return aliases
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DateTimeOffset? ParseFatV2IedTimestamp(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text is "-" or "—")
            return null;
        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }
}
