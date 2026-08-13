using System.Net;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SntpPacketTests
{
    [Fact]
    public void ClientRequest_IsRecognized_AndReplyCopiesVersionPollAndOriginateTimestamp()
    {
        var request = new byte[SntpPacket.MinimumLength];
        request[0] = (byte)((4 << 3) | 3);
        request[2] = 9;
        var clientTransmit = new DateTimeOffset(2026, 8, 13, 2, 3, 4, 567, TimeSpan.Zero);
        SntpPacket.WriteTimestamp(request.AsSpan(40, 8), clientTransmit);

        Assert.True(SntpPacket.TryReadClientRequest(request, out var parsed));
        Assert.Equal((byte)4, parsed.Version);
        Assert.Equal((sbyte)9, parsed.PollExponent);

        var receive = clientTransmit.AddMilliseconds(2);
        var transmit = receive.AddMilliseconds(1);
        var reply = SntpPacket.BuildServerReply(request, receive, transmit, new SntpServerProfile());

        Assert.Equal(4, reply[0] & 0x07);
        Assert.Equal(4, (reply[0] >> 3) & 0x07);
        Assert.Equal(SntpServerProfile.SiprotecCompatibilityStratum, reply[1]);
        Assert.Equal(9, unchecked((sbyte)reply[2]));
        Assert.Equal(request.AsSpan(40, 8).ToArray(), reply.AsSpan(24, 8).ToArray());
        Assert.InRange((SntpPacket.ReadTimestamp(reply.AsSpan(32, 8), receive) - receive).Duration(), TimeSpan.Zero, TimeSpan.FromTicks(2));
        Assert.InRange((SntpPacket.ReadTimestamp(reply.AsSpan(40, 8), transmit) - transmit).Duration(), TimeSpan.Zero, TimeSpan.FromTicks(2));
    }

    [Fact]
    public void Broadcast_IsMode5_AndUsesSiprotecCompatibilityStratum()
    {
        var now = new DateTimeOffset(2026, 8, 13, 2, 3, 4, TimeSpan.Zero);
        var packet = SntpPacket.BuildBroadcast(now, new SntpServerProfile());

        Assert.Equal(SntpPacket.MinimumLength, packet.Length);
        Assert.Equal(5, packet[0] & 0x07);
        Assert.Equal(4, (packet[0] >> 3) & 0x07);
        Assert.Equal((byte)2, SntpServerProfile.SiprotecCompatibilityStratum);
        Assert.Equal(SntpServerProfile.SiprotecCompatibilityStratum, packet[1]);
        Assert.Equal(6, unchecked((sbyte)packet[2]));
        Assert.Equal("LOCL", System.Text.Encoding.ASCII.GetString(packet, 12, 4));
        Assert.InRange((SntpPacket.ReadTimestamp(packet.AsSpan(40, 8), now) - now).Duration(), TimeSpan.Zero, TimeSpan.FromTicks(2));
    }

    [Fact]
    public void UnsynchronizedReply_UsesLeapAlarmStratumZeroAndZeroServerTimestamps()
    {
        var request = new byte[SntpPacket.MinimumLength];
        request[0] = (byte)((4 << 3) | 3);
        request[2] = 7;
        var clientTransmit = DateTimeOffset.UtcNow;
        SntpPacket.WriteTimestamp(request.AsSpan(40, 8), clientTransmit);

        var reply = SntpPacket.BuildServerReply(
            request,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            new SntpServerProfile(),
            synchronized: false);

        Assert.Equal(3, (reply[0] >> 6) & 0x03);
        Assert.Equal(0, reply[1]);
        Assert.Equal(7, unchecked((sbyte)reply[2]));
        Assert.Equal("INIT", System.Text.Encoding.ASCII.GetString(reply, 12, 4));
        Assert.All(reply.AsSpan(16, 8).ToArray(), value => Assert.Equal(0, value));
        Assert.Equal(request.AsSpan(40, 8).ToArray(), reply.AsSpan(24, 8).ToArray());
        Assert.All(reply.AsSpan(32, 8).ToArray(), value => Assert.Equal(0, value));
        Assert.All(reply.AsSpan(40, 8).ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public void DirectedBroadcast_IsCalculatedFromMask()
    {
        var broadcast = SntpNetworkRouteResolver.ComputeDirectedBroadcast(
            IPAddress.Parse("192.168.10.42"),
            IPAddress.Parse("255.255.255.0"));

        Assert.Equal(IPAddress.Parse("192.168.10.255"), broadcast);
    }

    [Fact]
    public void DirectedBroadcast_IsSuppressedForPointToPointPrefixes()
    {
        Assert.Null(SntpNetworkRouteResolver.ComputeDirectedBroadcast(
            IPAddress.Parse("10.0.0.1"),
            IPAddress.Parse("255.255.255.254")));
    }

    [Fact]
    public void TimestampRoundTrip_PreservesSubMillisecondTime()
    {
        var expected = new DateTimeOffset(2026, 8, 13, 2, 3, 4, 123, TimeSpan.Zero).AddTicks(4567);
        Span<byte> wire = stackalloc byte[8];

        SntpPacket.WriteTimestamp(wire, expected);
        var actual = SntpPacket.ReadTimestamp(wire, expected);

        Assert.InRange((actual - expected).Duration(), TimeSpan.Zero, TimeSpan.FromTicks(2));
    }
}
