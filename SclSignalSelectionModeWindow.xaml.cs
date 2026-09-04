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
        if (Owner is MainWindow mainWindow)
            mainWindow.OpenSelectedSclRcbEngineering();
    }

    private void DownloadComtrade_Click(object sender, RoutedEventArgs e)
    {
        if (Owner is MainWindow mainWindow)
            mainWindow.OpenSelectedSclComtradeDownload();
    }

    private void BrowseOffline_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
