using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Read-only A2.1 adapter. This partial adds no command behavior and does not alter
/// SendCommand_Click, ExecuteControlAsync, SBOw, Operate or CommandTermination flow.
/// </summary>
public partial class ControlCommandWindow
{
    internal SignalDefinition A21WitnessSignal => _signal;
    internal Iec61850MonitorDevice A21WitnessDevice => _device;
}
