using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class IntegratedFatWorkspaceControl : UserControl, INotifyPropertyChanged, IDisposable
{
    private readonly Action<Iec61850MonitorDevice?> _selectionChanged;
    private IoTestIedPlan? _selectedIed;
    private Iec61850MonitorDevice? _selectedEngineeringIed;
    private string _statusText = "Select an IED and start FAT when its Engineering monitor is live.";
    private bool _disposed;

    public IntegratedFatWorkspaceControl(
        IoTestWorkspaceLaunchResult launch,
        IEnumerable<Iec61850MonitorDevice> engineeringIeds,
        Action<Iec61850MonitorDevice?> selectionChanged)
    {
        InitializeComponent();
        Project = launch.Project;
        Session = launch.Session;
        Storage = launch.Workspace;
        EngineeringIeds = engineeringIeds;
        _selectionChanged = selectionChanged;
        Session.PropertyChanged += Session_PropertyChanged;
        DataContext = this;
        SelectedEngineeringIed = EngineeringIeds.FirstOrDefault();
        SelectedIed ??= Project.Ieds.FirstOrDefault();
    }

    public IoTestProject Project { get; }
    public IoTestSessionController Session { get; }
    public IoTestWorkspacePersistence Storage { get; }
    public IEnumerable<Iec61850MonitorDevice> EngineeringIeds { get; }

    public Iec61850MonitorDevice? SelectedEngineeringIed
    {
        get => _selectedEngineeringIed;
        set
        {
            if (ReferenceEquals(_selectedEngineeringIed, value)) return;
            _selectedEngineeringIed = value;
            Raise(nameof(SelectedEngineeringIed));
            SelectedIed = ResolveFatIed(value);
            _selectionChanged(value);
        }
    }

    public IoTestIedPlan? SelectedIed
    {
        get => _selectedIed;
        set
        {
            if (ReferenceEquals(_selectedIed, value)) return;
            _selectedIed = value;
            Raise(nameof(SelectedIed));
            ApplyPointScope();
            if (PreviewView.Visibility == Visibility.Visible) RefreshPreview();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value) return;
            _statusText = value;
            Raise(nameof(StatusText));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SelectEngineeringIed(string? deviceId, string? name, string? ipAddress)
    {
        if (!Session.CanSelectIed)
            return;

        var match = Project.Ieds.FirstOrDefault(ied =>
                        !string.IsNullOrWhiteSpace(deviceId) &&
                        ied.LiveDeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
                    ?? Project.Ieds.FirstOrDefault(ied =>
                        ied.IedName.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrWhiteSpace(ipAddress) ||
                         ied.IpAddress.Equals(ipAddress, StringComparison.OrdinalIgnoreCase)));
        var engineeringMatch = EngineeringIeds.FirstOrDefault(ied =>
                                   !string.IsNullOrWhiteSpace(deviceId) &&
                                   ied.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
                               ?? EngineeringIeds.FirstOrDefault(ied =>
                                   ied.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                                   (string.IsNullOrWhiteSpace(ipAddress) ||
                                    ied.IpAddress.Equals(ipAddress, StringComparison.OrdinalIgnoreCase)));
        if (engineeringMatch != null)
            SelectedEngineeringIed = engineeringMatch;
        else if (match != null)
            SelectedIed = match;
    }

    private IoTestIedPlan? ResolveFatIed(Iec61850MonitorDevice? device)
        => device == null
            ? null
            : Project.Ieds.FirstOrDefault(ied =>
                  ied.LiveDeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase))
              ?? Project.Ieds.FirstOrDefault(ied =>
                  ied.IedName.Equals(device.Name, StringComparison.OrdinalIgnoreCase) &&
                  ied.IpAddress.Equals(device.IpAddress, StringComparison.OrdinalIgnoreCase));

    private void ApplyPointScope()
    {
        if (SelectedIed == null) return;
        var view = CollectionViewSource.GetDefaultView(SelectedIed.TestPoints);
        view.Filter = item => item is IoTestPointPlan point &&
                              point.WorkspaceSelected && point.IsIncludedInFat;
        view.Refresh();
    }

    private void StartFat_Click(object sender, RoutedEventArgs e)
        => ShowResult(Session.Start(SelectedIed));

    private void StopFat_Click(object sender, RoutedEventArgs e)
        => ShowResult(Session.Stop("Stopped from integrated FAT tab."));

    private void CaptureValue1_Click(object sender, RoutedEventArgs e)
        => CaptureSelected(FatValueSlot.Value1);

    private void CaptureValue2_Click(object sender, RoutedEventArgs e)
        => CaptureSelected(FatValueSlot.Value2);

    private void CaptureSelected(FatValueSlot slot)
    {
        var points = FatSignalsGrid.SelectedItems.OfType<IoTestPointPlan>().ToArray();
        if (points.Length == 0 && FatSignalsGrid.SelectedItem is IoTestPointPlan current)
            points = new[] { current };
        if (points.Length == 0)
        {
            StatusText = "Select at least one FAT row first.";
            return;
        }

        var failures = new List<string>();
        foreach (var point in points)
        {
            var result = Session.CaptureOperatorSnapshot(point, slot);
            if (!result.Succeeded)
                failures.Add($"{point.DisplaySignalName}: {result.Message}");
        }
        Storage.ScheduleSave();
        StatusText = failures.Count == 0
            ? $"{slot}: captured for {points.Length} selected signal(s)."
            : string.Join(" • ", failures.Take(3));
    }

    private void ShowResult(IoTestSessionActionResult result)
    {
        StatusText = result.Message;
        if (result.Succeeded)
            Storage.ScheduleSave();
    }

    private void OpenPreview_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedIed == null)
        {
            StatusText = "Select an IED before opening Print Preview.";
            return;
        }
        WorkspaceView.Visibility = Visibility.Collapsed;
        PreviewView.Visibility = Visibility.Visible;
        RefreshPreview();
    }

    private void ClosePreview_Click(object sender, RoutedEventArgs e)
    {
        PreviewView.Visibility = Visibility.Collapsed;
        WorkspaceView.Visibility = Visibility.Visible;
    }

    private void RefreshPreview_Click(object sender, RoutedEventArgs e)
        => RefreshPreview();

    private void RefreshPreview()
    {
        if (SelectedIed == null)
            return;
        try
        {
            var scoped = IoFatReportPreviewService.CreateIedScopedProject(Project, SelectedIed);
            var draft = Session.IsSessionActive && ReferenceEquals(Session.ActiveIed, SelectedIed);
            PreviewDocument.Document = IoFatReportPreviewDocumentBuilder.Build(scoped, draft);
            PreviewDocument.UpdateLayout();
            Dispatcher.BeginInvoke(new Action(PreviewDocument.FitToWidth), DispatcherPriority.Loaded);
        }
        catch (Exception ex)
        {
            StatusText = $"Print Preview could not be rendered: {ex.Message}";
        }
    }

    private void PrintPreview_Click(object sender, RoutedEventArgs e)
    {
        if (PreviewDocument.Document != null &&
            ApplicationCommands.Print.CanExecute(null, PreviewDocument))
        {
            ApplicationCommands.Print.Execute(null, PreviewDocument);
        }
    }

    private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Raise(nameof(Session));
        if (PreviewView.Visibility == Visibility.Visible &&
            e.PropertyName is nameof(IoTestSessionController.State) or
                nameof(IoTestSessionController.EvidenceRecordCount))
        {
            Dispatcher.BeginInvoke(new Action(RefreshPreview), DispatcherPriority.Background);
        }
    }

    private void Raise(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Session.PropertyChanged -= Session_PropertyChanged;
        Storage.ScheduleSave();
    }
}
