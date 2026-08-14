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

        base.OnInitialized(e);
    }
}
