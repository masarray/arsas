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
    private readonly IoListExcelImportService _ioListExcelImportService = new();
    private readonly IoTestLiveBindingService _ioTestLiveBindingService = new();
    private Button? _ioListTestingLauncher;
    private IoTestSessionController? _activeIoTestSessionController;
    private long _ioTestObservationSequence;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Dispatcher.BeginInvoke(new Action(InstallIoListTestingLauncher));
    }

    private void InstallIoListTestingLauncher()
    {
        if (_ioListTestingLauncher != null || Content is not Grid root)
            return;

        var header = root.Children
            .OfType<Grid>()
            .FirstOrDefault(child => Grid.GetRow(child) == 0);
        var actionPanel = header?.Children
            .OfType<WrapPanel>()
            .FirstOrDefault(panel => Grid.GetColumn(panel) == 2);
        if (actionPanel == null)
            return;

        var button = new Button
        {
            Style = TryFindResource("PrimaryButton") as Style,
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Import an ARSAS IO List workbook and enter the dedicated FAT workspace"
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new System.Windows.Shapes.Path
        {
            Data = TryFindResource("LucideFileInput") as Geometry,
            Style = TryFindResource("LucideIcon") as Style,
            Stroke = Brushes.White
        };
        content.Children.Add(new Viewbox
        {
            Width = 14,
            Height = 14,
            Margin = new Thickness(0, 0, 6, 0),
            Child = icon
        });
        content.Children.Add(new TextBlock
        {
            Text = "IO List Testing",
            FontWeight = FontWeights.SemiBold
        });
        button.Content = content;
        button.Click += OpenIoListTesting_Click;
        actionPanel.Children.Insert(0, button);
        _ioListTestingLauncher = button;
    }

    private async void OpenIoListTesting_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import ARSAS IO List FAT workbook",
            Filter = "ARSAS IO List workbook (*.xlsx)|*.xlsx|Excel workbook (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        SetStatus($"Importing IO List test plan from {Path.GetFileName(dialog.FileName)}…");
        try
        {
            var import = await _ioListExcelImportService.ImportAsync(
                dialog.FileName,
                _applicationCancellation.Token);
            var errors = import.AllFindings
                .Where(finding => finding.Severity == IoTestImportFindingSeverity.Error)
                .ToList();
            if (!import.IsValid)
            {
                var details = string.Join(
                    Environment.NewLine,
                    errors.Take(12).Select(finding => $"• {finding.Code}: {finding.Message}"));
                if (errors.Count > 12)
                    details += $"{Environment.NewLine}• …and {errors.Count - 12} more error(s).";

                SetStatus("IO List import was rejected. The source workbook was not guessed or partially executed.");
                MessageBox.Show(
                    this,
                    $"ARSAS could not import this IO List workbook safely.\n\n{details}",
                    "IO List import rejected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var binding = _ioTestLiveBindingService.Bind(import.Project, Devices);
            var warnings = import.AllFindings.Count(finding =>
                finding.Severity == IoTestImportFindingSeverity.Warning);
            SetStatus(
                $"IO List ready: {import.Project.Ieds.Count} IED, {import.Project.SignalCount} SDI, " +
                $"{binding.SignalBoundCount} matched to the loaded workspace, {warnings} warning(s).");

            var journalRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ARSAS",
                "IO Testing Evidence");
            using var controller = new IoTestSessionController(
                import.Project,
                ResolveIoTestDevice,
                action => Dispatcher.BeginInvoke(action, DispatcherPriority.Background),
                journalRoot);
            var window = new IoListTestingWindow(import.Project, controller) { Owner = this };
            _activeIoTestSessionController = controller;
            Interlocked.Exchange(ref _ioTestObservationSequence, DateTime.UtcNow.Ticks);
            _runtime.PointUpdated += Runtime_IoTestPointUpdated;
            Hide();
            try
            {
                window.ShowDialog();
            }
            finally
            {
                _runtime.PointUpdated -= Runtime_IoTestPointUpdated;
                _activeIoTestSessionController = null;
                Show();
                if (WindowState == System.Windows.WindowState.Minimized)
                    WindowState = System.Windows.WindowState.Normal;
                Activate();
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("IO List import cancelled.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            AddLog("ERROR", "IO Testing", ex.Message);
            MarkDiagnosticAlert();
            SetStatus("IO List import failed. Diagnostics is marked with !.");
            MessageBox.Show(this, ex.Message, "IO List import failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Runtime_IoTestPointUpdated(Iec61850PointSnapshot snapshot)
    {
        var point = snapshot.Point;
        _activeIoTestSessionController?.Enqueue(new Iec61850EventEntry
        {
            Sequence = Interlocked.Increment(ref _ioTestObservationSequence),
            DeviceId = point.DeviceId,
            PointKey = point.PointKey,
            DeviceTimestamp = snapshot.DeviceTimestamp,
            DeviceName = point.DeviceName,
            IpAddress = point.IpAddress,
            SignalName = point.SignalName,
            IecReference = point.IecReference,
            OldValue = snapshot.PreviousValue,
            NewValue = snapshot.Value,
            Quality = snapshot.Quality,
            SourceMode = snapshot.SourceMode,
            Reason = snapshot.Reason
        });
    }

    private Iec61850MonitorDevice? ResolveIoTestDevice(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;
        return Devices.FirstOrDefault(device =>
            device.DeviceId.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            device.Name.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            device.SclIedName.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            device.IpAddress.Equals(key, StringComparison.OrdinalIgnoreCase));
    }
}
