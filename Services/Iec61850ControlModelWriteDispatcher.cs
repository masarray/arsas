using ArIED61850Tester.Models;
using AR.Iec61850.Control;

namespace ArIED61850Tester.Services;

/// <summary>
/// Statically bound dispatch for the WPF ctlModel editor. The adapter is not
/// XAML-generated, and its signature is verified during normal C# compilation.
/// No reflection, generic MMS Write, or automatic configuration.
/// </summary>
public static class Iec61850ControlModelWriteDispatcher
{
    public static Task<Iec61850ControlModelConfigurationResult> ConfigureAsync(
        global::ArIED61850Tester.Iec61850MonitorRuntime runtime, string deviceId, SignalDefinition signal,
        Iec61850ControlModelKind expected, Iec61850ControlModelKind requested,
        CancellationToken cancellationToken)
        => runtime.ConfigureControlModelAsync(deviceId, signal, expected, requested, cancellationToken);
}
