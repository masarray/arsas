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
        }
    }

    public string ProjectSummary =>
        $"{Project.Ieds.Count} IED · {Project.SignalCount} SDI · {Project.ReadySignalCount} ready · {Project.LiveBoundSignalCount} live-bound";

    public string SelectedIedSummary => SelectedIed == null
        ? "Select an imported IED"
        : $"{SelectedIed.IpAddress} · {SelectedIed.BoundCount}/{SelectedIed.TestPoints.Count} bound · {SelectedIed.LiveStatusText}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void StartSession_Click(object sender, RoutedEventArgs e)
    {
        var preflight = IoTestSessionPreflight.Validate(SelectedIed);
        if (!preflight.Succeeded)
        {
            ShowActionResult(preflight, "FAT session scope is not ready");
            return;
        }

        var result = Session.Start(SelectedIed);
        ShowActionResult(result, "FAT session could not start");
        if (result.Succeeded)
            Storage?.ScheduleSave();
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
                $"Portable ARSAS project created successfully.\n\n{exportedPath}\n\nOpen this .arsas file on another laptop to continue the remaining FAT scope. The package also contains report/IO-FAT-Report.pdf for direct review and printing.",
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
