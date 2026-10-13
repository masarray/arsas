using ArIED61850Tester.Models;
using System.Windows;
using System.Windows.Controls;

namespace ArIED61850Tester;

public partial class MainWindow
{
    internal void OpenRcbExportFilter(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        IedEditRcb_Click(new Button { Tag = device }, new RoutedEventArgs());
    }
}
