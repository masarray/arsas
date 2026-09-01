using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Services.IoTesting;
using Microsoft.Win32;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private FatVerificationWindow? _fatV2Window;

    static MainWindow()
    {
        // P4 remains additive to the proven workbook FAT path. Install the SCL FAT command
        // after the existing first-run card has been constructed instead of rewriting the
        // legacy launcher or its session lifecycle.
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MainWindow_FatV2Loaded));
    }

    private static void MainWindow_FatV2Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
            return;
        window.Dispatcher.BeginInvoke(
            new Action(window.InstallFatV2Launcher),
            DispatcherPriority.Background);
    }

    private void InstallFatV2Launcher()
    {
        if (_ioListTestingLauncherCard?.Child is not StackPanel content)
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
                "Create FAT v2 directly from every static IEC 61850 DataSet membership in one or more SCL files, or continue using the proven Excel IO List workflow.";
        }

        var button = CreateLauncherButton(
            "Open SCL for FAT v2",
            "LucideFileInput",
            "PrimaryButton",
            OpenFatV2Scl_Click,
            Brushes.White,
            new Thickness(0, 0, 0, 8));
        button.Tag = "FatV2SclLauncher";

        var firstExistingButton = content.Children
            .OfType<Button>()
            .FirstOrDefault();
        var insertAt = firstExistingButton == null
            ? content.Children.Count
            : content.Children.IndexOf(firstExistingButton);
        content.Children.Insert(insertAt, button);
    }

    private async void OpenFatV2Scl_Click(object sender, RoutedEventArgs e)
    {
        if (_fatV2Window is { IsLoaded: true })
        {
            if (_fatV2Window.WindowState == WindowState.Minimized)
                _fatV2Window.WindowState = WindowState.Normal;
            _fatV2Window.Activate();
            return;
        }

        if (_loadedIoFatWindow is { IsLoaded: true })
        {
            MessageBox.Show(
                this,
                "Close the active IO List FAT workspace before opening a FAT v2 SCL workspace.",
                "FAT workspace already open",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

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
            var bootstrap = new FatSclWorkspaceBootstrapService();
            var launch = await bootstrap.OpenAsync(
                dialog.FileNames,
                IoTestingProjectsRoot(),
                _applicationCancellation.Token);

            var digital = launch.Project.Signals.Count(signal => signal.SignalKind == Models.IoTesting.FatSignalKind.Discrete);
            var analog = launch.Project.Signals.Count(signal => signal.SignalKind == Models.IoTesting.FatSignalKind.Analog);
            var other = launch.Project.Signals.Count - digital - analog;
            var status =
                $"FAT v2 ready: {launch.Project.Signals.Count} static DataSet membership(s) from {launch.SourceFiles.Count} SCL source(s) — " +
                $"{digital} digital, {analog} analog, {other} other.";
            SetStatus(status);
            AddLog("INFO", "FAT v2", $"{status} Sources: {sourceNames}");

            var window = new FatVerificationWindow(launch) { Owner = this };
            _fatV2Window = window;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_fatV2Window, window))
                    _fatV2Window = null;
            };
            window.Show();
        }
        catch (OperationCanceledException)
        {
            SetStatus("FAT v2 SCL import cancelled.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            AddLog("ERROR", "FAT v2", ex.Message);
            MarkDiagnosticAlert();
            SetStatus("FAT v2 SCL import failed. The source SCL was not modified.");
            MessageBox.Show(
                this,
                $"ARSAS could not create FAT v2 from the selected SCL source set.\n\n{ex.Message}",
                "FAT v2 import failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
