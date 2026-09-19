using System.Windows;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class SclSignalSelectionModeWindow : Window
{
    private readonly Iec61850MonitorDevice? _targetDevice;
    private bool _useStaticDataSet;

    public SclSignalSelectionModeWindow(int iedCount, Iec61850MonitorDevice? targetDevice = null)
    {
        _targetDevice = targetDevice;
        InitializeComponent();

        DialogTitle = targetDevice is null ? "SCL Quick Start" : $"IED Actions — {targetDevice.Name}";
        ContextHeading = targetDevice is null ? "Workspace opened offline" : $"IED actions — {targetDevice.Name}";
        ContextSubtitle = targetDevice is null
            ? "Choose a task. ARSAS only connects when that task needs the IED."
            : "Choose what to do with this IED. Closing this window changes nothing.";
        ImportScopeText = targetDevice is not null
            ? targetDevice.Name
            : iedCount == 1
                ? "1 IED WORKSPACE"
                : $"{iedCount} IED WORKSPACES";

        // Preserve the proven Open SCL prompt when it is opened before a concrete device
        // card is selected. For a discovered/card-scoped IED, capability comes from the same
        // canonical model used by Static DataSet selection.
        if (targetDevice is null)
        {
            CanUseStaticDataSet = true;
            StaticDataSetAvailabilityText = "Select + monitor";
        }
        else
        {
            var model = targetDevice.SclWorkspace?.DesignModel ?? targetDevice.LiveDiscoveryModel;
            var dataSetCount = model?.DataSets.Count ?? 0;
            var reportBackedCount =
                Iec61850StaticDataSetAuthoritySelection.BuildReportBackedDataSetReferences(targetDevice).Count;

            CanUseStaticDataSet = dataSetCount > 0;
            StaticDataSetAvailabilityText = dataSetCount == 0
                ? "No static DataSet"
                : reportBackedCount > 0
                    ? $"{dataSetCount} DataSet(s) • report ready"
                    : $"{dataSetCount} DataSet(s) • no configured RCB";
        }

        DataContext = this;
    }

    public string DialogTitle { get; }
    public string ContextHeading { get; }
    public string ContextSubtitle { get; }
    public string ImportScopeText { get; }
    public bool CanUseStaticDataSet { get; }
    public string StaticDataSetAvailabilityText { get; }

    public bool UseStaticDataSet => _useStaticDataSet;

    private void MonitorStaticDataSet_Click(object sender, RoutedEventArgs e)
    {
        _useStaticDataSet = true;
        DialogResult = true;
    }

    private void MonitorManual_Click(object sender, RoutedEventArgs e)
    {
        _useStaticDataSet = false;
        DialogResult = true;
    }

    private void RcbEngineering_Click(object sender, RoutedEventArgs e)
    {
        var mainWindow = Owner as MainWindow;

        // Close the action chooser before opening a second modal workflow. Returning
        // false means the caller must not reinterpret this engineering action as a
        // monitoring-selection decision.
        DialogResult = false;
        if (mainWindow is null)
            return;

        if (_targetDevice is not null)
            mainWindow.OpenSclRcbEngineering(_targetDevice);
        else
            mainWindow.OpenSelectedSclRcbEngineering();
    }

    private void DownloadComtrade_Click(object sender, RoutedEventArgs e)
    {
        var mainWindow = Owner as MainWindow;
        DialogResult = false;
        if (mainWindow is null)
            return;

        if (_targetDevice is not null)
            mainWindow.OpenSclComtradeDownload(_targetDevice);
        else
            mainWindow.OpenSelectedSclComtradeDownload();
    }

    private void BrowseOffline_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
