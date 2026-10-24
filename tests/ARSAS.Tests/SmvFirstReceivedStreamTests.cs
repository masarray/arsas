using ArIED61850Tester;

namespace ARSAS.Tests;

public sealed class SmvFirstReceivedStreamTests
{
    [Fact]
    public void NewOperatorCanReceiveAnySvWithoutMatchingSclIdentity()
    {
        var row = SmvStreamRow.AnyReceived();

        Assert.Equal("Live Ethernet (auto)", row.Source);
        Assert.Equal("-", row.AppId);
        Assert.Equal("-", row.StreamId);
        Assert.Equal("-", row.DestinationMac);
        Assert.Equal("-", row.SampleRate);
        Assert.Equal("-", row.SampleMode);
        Assert.Equal(0, row.MemberCount);
    }
}
