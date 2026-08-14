using System.Windows;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class SignalSelectionWizardWindow
{
    protected override void OnInitialized(EventArgs e)
    {
        var merge = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(_device);
        foreach (var signal in merge.AddedSignals)
        {
            signal.DisplayReference = Iec61850MonitorPoint.StripIedNamePrefix(signal.ObjectReference, _device.Name);
            signal.PropertyChanged += Signal_PropertyChanged;
        }

        // Window initialization can run inside InitializeComponent(), before the caller's
        // object initializer assigns Owner. Register recovered rows through the actual
        // application MainWindow so they receive the same owner/property-change lifecycle
        // as rows produced by the normal discovery pipeline.
        if (Application.Current?.MainWindow is MainWindow mainWindow)
            mainWindow.RegisterRecoveredDataSetSignals(_device, merge);
        else
        {
            _device.RecountSelectedSignals();
            _device.RefreshComputed();
        }

        base.OnInitialized(e);
    }
}
