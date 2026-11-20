using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SmvP0FullIedNameAuthorityTests
{
    [Fact]
    public void ProvisionalLiveModelName_CannotOverrideAllObservedMmsDomains()
    {
        var model = new LiveIedModelDiscoveryDocument
        {
            Host = "192.0.2.22",
            // This is a provisional label, NOT a verified operator/SCL override.
            IedName = "SIE",
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel { MmsDomain = "SIEBCUCTRL" },
                new LiveIedLogicalDeviceModel { MmsDomain = "SIEBCUMEAS" },
                new LiveIedLogicalDeviceModel { MmsDomain = "SIEBCUPROT" },
                new LiveIedLogicalDeviceModel { MmsDomain = "SIEBCUUD1" },
                new LiveIedLogicalDeviceModel { MmsDomain = "SIEBCUUD2" }
            ]
        };

        var identity = Iec61850DeviceIdentityResolver.Resolve(
            null, model, Array.Empty<SignalDefinition>());

        Assert.Equal("SIEBCU", identity.IedName);
        Assert.Contains("MmsDomainCommonPrefix", identity.Source);
        Assert.DoesNotContain("ExplicitOverride", identity.Source);
        Assert.Equal(5, identity.LogicalDevices.Count);
    }

    [Fact]
    public void ConflictingOrUnprovenNames_NeverClaimVerifiedTopology()
    {
        var model = new LiveIedModelDiscoveryDocument
        {
            Host = "192.0.2.23",
            IedName = "KNOWN_ALIAS",
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel { MmsDomain = "ALPHA_LD0" },
                new LiveIedLogicalDeviceModel { MmsDomain = "BETA_LD0" }
            ]
        };
        var identity = Iec61850DeviceIdentityResolver.Resolve(
            null, model, Array.Empty<SignalDefinition>());

        Assert.NotEqual("KNOWN_ALIAS", identity.IedName);
        Assert.Contains("Low", identity.Source);
        Assert.Contains("MmsDomainAmbiguous", identity.Source);
    }
}
