using System.Windows;

namespace ArIED61850Tester;

public partial class SclSignalSelectionModeWindow : Window
{
    private bool _useStaticDataSet;

    public SclSignalSelectionModeWindow(int iedCount)
    {
        InitializeComponent();
        ImportScopeText = iedCount == 1
            ? "1 IED WORKSPACE"
            : $"{iedCount} IED WORKSPACES";
        DataContext = this;
    }

    public string ImportScopeText { get; }

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

        // Close Quick Start before opening a second modal workflow. Returning false keeps
        // the imported SCL offline and prevents the caller from interpreting this as a
        // monitoring signal-selection decision.
        DialogResult = false;
        mainWindow?.OpenSelectedSclRcbEngineering();
    }

    private void DownloadComtrade_Click(object sender, RoutedEventArgs e)
    {
        var mainWindow = Owner as MainWindow;

        // File transfer is a separate task intent, not a monitoring selection. Close this
        // modal first so the fault-record window has one clear owner/focus chain.
        DialogResult = false;
        mainWindow?.OpenSelectedSclComtradeDownload();
    }

    private void BrowseOffline_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
