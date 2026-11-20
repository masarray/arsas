namespace ArIED61850Tester.Models;

/// <summary>
/// Operator task chosen after SCL import or IED discovery. Browse IED is
/// intentionally not an acquisition mode: it cannot silently arm an RCB,
/// mutate signal selections or turn on polling. None covers close/cancel
/// and the separately handled RCB/COMTRADE engineering actions.
/// </summary>
public enum IedWorkspaceTaskChoice
{
    None = 0,
    StaticDataSet = 1,
    BrowseIed = 2,
    ManualSignals = 3
}
