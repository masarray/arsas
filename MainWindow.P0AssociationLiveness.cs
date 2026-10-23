namespace ArIED61850Tester;

/// <summary>
/// Association liveness belongs to the per-IED MMS runtime, not to the WPF shell.
///
/// The previous implementation used ICMP Ping as an authoritative death signal after an
/// endpoint had once answered ICMP. That can tear down a healthy IEC 61850 association when
/// ICMP is filtered, rate-limited, or transiently lost. It also duplicated the bounded
/// reconnect state machine already owned by <see cref="Services.Iec61850MonitorRuntime"/>.
///
/// Keep this partial file as the explicit architectural boundary: UI code may render runtime
/// liveness/reconnect state, but it must not independently declare an MMS association dead or
/// start a second reconnect owner.
/// </summary>
public partial class MainWindow
{
}
