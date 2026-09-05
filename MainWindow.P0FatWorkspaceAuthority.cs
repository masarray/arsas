using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    internal bool HasP0SharedStaticDataSetAuthority(IoTestIedPlan ied)
    {
        ArgumentNullException.ThrowIfNull(ied);
        var device = ResolveIoTestDevice(ied.LiveDeviceId)
                     ?? ResolveIoTestDevice(ied.IpAddress)
                     ?? ResolveIoTestDevice(ied.IedName);
        return device is not null && IsSharedStaticDataSetAuthority(device);
    }
}
