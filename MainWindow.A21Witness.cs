using ArIED61850Tester.Services;

namespace ArIED61850Tester;

/// <summary>
/// Read-only commissioning observer surface. Exposes the already-existing runtime
/// instance so G2.5-A2.1 can subscribe to Diagnostic events without changing the
/// control transaction implementation.
/// </summary>
public partial class MainWindow
{
    internal Iec61850MonitorRuntime A21WitnessRuntime => _runtime;
}
