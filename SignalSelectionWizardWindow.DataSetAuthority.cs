using System.Windows;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class SignalSelectionWizardWindow
{
    protected override void OnInitialized(EventArgs e)
    {
        RestoreAuthoritativeDataSetInventory();
        base.OnInitialized(e);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        // The legacy selector constructor prepares display text before WPF finishes
        // initialization. Run authority restoration again after construction so no
        // runtime primary leaf can survive as a replacement for a static FCDA/FCD
        // identity. The merge is idempotent and does not alter user selection.
        RestoreAuthoritativeDataSetInventory();
        SignalsView.Refresh();
        RefreshViewState();
        base.OnContentRendered(e);
    }

    private void RestoreAuthoritativeDataSetInventory()
    {
        var merge = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(_device);
        foreach (var signal in merge.AddedSignals)
        {
            // DisplayReference is the engine-authoritative static FCDA/FCD identity.
            // ObjectReference may point to a resolved runtime leaf such as .stVal.
            if (string.IsNullOrWhiteSpace(signal.DisplayReference))
            {
                signal.DisplayReference = Iec61850MonitorPoint.StripIedNamePrefix(
                    signal.ObjectReference,
                    _device.Name);
            }

            signal.PropertyChanged -= Signal_PropertyChanged;
            signal.PropertyChanged += Signal_PropertyChanged;
        }

        if (Application.Current?.MainWindow is MainWindow mainWindow)
            mainWindow.RegisterRecoveredDataSetSignals(_device, merge);
        else
        {
            _device.RecountSelectedSignals();
            _device.RefreshComputed();
        }
    }
}
