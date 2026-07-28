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
        }
    }

    public string ProjectSummary =>
        $"{Project.Ieds.Count} IED · {Project.SignalCount} SDI · {Project.ReadySignalCount} ready";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void ReturnToEngineering_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Raise([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static IoTestProject CreateEmptyProject()
    {
        return new IoTestProject
        {
            ProjectId = "EMPTY",
            SchemaVersion = "ARSAS-FAT-IO-1.0",
            ProjectName = "No IO List project loaded"
        };
    }
}
