using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// A zero-network-cost read-only lease for sharing the existing monitoring
/// association's exact discovered schema with an independent MMS reader.
/// Never copy SCL-only declarations, and reject catalog/session replacement.
/// No attribute values, network handles or report state are cached here.
/// </summary>
public sealed class IedBrowserIsolatedReadSchemaLease
{
    public LiveIedModelDiscoveryDocument Model { get; }
    private readonly long _association;
    private readonly long _projectionEpoch;
    private readonly string _ip;
    private readonly int _port;

    private IedBrowserIsolatedReadSchemaLease(Iec61850MonitorDevice device,
        LiveIedModelDiscoveryDocument model)
    {
        Model = model;
        _association = device.LiveValues.Association;
        _projectionEpoch = device.BrowserProjectionEpoch;
        _ip = device.IpAddress;
        _port = device.Port;
    }

    public static IedBrowserIsolatedReadSchemaLease? Capture(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return device is { IsConnected: true, IsMonitoring: true, LiveDiscoveryModel: { } model } &&
            !device.LiveValues.IsOffline
            ? new IedBrowserIsolatedReadSchemaLease(device, model)
            : null;
    }

    public bool IsCurrent(Iec61850MonitorDevice device)
        => device.IsConnected && device.IsMonitoring && !device.LiveValues.IsOffline &&
           ReferenceEquals(Model, device.LiveDiscoveryModel) &&
           device.LiveValues.Association == _association &&
           device.BrowserProjectionEpoch == _projectionEpoch &&
           string.Equals(device.IpAddress, _ip, StringComparison.Ordinal) &&
           device.Port == _port;
}
