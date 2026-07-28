using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester;

public partial class IoListTestingWindow : Window, INotifyPropertyChanged
{
    private IoTestIedPlan? _selectedIed;

    public IoListTestingWindow()
        : this(CreateEmptyProject())
    {
    }

    public IoListTestingWindow(IoTestProject project)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        Project.InitializeRuntimeNotifications();
        InitializeComponent();
        DataContext = this;
        SelectedIed = Project.Ieds.FirstOrDefault();
    }

    public IoTestProject Project { get; }

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

    public string WorkspaceStatus => Project.LiveBoundSignalCount > 0
        ? $"{Project.LiveBoundSignalCount:N0} signal(s) already match the loaded ARSAS workspace. FAT execution remains locked until the session controller and evidence journal are enabled."
        : "The IO test plan is imported. Load or discover an IED in the engineering workspace before starting live FAT execution.";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void ReturnToEngineering_Click(object sender, RoutedEventArgs e)
        => Close();

    private void Raise([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static IoTestProject CreateEmptyProject() => new()
    {
        ProjectId = "EMPTY",
        SchemaVersion = "ARSAS-FAT-IO-1.0",
        ProjectName = "No IO List project loaded"
    };
}
