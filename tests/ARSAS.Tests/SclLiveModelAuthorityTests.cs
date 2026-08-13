using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class SclLiveModelAuthorityTests
{
    [Fact]
    public void Device_PrefersNativeLiveModel_WhenSclDesignAndLiveModelAreAvailable()
    {
        var designModel = new LiveIedModelDiscoveryDocument
        {
            IedName = "IED_EXPECTED"
        };
        var liveModel = new LiveIedModelDiscoveryDocument
        {
            IedName = "IED_OBSERVED"
        };
        var device = new Iec61850MonitorDevice
        {
            SclWorkspace = new SclIedWorkspace
            {
                IedName = "IED_EXPECTED",
                DesignModel = designModel
            },
            LiveDiscoveryModel = liveModel
        };

        var comparison = Assert.IsType<SclLiveModelComparisonResult>(device.SclComparison);

        Assert.False(comparison.IsCompatible);
        Assert.Equal("IED_EXPECTED", comparison.ExpectedIedName);
        Assert.Equal("IED_OBSERVED", comparison.ObservedIedName);
        Assert.Contains(
            comparison.Findings,
            finding => finding.Kind == SclLiveModelFindingKind.IdentityMismatch);
    }

    [Fact]
    public void Device_DoesNotReplaceNativeComparison_WithProjectedFallbackResult()
    {
        var device = new Iec61850MonitorDevice
        {
            SclWorkspace = new SclIedWorkspace
            {
                IedName = "IED_EXPECTED",
                DesignModel = new LiveIedModelDiscoveryDocument
                {
                    IedName = "IED_EXPECTED"
                }
            },
            LiveDiscoveryModel = new LiveIedModelDiscoveryDocument
            {
                IedName = "IED_OBSERVED"
            }
        };

        device.SclComparison = new SclLiveModelComparisonResult
        {
            ExpectedIedName = "PROJECTED_EXPECTED",
            ObservedIedName = "PROJECTED_OBSERVED"
        };

        Assert.NotNull(device.SclComparison);
        Assert.Equal("IED_EXPECTED", device.SclComparison!.ExpectedIedName);
        Assert.Equal("IED_OBSERVED", device.SclComparison.ObservedIedName);
    }
}
