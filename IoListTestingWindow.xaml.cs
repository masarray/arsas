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
    private IoTestIedPlan? _preparingIed;
    private string _preparationStatusText = string.Empty;

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

    public bool IsPreparingIed => _preparingIed != null;

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

    public Visibility PreparationVisibility => IsPreparingIed ? Visibility.Visible : Visibility.Collapsed;
    public string PreparationIedText => _preparingIed == null ? string.Empty : $"Preparing {_preparingIed.IedName}";

    public bool CanStartWorkflow =>
        SelectedIed != null && !IsPreparingIed && Session.CanStart;

    // Explorer navigation stays available while one IED is connecting or another FAT
    // session is running. This is inspection-only; the active evidence scope remains
    // pinned to Session.ActiveIed.
    public bool CanSelectIed => true;

    public bool CanEditPlan =>
        !IsPreparingIed && Session.CanEditPlan;

    public string StartWorkflowText =>
        IsPreparingIed ? $"Connecting {_preparingIed!.IedName}…" : "Connect & Start IED";

    public string ProjectSummary =>
        $"{Project.Ieds.Count} IED · {Project.SignalCount} points · {Project.LiveBoundSignalCount} live";

    public string SelectedIedSummary => SelectedIed == null
        ? "Select an imported IED"
        : $"{SelectedIed.IpAddress} · {SelectedIed.EnabledCount} test points · {SelectedIed.LiveStatusText}";

    public string FooterStatusText => IsPreparingIed
        ? PreparationStatusText
        : Session.StatusText;

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

        SetPreparingIed(selectedIed!, $"Connecting {selectedIed!.IedName} · {selectedIed.IpAddress}:102");
        try
        {
            if (Owner is MainWindow engineeringWindow)
            {
                var progress = new Progress<string>(message =>
                {
                    selectedIed.SetPreparationState(true, message);
                    PreparationStatusText = message;
                    RaiseStatusProperties();
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
            ShowFailureShout("Connect and start IED failed", ex.Message);
        }
        finally
        {
            selectedIed.SetPreparationState(false, selectedIed.LiveStatusText);
            SetPreparingIed(null, string.Empty);
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

        var blankForm = IoFatPdfReportService.IsBlankForm(Project);
        var dialog = new SaveFileDialog
        {
            Title = blankForm
                ? "Export ARSAS blank IFAT test form for customer review"
                : "Export native ARSAS IO FAT test record",
            Filter = blankForm
                ? "Blank IFAT test form (*.pdf)|*.pdf"
                : "PDF FAT test record (*.pdf)|*.pdf",
            FileName = blankForm
                ? $"{SafeFileName(Project.ProjectId)}_IFAT_Blank_Form_{DateTime.Now:yyyyMMdd_HHmm}.pdf"
                : $"{SafeFileName(Project.ProjectId)}_IO-FAT_As-Tested_{DateTime.Now:yyyyMMdd_HHmm}.pdf",
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
                blankForm
                    ? $"Blank IFAT test form created for customer review.\n\n{dialog.FileName}\n\nThe PDF declares the planned scope only; no test result is implied."
                    : $"Native PDF FAT test record created successfully.\n\n{dialog.FileName}",
                blankForm ? "Blank IFAT form exported" : "FAT test record exported",
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
                $"ARSAS is still preparing {_preparingIed!.IedName}. You can inspect other IEDs while it runs, but wait for acquisition setup to finish before closing this workspace.",
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
        => RaiseStatusProperties();

    private void SetPreparingIed(IoTestIedPlan? ied, string status)
    {
        _preparingIed = ied;
        PreparationStatusText = status;
        Raise(nameof(IsPreparingIed));
        Raise(nameof(PreparationVisibility));
        Raise(nameof(PreparationIedText));
        Raise(nameof(CanStartWorkflow));
        Raise(nameof(CanSelectIed));
        Raise(nameof(CanEditPlan));
        Raise(nameof(StartWorkflowText));
        Raise(nameof(FooterStatusText));
    }

    private void RaiseStatusProperties()
    {
        Raise(nameof(CanStartWorkflow));
        Raise(nameof(CanSelectIed));
        Raise(nameof(CanEditPlan));
        Raise(nameof(ProjectSummary));
        Raise(nameof(SelectedIedSummary));
        Raise(nameof(FooterStatusText));
    }

    private void ShowActionResult(IoTestSessionActionResult result, string title)
    {
        if (result.Succeeded)
            return;
        ShowFailureShout(title, result.Message);
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
