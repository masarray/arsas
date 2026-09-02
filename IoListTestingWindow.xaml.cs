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
    private string _preparationStatusText = string.Empty;
    private bool _isAddingFatIeds;
    private bool _parallelSessionsAttached;

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
        Session = new IoTestMultiSessionCoordinator(
            Project,
            session ?? throw new ArgumentNullException(nameof(session)));
        Storage = persistence;
        Project.InitializeRuntimeNotifications();
        Session.PropertyChanged += Session_PropertyChanged;
        InitializeComponent();
        DataContext = this;
        SelectedIed = Project.Ieds.FirstOrDefault();
        Loaded += Window_LoadedForParallelEvidenceSessions;
    }

    public IoTestProject Project { get; }
    public IoTestMultiSessionCoordinator Session { get; }
    public IoTestWorkspacePersistence? Storage { get; }

    public IoTestIedPlan? SelectedIed
    {
        get => _selectedIed;
        set
        {
            if (ReferenceEquals(_selectedIed, value))
                return;
            _selectedIed = value;
            Session.SelectContext(_selectedIed);
            Raise();
            Raise(nameof(SelectedIedSummary));
            RaisePreparationProperties();
        }
    }

    // Compatibility aggregate used for close protection. It no longer blocks another
    // IED from starting its own independent connection or evidence workflow.
    public bool IsPreparingIed => Project.Ieds.Any(ied => ied.IsPreparing);

    public string PreparationStatusText
    {
        get => _preparationStatusText;
        private set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (_preparationStatusText == normalized)
                return;
            _preparationStatusText = normalized;
            Raise();
            Raise(nameof(FooterStatusText));
        }
    }

    public Visibility PreparationVisibility => SelectedIed?.IsPreparing == true ? Visibility.Visible : Visibility.Collapsed;
    public string PreparationIedText => SelectedIed?.IsPreparing == true ? $"Preparing {SelectedIed.IedName}" : string.Empty;

    public bool CanStartWorkflow =>
        SelectedIed != null && !SelectedIed.IsPreparing && Session.CanStart;

    // Explorer navigation stays available while one or more IEDs are connecting or FAT
    // evidence sessions are running. Each IED owns its own connection and evidence state.
    public bool CanSelectIed => true;

    // P2 edit lock is selected-IED-local: an IED whose evidence session is active is
    // immutable, while another selected IED remains editable even when its sibling is live.
    public bool CanEditPlan =>
        SelectedIed?.IsPreparing != true && Session.CanEditPlan;

    // Adding another SCL is a workspace-level append operation, independent of per-IED
    // connection preparation and P2 evidence sessions. Only another append locks it.
    public bool CanAddFatIed => !_isAddingFatIeds;

    private void SetAddingFatIeds(bool value)
    {
        if (_isAddingFatIeds == value)
            return;
        _isAddingFatIeds = value;
        Raise(nameof(CanAddFatIed));
    }

    public string StartWorkflowText =>
        SelectedIed?.IsPreparing == true ? $"Connecting {SelectedIed.IedName}…" : "Connect & Start IED";

    public string ProjectSummary =>
        $"{Project.Ieds.Count} IED · {Project.SignalCount} points · {Project.LiveBoundSignalCount} live";

    public string SelectedIedSummary => SelectedIed == null
        ? "Select an imported IED"
        : $"{SelectedIed.IpAddress} · {SelectedIed.EnabledCount} test points · {SelectedIed.LiveStatusText}";

    public string FooterStatusText => SelectedIed?.IsPreparing == true
        ? SelectedIed.PreparationStatusText
        : Session.StatusText;

    public event PropertyChangedEventHandler? PropertyChanged;

    private async void StartSession_Click(object sender, RoutedEventArgs e)
    {
        var selectedIed = SelectedIed;
        if (selectedIed?.IsPreparing == true)
            return;

        var preflight = IoTestSessionPreflight.Validate(selectedIed);
        if (!preflight.Succeeded)
        {
            ShowActionResult(preflight, "FAT session scope is not ready");
            return;
        }

        PreparationStatusText = $"Connecting {selectedIed!.IedName} · {selectedIed.IpAddress}:102";
        RaisePreparationProperties();
        try
        {
            if (Owner is MainWindow engineeringWindow)
            {
                var progress = new Progress<string>(message =>
                {
                    PreparationStatusText = message;
                    RaiseStatusProperties();
                    RaisePreparationProperties();
                });
                var preparation = await engineeringWindow.PrepareIoTestIedForFatAsync(
                    Project,
                    selectedIed,
                    progress);
                RaiseStatusProperties();
                if (!preparation.Succeeded)
                {
                    PreparationStatusText = preparation.Message;
                    ShowActionResult(preparation, "IED acquisition could not start");
                    return;
                }
            }

            var result = Session.Start(selectedIed);
            ShowActionResult(result, "FAT evidence session could not start");
            RaiseStatusProperties();
            if (result.Succeeded)
            {
                PreparationStatusText = $"{selectedIed.IedName} live · waiting for OFF → ON → OFF";
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
            selectedIed.SetPreparationState(false, selectedIed.LiveStatusText);
            RaisePreparationProperties();
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
                Session.PrimaryController,
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
            $"Stop all active IED evidence sessions before exporting the {outputName}. Every per-IED evidence journal must be sealed and verified first.",
            "Stop sessions before export",
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
            var activeNames = string.Join(", ", Project.Ieds.Where(ied => ied.IsPreparing).Select(ied => ied.IedName));
            MessageBox.Show(
                this,
                $"ARSAS is still preparing {activeNames}. You can inspect or connect other IEDs while these independent workflows run, but finish preparation before closing this workspace.",
                "IED preparation in progress",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            e.Cancel = true;
            return;
        }

        if (Session.IsSessionActive)
        {
            var activeCount = Session.ActiveSessionCount;
            var answer = MessageBox.Show(
                this,
                $"{activeCount} FAT evidence session(s) are active. Returning to Engineering will stop every active IED session, seal each independent evidence journal, and save the current project progress.\n\nStop all sessions and return?",
                "Stop active FAT sessions",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            var stopAll = Session.StopAll("Workspace closed by operator; per-IED evidence journal sealed.");
            if (!stopAll.Succeeded)
            {
                MessageBox.Show(this, stopAll.Message, "Evidence journals could not be sealed", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Cancel = true;
                return;
            }
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

    private void Window_LoadedForParallelEvidenceSessions(object sender, RoutedEventArgs e)
    {
        if (_parallelSessionsAttached || Owner is not MainWindow engineeringWindow)
            return;

        var evidenceRoot = Storage?.EvidenceProjectDirectory;
        if (string.IsNullOrWhiteSpace(evidenceRoot))
        {
            evidenceRoot = Path.Combine(
                Path.GetTempPath(),
                "ARSAS",
                "IO Testing Evidence",
                string.IsNullOrWhiteSpace(Project.ProjectId) ? "UNNAMED" : Project.ProjectId);
        }

        engineeringWindow.AttachIoTestParallelEvidenceSessions(Session, evidenceRoot);
        _parallelSessionsAttached = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        Loaded -= Window_LoadedForParallelEvidenceSessions;
        Session.PropertyChanged -= Session_PropertyChanged;
        if (_parallelSessionsAttached && Owner is MainWindow engineeringWindow)
        {
            engineeringWindow.DetachIoTestParallelEvidenceSessions(Session);
            _parallelSessionsAttached = false;
        }
        Session.Dispose();
        base.OnClosed(e);
    }

    private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        => RaiseStatusProperties();

    private void SetPreparingIed(IoTestIedPlan? ied, string status)
    {
        // Retained as a lightweight UI refresh hook for older call paths. Preparation
        // ownership now lives on IoTestIedPlan, so parallel IEDs never share one lock.
        PreparationStatusText = status;
        RaisePreparationProperties();
    }

    private void RaisePreparationProperties()
    {
        Raise(nameof(IsPreparingIed));
        Raise(nameof(PreparationVisibility));
        Raise(nameof(PreparationIedText));
        Raise(nameof(CanStartWorkflow));
        Raise(nameof(CanSelectIed));
        Raise(nameof(CanEditPlan));
        Raise(nameof(CanAddFatIed));
        Raise(nameof(StartWorkflowText));
        Raise(nameof(FooterStatusText));
    }

    private void RaiseStatusProperties()
    {
        RaisePreparationProperties();
        Raise(nameof(ProjectSummary));
        Raise(nameof(SelectedIedSummary));
        Raise(nameof(FooterStatusText));
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
