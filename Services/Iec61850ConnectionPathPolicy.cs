using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public enum Iec61850ConnectionPath
{
    FullDiscovery,
    CachedLiveModel,
    SclAssisted
}

/// <summary>
/// Pure routing policy. SCL design authority is distinct from a saved live-discovery
/// cache: Play/Connect uses the SCL-assisted path, while Re-scan remains an explicit
/// caller of full discovery.
/// </summary>
public static class Iec61850ConnectionPathPolicy
{
    public static Iec61850ConnectionPath SelectForFastConnect(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.HasSclDesignModel)
            return Iec61850ConnectionPath.SclAssisted;
        if (device.HasDiscoveryCache && device.Signals.Count > 0)
            return Iec61850ConnectionPath.CachedLiveModel;
        return Iec61850ConnectionPath.FullDiscovery;
    }
}
