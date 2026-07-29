using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;
using Microsoft.Win32;

namespace ArIED61850Tester;

public partial class IoListTestingWindow : Window, INotifyPropertyChanged
{
    private IoTestIedPlan? _selectedIed;
    private bool _isPreparingIed;
    private string _preparationStatusText = "Workbook-driven connection is ready.";

    public IoListTestingWindow()
        : this(CreateEmptyProject(), CreateEmptyController(), null)
    {
    }

    public IoListTestingWindow(
        IoTestProject project,
        IoTestSessionController session,
        IoTestWorkspacePersistence? persistence)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Storage = persistence;
        Project.InitializeRuntimeNotifications();
        Session.PropertyChanged += Session_PropertyChanged;
        InitializeComponent();
        DataContext = this;
        SelectedIed = Project.Ieds.FirstOrDefault();
    }

    public IoTestProject Project { get; }
    public IoTestSessionController Session { get; }
    public IoTestWorkspacePersistence? Storage { get; }

    public IoTestIedPlan? SelectedIed
    {
        get => _selectedIed;
        set
        {
            if (ReferenceEquals(_selectedIed, value))
                return;
            _selectedIed = value;
            Raise();
            Raise(nameof(SelectedIedSummary));
            Raise(nameof(CanStartWorkflow));
        }
    }

    public bool IsPreparingIed
    {
        get => _isPreparingIed;
        private set
        {
            if (_isPreparingIed == value)
                return;
            _isPreparingIed = value;
            Raise();
            Raise(nameof(CanStartWorkflow));
            Raise(nameof(CanSelectIed));
            Raise(nameof(CanEditPlan));
            Raise(nameof(StartWorkflowText));
        }
    }

    public string PreparationStatusText
    {
        get => _preparationStatusText;
        private set
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? "Workbook-driven connection is ready."
                : value.Trim();
            if (_preparationStatusText == normalized)
                return;
            _preparationStatusText = normalized;
            Raise();
        }
    }

    public bool CanStartWorkflow =>
        SelectedIed != null && !IsPreparingIed && Session.CanStart;

    public bool CanSelectIed =>
        !IsPreparingIed && Session.CanSelectIed;

    public bool CanEditPlan =>
        !IsPreparingIed && Session.CanEditPlan;

    public string StartWorkflowText =>
        IsPreparingIed ? "Connecting IED…" : "Connect & Start IED";

    public string ProjectSummary =>
        $"{Project.Ieds.Count} IED · {Project.SignalCount} SDI · {Project.ReadySignalCount} ready · {Project.LiveBoundSignalCount} live-bound";

    public string SelectedIedSummary => SelectedIed == null
        ? "Select an imported IED"
        : $"{SelectedIed.IpAddress} · {SelectedIed.BoundCount}/{SelectedIed.TestPoints.Count} bound · {SelectedIed.LiveStatusText}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void StartSession_Click(object sender, RoutedEventArgs e)
    {
        if (IsPreparingIed)
            return;

        var selectedIed = SelectedIed;
        var preflight = IoTestSessionPreflight.Validate(selectedIed);
        if (!preflight.Succeeded)
        {
            ShowActionResult(preflight, "FAT session scope is not ready");
            return;
        }

        IsPreparingIed = true;
        PreparationStatusText = $"Preparing {selectedIed!.IedName} from workbook endpoint {selectedIed.IpAddress}:102…";
        try
        {
            if (Owner is MainWindow engineeringWindow)
            {
                var progress = new Progress<string>(message =>
                {
                    PreparationStatusText = message;
                    Raise(nameof(ProjectSummary));
                    Raise(nameof(SelectedIedSummary));
                });
                var preparation = await engineeringWindow.PrepareIoTestIedForFatAsync(
                    Project,
                    selectedIed,
                    progress);
                Raise(nameof(ProjectSummary));
                Raise(nameof(SelectedIedSummary));
                if (!preparation.Succeeded)
                {
                    PreparationStatusText = preparation.Message;
                    ShowActionResult(preparation, "IED connection and monitoring could not start");
                    return;
                }
            }

            var result = Session.Start(selectedIed);
            ShowActionResult(result, "FAT evidence session could not start");
            Raise(nameof(ProjectSummary));
            Raise(nameof(SelectedIedSummary));
            if (result.Succeeded)
            {
                PreparationStatusText =
                    $"{selectedIed.IedName} is connected and monitoring. Evidence capture is waiting for OFF → ON → OFF.";
                Storage?.ScheduleSave();
            }
            else
            {
                PreparationStatusText = result.Message;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            PreparationStatusText = ex.Message;
            MessageBox.Show(
                this,
                ex.Message,
                "Connect and start IED failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsPreparingIed = false;
        }
    }

    private void PauseSession_Click(object sender, RoutedEventArgs e)
    {
        var result = Session.Pause();
        ShowActionResult(result, "FAT session could not pause");
        if (result.Succeeded)
            Storage?.SaveNow();
    }

    private void ResumeSession_Click(object sender, RoutedEventArgs e)
    {
        var result = Session.Resume();
        ShowActionResult(result, "FAT session could not resume");
        if (result.Succeeded)
            Storage?.ScheduleSave();
    }

    private void StopSession_Click(object sender, RoutedEventArgs e)
    {
        var result = Session.Stop();
        ShowActionResult(result, "FAT session could not stop");
        if (result.Succeeded)
            Storage?.SaveNow();
    }

    private void SaveProgress_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Storage?.SaveNow();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, "Progress save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportExcel_Click(object sender, RoutedEventArgs e)
    {
        if (Storage == null)
            return;
        if (!EnsureSessionSealedForExport("Excel evidence workbook"))
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Export ARSAS IO FAT result workbook",
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            FileName = $"{SafeFileName(Project.ProjectId)}_IO-FAT-Results_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
            AddExtension = true,
            DefaultExt = ".xlsx",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            IsEnabled = false;
            Storage.SaveNow();
            await IoFatExcelResultExportService.ExportAsync(
                Storage.SourceWorkbookPath,
                dialog.FileName,
                Project);
            MessageBox.Show(
                this,
                $"FAT result workbook created successfully.\n\n{dialog.FileName}\n\nThe approved source workbook was not modified.",
                "Excel evidence exported",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, "Excel evidence export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSessionSealedForExport("PDF evidence report"))
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Export native ARSAS IO FAT PDF report",
            Filter = "PDF evidence report (*.pdf)|*.pdf",
            FileName = $"{SafeFileName(Project.ProjectId)}_IO-FAT_{DateTime.Now:yyyyMMdd_HHmm}.pdf",
            AddExtension = true,
            DefaultExt = ".pdf",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            IsEnabled = false;
            Storage?.SaveNow();
            IoFatPdfReportService.Save(dialog.FileName, Project);
            MessageBox.Show(
                this,
                $"Native PDF evidence report created successfully.\n\n{dialog.FileName}",
                "PDF report exported",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, "PDF report export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async void ExportHandover_Click(object sender, RoutedEventArgs e)
    {
        if (Storage == null)
            return;
        if (!EnsureSessionSealedForExport("ARSAS project"))
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Export portable ARSAS IO FAT project",
            Filter = $"ARSAS project (*{IoFatProjectPackageService.PackageExtension})|*{IoFatProjectPackageService.PackageExtension}",
            FileName = $"{SafeFileName(Project.ProjectId)}_{DateTime.Now:yyyyMMdd_HHmm}{IoFatProjectPackageService.PackageExtension}",
            AddExtension = true,
            DefaultExt = IoFatProjectPackageService.PackageExtension,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            IsEnabled = false;
            var exportedPath = await IoFatProjectPackageService.ExportAsync(
                Storage,
                Session,
                dialog.FileName);
            MessageBox.Show(
                this,
                $"Portable ARSAS project created successfully.\n\n{exportedPath}\n\nOpen this .arsas file on another laptop to continue the remaining FAT scope. The package also contains the native PDF report and the completed Excel result workbook.",
                "ARSAS project exported",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, "ARSAS project export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private bool EnsureSessionSealedForExport(string outputName)
    {
        if (!Session.IsSessionActive)
            return true;

        MessageBox.Show(
            this,
            $"Stop the active IED session before exporting the {outputName}. This seals and verifies the current evidence journal first.",
            "Stop session before export",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

    private void ReturnToEngineering_Click(object sender, RoutedEventArgs e)
        => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (IsPreparingIed)
        {
            MessageBox.Show(
                this,
                "ARSAS is still connecting, discovering, or preparing live monitoring for the selected IED. Wait for this operation to finish before returning to Engineering.",
                "IED preparation in progress",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            e.Cancel = true;
            return;
        }

        if (Session.IsSessionActive)
        {
            var answer = MessageBox.Show(
                this,
                "A FAT session is active. Returning to Engineering will stop the session, seal the evidence journal, and save the current project progress.\n\nStop the session and return?",
                "Stop active FAT session",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            Session.Stop("Workspace closed by operator; evidence journal sealed.");
        }

        try
        {
            Storage?.SaveNow();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var answer = MessageBox.Show(
                this,
                $"ARSAS could not save the latest IO FAT progress.\n\n{ex.Message}\n\nClose the workspace anyway?",
                "Progress save failed",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                e.Cancel = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Session.PropertyChanged -= Session_PropertyChanged;
        base.OnClosed(e);
    }

    private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Raise(nameof(CanStartWorkflow));
        Raise(nameof(CanSelectIed));
        Raise(nameof(CanEditPlan));
        Raise(nameof(ProjectSummary));
        Raise(nameof(SelectedIedSummary));
    }

    private void ShowActionResult(IoTestSessionActionResult result, string title)
    {
        if (result.Succeeded)
            return;
        MessageBox.Show(this, result.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = new string((value ?? "IO-FAT").Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return result.Length == 0 ? "IO-FAT" : result;
    }

    private void Raise([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static IoTestProject CreateEmptyProject() => new()
    {
        ProjectId = "EMPTY",
        SchemaVersion = "ARSAS-FAT-IO-1.0",
        ProjectName = "No IO List project loaded"
    };

    private static IoTestSessionController CreateEmptyController()
    {
        var project = CreateEmptyProject();
        return new IoTestSessionController(
            project,
            _ => null,
            action => action(),
            Path.Combine(Path.GetTempPath(), "ARSAS", "IO Testing Preview"));
    }
}
