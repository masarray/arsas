using System.Windows;
using System.Windows.Controls;

namespace ArIED61850Tester;

public enum SclWorkspaceAction
{
    BrowseOffline,
    MonitorStaticDataSet,
    MonitorSelectedSignals,
    RcbEngineering,
    DownloadComtrade
}

public partial class SclWorkspaceQuickStartWindow : Window
{
    public SclWorkspaceQuickStartWindow(
        int iedCount,
        string selectedIedName,
        string endpointText,
        bool canMonitor,
        bool canDownloadComtrade)
    {
        InitializeComponent();
        IedCount = Math.Max(1, iedCount);
        SelectedIedName = string.IsNullOrWhiteSpace(selectedIedName) ? "Selected IED" : selectedIedName.Trim();
        EndpointText = string.IsNullOrWhiteSpace(endpointText) ? "No MMS endpoint in SCL" : endpointText.Trim();
        CanMonitor = canMonitor;
        CanDownloadComtrade = canDownloadComtrade;
        DataContext = this;
    }

    public SclWorkspaceAction? SelectedAction { get; private set; }
    public int IedCount { get; }
    public string SelectedIedName { get; }
    public string EndpointText { get; }
    public bool CanMonitor { get; }
    public bool CanDownloadComtrade { get; }

    public string ScopeText => IedCount == 1
        ? $"{SelectedIedName} · {EndpointText}"
        : $"{IedCount} IED workspaces · selected: {SelectedIedName} · {EndpointText}";

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } ||
            !Enum.TryParse<SclWorkspaceAction>(tag, ignoreCase: false, out var action))
        {
            return;
        }

        SelectedAction = action;
        DialogResult = true;
    }

    private void KeepOffline_Click(object sender, RoutedEventArgs e)
    {
        SelectedAction = SclWorkspaceAction.BrowseOffline;
        DialogResult = true;
    }
}
