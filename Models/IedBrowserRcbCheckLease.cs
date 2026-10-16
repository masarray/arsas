using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Immutable UI transaction ticket for one explicit read-only RCB probe.
/// Capturing a ticket is not a protocol read or another RCB state cache.
/// Prevents an in-flight result or previously displayed status from crossing
/// IED object, MMS association, live model, or SCL workspace replacement.
/// </summary>
public sealed class IedBrowserRcbCheckLease
{
    private readonly Iec61850MonitorDevice _device;
    private readonly long _association;
    private readonly LiveIedModelDiscoveryDocument? _model;
    private readonly SclIedWorkspace? _sclWorkspace;

    public IedBrowserRcbCheckLease(Iec61850MonitorDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _association = device.LiveValues.Association;
        _model = device.LiveDiscoveryModel;
        _sclWorkspace = device.SclWorkspace;
    }

    public bool IsCurrent(Iec61850MonitorDevice? device) =>
        ReferenceEquals(device, _device) &&
        device.IsConnected && !device.LiveValues.IsOffline &&
        device.LiveValues.Association == _association &&
        ReferenceEquals(device.LiveDiscoveryModel, _model) &&
        ReferenceEquals(device.SclWorkspace, _sclWorkspace);
}
