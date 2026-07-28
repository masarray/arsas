using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class IoListTestingWindow : Window, INotifyPropertyChanged
{
    private IoTestIedPlan? _selectedIed;

    public IoListTestingWindow()
        : this(CreateEmptyProject(), CreateEmptyController())
    {
    }

    public IoListTestingWindow(IoTestProject project, IoTestSessionController session)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Project.InitializeRuntimeNotifications();
        InitializeComponent();
        DataContext = this;
        SelectedIed = Project.Ieds.FirstOrDefault();
    }

    public IoTestProject Project { get; }
    public IoTestSessionController Session { get; }

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

        ShowActionResult(Session.Start(SelectedIed), "FAT session could not start");
    }

    private void PauseSession_Click(object sender, RoutedEventArgs e)
        => ShowActionResult(Session.Pause(), "FAT session could not pause");

    private void ResumeSession_Click(object sender, RoutedEventArgs e)
        => ShowActionResult(Session.Resume(), "FAT session could not resume");

    private void StopSession_Click(object sender, RoutedEventArgs e)
        => ShowActionResult(Session.Stop(), "FAT session could not stop");

    private void ReturnToEngineering_Click(object sender, RoutedEventArgs e)
        => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!Session.IsSessionActive)
            return;

        var answer = MessageBox.Show(
            this,
            "A FAT session is active. Returning to Engineering will stop the session and seal the current evidence journal.\n\nStop the session and return?",
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

    private void ShowActionResult(IoTestSessionActionResult result, string title)
    {
        if (result.Succeeded)
            return;
        MessageBox.Show(this, result.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
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
