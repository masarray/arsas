using System.Buffers.Binary;
using System.Net;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SntpEthernetFrameCodecTests
{
    private static readonly byte[] LaptopMac = [0x02, 0xAA, 0xBB, 0xCC, 0xDD, 0x01];
    private static readonly byte[] RelayMac = [0x00, 0x0E, 0x8C, 0x11, 0x22, 0x33];
    private static readonly IPAddress LaptopIp = IPAddress.Parse("192.168.81.100");
    private static readonly IPAddress RelayIp = IPAddress.Parse("192.168.81.70");

    [Fact]
    public void Mode3RawFrame_IsParsed_AndMode4ReplySwapsEndpointsWithValidChecksums()
    {
        var requestPayload = BuildMode3Request();
        var ethernet = BuildClientFrame(requestPayload, sourcePort: 49152);

        Assert.True(SntpEthernetFrameCodec.TryParseClientRequest(ethernet, LaptopIp, out var parsed));
        Assert.Equal(RelayMac, parsed.SourceMac);
        Assert.Equal(RelayIp, parsed.SourceAddress);
        Assert.Equal(LaptopIp, parsed.DestinationAddress);
        Assert.Equal((ushort)49152, parsed.SourcePort);
        Assert.Equal((ushort)123, parsed.DestinationPort);

        var receive = new DateTimeOffset(2026, 8, 13, 5, 40, 0, TimeSpan.Zero);
        var transmit = receive.AddMilliseconds(1);
        var ntpReply = SntpPacket.BuildServerReply(requestPayload, receive, transmit, new SntpServerProfile());
        var reply = SntpEthernetFrameCodec.BuildServerReply(parsed, LaptopMac, LaptopIp, ntpReply, 0x1234);

        Assert.Equal(RelayMac, reply.AsSpan(0, 6).ToArray());
        Assert.Equal(LaptopMac, reply.AsSpan(6, 6).ToArray());
        Assert.Equal((ushort)0x0800, BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(12, 2)));

        const int ipOffset = 14;
        Assert.Equal(LaptopIp, new IPAddress(reply.AsSpan(ipOffset + 12, 4)));
        Assert.Equal(RelayIp, new IPAddress(reply.AsSpan(ipOffset + 16, 4)));
        Assert.Equal((ushort)0x1234, BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(ipOffset + 4, 2)));
        Assert.Equal((ushort)0, InternetChecksum(reply.AsSpan(ipOffset, 20)));

        const int udpOffset = ipOffset + 20;
        Assert.Equal((ushort)123, BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(udpOffset, 2)));
        Assert.Equal((ushort)49152, BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(udpOffset + 2, 2)));
        Assert.True(UdpChecksumIsValid(reply, ipOffset, udpOffset));

        var payload = reply.AsSpan(udpOffset + 8, SntpPacket.MinimumLength);
        Assert.Equal(4, payload[0] & 0x07);
        Assert.Equal(SntpServerProfile.SiprotecCompatibilityStratum, payload[1]);
        Assert.Equal(requestPayload.AsSpan(40, 8).ToArray(), payload.Slice(24, 8).ToArray());
    }

    [Fact]
    public void VlanTaggedMode3_PreservesTagInRawReply()
    {
        const ushort vlanTci = 0xA064;
        var requestPayload = BuildMode3Request();
        var ethernet = BuildClientFrame(requestPayload, sourcePort: 123, vlanTci: vlanTci);

        Assert.True(SntpEthernetFrameCodec.TryParseClientRequest(ethernet, LaptopIp, out var parsed));
        Assert.Equal(vlanTci, parsed.VlanTci);
        Assert.Equal((ushort)0x8100, parsed.VlanEtherType);

        var replyPayload = SntpPacket.BuildServerReply(
            requestPayload,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            new SntpServerProfile());
        var reply = SntpEthernetFrameCodec.BuildServerReply(parsed, LaptopMac, LaptopIp, replyPayload);

        Assert.Equal((ushort)0x8100, BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(12, 2)));
        Assert.Equal(vlanTci, BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(14, 2)));
        Assert.Equal((ushort)0x0800, BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(16, 2)));
    }

    [Fact]
    public void BroadcastFrame_UsesEthernetBroadcastDirectedIpv4AndMode5()
    {
        var now = new DateTimeOffset(2026, 8, 13, 5, 45, 0, TimeSpan.Zero);
        var ntp = SntpPacket.BuildBroadcast(now, new SntpServerProfile());
        var frame = SntpEthernetFrameCodec.BuildBroadcast(
            LaptopMac,
            LaptopIp,
            IPAddress.Parse("192.168.81.255"),
            ntp,
            7);

        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, frame.AsSpan(0, 6).ToArray());
        Assert.Equal(LaptopMac, frame.AsSpan(6, 6).ToArray());
        Assert.Equal(IPAddress.Parse("192.168.81.255"), new IPAddress(frame.AsSpan(30, 4)));
        Assert.Equal((ushort)123, BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(34, 2)));
        Assert.Equal((ushort)123, BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(36, 2)));
        Assert.Equal(5, frame[42] & 0x07);
        Assert.Equal((byte)2, frame[43]);
        Assert.Equal((ushort)0, InternetChecksum(frame.AsSpan(14, 20)));
        Assert.True(UdpChecksumIsValid(frame, 14, 34));
    }

    [Fact]
    public void NonMode3OrWrongDestinationPort_IsRejected()
    {
        var request = BuildMode3Request();
        request[0] = (byte)((4 << 3) | 4);
        Assert.False(SntpEthernetFrameCodec.TryParseClientRequest(BuildClientFrame(request), LaptopIp, out _));

        request[0] = (byte)((4 << 3) | 3);
        var wrongPort = BuildClientFrame(request, destinationPort: 124);
        Assert.False(SntpEthernetFrameCodec.TryParseClientRequest(wrongPort, LaptopIp, out _));
    }

    private static byte[] BuildMode3Request()
    {
        var request = new byte[SntpPacket.MinimumLength];
        request[0] = (byte)((4 << 3) | 3);
        request[2] = 6;
        SntpPacket.WriteTimestamp(
            request.AsSpan(40, 8),
            new DateTimeOffset(2026, 8, 13, 5, 30, 0, 125, TimeSpan.Zero));
        return request;
    }

    private static byte[] BuildClientFrame(
        byte[] ntpPayload,
        ushort sourcePort = 123,
        ushort destinationPort = 123,
        ushort? vlanTci = null)
    {
        var ethernetLength = vlanTci.HasValue ? 18 : 14;
        var udpLength = 8 + ntpPayload.Length;
        var ipLength = 20 + udpLength;
        var frame = new byte[ethernetLength + ipLength];
        LaptopMac.CopyTo(frame, 0);
        RelayMac.CopyTo(frame, 6);

        if (vlanTci.HasValue)
        {
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(12, 2), 0x8100);
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(14, 2), vlanTci.Value);
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(16, 2), 0x0800);
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(12, 2), 0x0800);
        }

        var ip = frame.AsSpan(ethernetLength, 20);
        ip[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(ip.Slice(2, 2), (ushort)ipLength);
        ip[8] = 64;
        ip[9] = 17;
        RelayIp.GetAddressBytes().CopyTo(ip.Slice(12, 4));
        LaptopIp.GetAddressBytes().CopyTo(ip.Slice(16, 4));
        BinaryPrimitives.WriteUInt16BigEndian(ip.Slice(10, 2), InternetChecksum(ip));

        var udp = frame.AsSpan(ethernetLength + 20, udpLength);
        BinaryPrimitives.WriteUInt16BigEndian(udp.Slice(0, 2), sourcePort);
        BinaryPrimitives.WriteUInt16BigEndian(udp.Slice(2, 2), destinationPort);
        BinaryPrimitives.WriteUInt16BigEndian(udp.Slice(4, 2), (ushort)udpLength);
        ntpPayload.CopyTo(udp.Slice(8));
        return frame;
    }

    private static bool UdpChecksumIsValid(byte[] frame, int ipOffset, int udpOffset)
    {
        var udpLength = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(udpOffset + 4, 2));
        var pseudo = new byte[12 + udpLength];
        frame.AsSpan(ipOffset + 12, 8).CopyTo(pseudo.AsSpan(0, 8));
        pseudo[9] = 17;
        BinaryPrimitives.WriteUInt16BigEndian(pseudo.AsSpan(10, 2), udpLength);
        frame.AsSpan(udpOffset, udpLength).CopyTo(pseudo.AsSpan(12));
        return InternetChecksum(pseudo) == 0;
    }

    private static ushort InternetChecksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        var index = 0;
        while (index + 1 < data.Length)
        {
            sum += BinaryPrimitives.ReadUInt16BigEndian(data.Slice(index, 2));
            index += 2;
        }

        if (index < data.Length)
            sum += (uint)data[index] << 8;

        while ((sum >> 16) != 0)
            sum = (sum & 0xFFFF) + (sum >> 16);

        return unchecked((ushort)~sum);
    }
}
