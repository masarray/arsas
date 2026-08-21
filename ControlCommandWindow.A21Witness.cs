using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Read-only A2.1 adapter. This partial exposes context only; it adds no command
/// behavior and leaves the existing IEC 61850 control transaction completely untouched.
/// </summary>
public partial class ControlCommandWindow
{
    internal SignalDefinition A21WitnessSignal => _signal;
    internal Iec61850MonitorDevice A21WitnessDevice => _device;
}
