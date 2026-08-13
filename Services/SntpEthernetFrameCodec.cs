using System.Buffers.Binary;
using System.Net;

namespace ArIED61850Tester.Services;

public readonly record struct SntpRawClientFrame(
    byte[] SourceMac,
    byte[] DestinationMac,
    IPAddress SourceAddress,
    IPAddress DestinationAddress,
    ushort SourcePort,
    ushort DestinationPort,
    ushort? VlanTci,
    ushort? VlanEtherType,
    byte[] Payload);

/// <summary>
/// Minimal Ethernet/IPv4/UDP codec used by the raw Npcap SNTP fallback.
/// It intentionally understands only the traffic needed by commissioning NTP:
/// Ethernet (optionally one VLAN tag), IPv4 without fragmentation, UDP and SNTP Mode 3.
/// </summary>
public static class SntpEthernetFrameCodec
{
    private const ushort EtherTypeIpv4 = 0x0800;
    private const ushort EtherTypeDot1Q = 0x8100;
    private const ushort EtherTypeDot1Ad = 0x88A8;
    private const byte IpProtocolUdp = 17;
    private const int Ipv4HeaderLength = 20;
    private const int UdpHeaderLength = 8;

    public static bool TryParseClientRequest(
        ReadOnlySpan<byte> frame,
        IPAddress localAddress,
        out SntpRawClientFrame request)
    {
        request = default;
        ArgumentNullException.ThrowIfNull(localAddress);

        if (localAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || frame.Length < 14)
            return false;

        var sourceMac = frame.Slice(6, 6).ToArray();
        var destinationMac = frame.Slice(0, 6).ToArray();
        var etherType = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(12, 2));
        var networkOffset = 14;
        ushort? vlanTci = null;
        ushort? vlanEtherType = null;

        if (etherType is EtherTypeDot1Q or EtherTypeDot1Ad)
        {
            if (frame.Length < 18)
                return false;

            vlanEtherType = etherType;
            vlanTci = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(14, 2));
            etherType = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(16, 2));
            networkOffset = 18;
        }

        if (etherType != EtherTypeIpv4 || frame.Length < networkOffset + Ipv4HeaderLength)
            return false;

        var versionAndHeaderLength = frame[networkOffset];
        if ((versionAndHeaderLength >> 4) != 4)
            return false;

        var ipHeaderLength = (versionAndHeaderLength & 0x0F) * 4;
        if (ipHeaderLength < Ipv4HeaderLength || frame.Length < networkOffset + ipHeaderLength + UdpHeaderLength)
            return false;

        var totalLength = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(networkOffset + 2, 2));
        if (totalLength < ipHeaderLength + UdpHeaderLength || frame.Length < networkOffset + totalLength)
            return false;

        var fragmentField = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(networkOffset + 6, 2));
        if ((fragmentField & 0x3FFF) != 0)
            return false;

        if (frame[networkOffset + 9] != IpProtocolUdp)
            return false;

        var sourceAddress = new IPAddress(frame.Slice(networkOffset + 12, 4));
        var destinationAddress = new IPAddress(frame.Slice(networkOffset + 16, 4));
        var udpOffset = networkOffset + ipHeaderLength;
        var sourcePort = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(udpOffset, 2));
        var destinationPort = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(udpOffset + 2, 2));
        var udpLength = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(udpOffset + 4, 2));

        if (destinationPort != 123 || udpLength < UdpHeaderLength || udpOffset + udpLength > networkOffset + totalLength)
            return false;

        if (!destinationAddress.Equals(localAddress) &&
            !destinationAddress.Equals(IPAddress.Broadcast) &&
            !IsSubnetBroadcastFor(localAddress, destinationAddress))
            return false;

        var payload = frame.Slice(udpOffset + UdpHeaderLength, udpLength - UdpHeaderLength).ToArray();
        if (!SntpPacket.TryReadClientRequest(payload, out _))
            return false;

        request = new SntpRawClientFrame(
            sourceMac,
            destinationMac,
            sourceAddress,
            destinationAddress,
            sourcePort,
            destinationPort,
            vlanTci,
            vlanEtherType,
            payload);
        return true;
    }

    public static byte[] BuildServerReply(
        in SntpRawClientFrame request,
        ReadOnlySpan<byte> localMac,
        IPAddress localAddress,
        ReadOnlySpan<byte> sntpPayload,
        ushort identification = 0)
    {
        ValidateMac(localMac);
        ValidateIpv4(localAddress);
        if (request.SourceMac is not { Length: 6 })
            throw new ArgumentException("Raw SNTP request must contain a six-byte source MAC.", nameof(request));

        return BuildIpv4UdpFrame(
            destinationMac: request.SourceMac,
            sourceMac: localMac,
            sourceAddress: localAddress,
            destinationAddress: request.SourceAddress,
            sourcePort: 123,
            destinationPort: request.SourcePort,
            payload: sntpPayload,
            vlanTci: request.VlanTci,
            vlanEtherType: request.VlanEtherType,
            identification: identification);
    }

    public static byte[] BuildBroadcast(
        ReadOnlySpan<byte> localMac,
        IPAddress localAddress,
        IPAddress directedBroadcast,
        ReadOnlySpan<byte> sntpPayload,
        ushort identification = 0)
    {
        ValidateMac(localMac);
        ValidateIpv4(localAddress);
        ValidateIpv4(directedBroadcast);

        Span<byte> destinationMac = stackalloc byte[6];
        destinationMac.Fill(0xFF);
        return BuildIpv4UdpFrame(
            destinationMac,
            localMac,
            localAddress,
            directedBroadcast,
            123,
            123,
            sntpPayload,
            null,
            null,
            identification);
    }

    private static byte[] BuildIpv4UdpFrame(
        ReadOnlySpan<byte> destinationMac,
        ReadOnlySpan<byte> sourceMac,
        IPAddress sourceAddress,
        IPAddress destinationAddress,
        ushort sourcePort,
        ushort destinationPort,
        ReadOnlySpan<byte> payload,
        ushort? vlanTci,
        ushort? vlanEtherType,
        ushort identification)
    {
        ValidateMac(destinationMac);
        ValidateMac(sourceMac);
        ValidateIpv4(sourceAddress);
        ValidateIpv4(destinationAddress);

        var hasVlan = vlanTci.HasValue;
        var ethernetLength = hasVlan ? 18 : 14;
        var udpLength = checked(UdpHeaderLength + payload.Length);
        var ipLength = checked(Ipv4HeaderLength + udpLength);
        if (udpLength > ushort.MaxValue || ipLength > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(payload), "SNTP Ethernet payload is too large for IPv4/UDP.");

        var result = new byte[ethernetLength + ipLength];
        destinationMac.CopyTo(result.AsSpan(0, 6));
        sourceMac.CopyTo(result.AsSpan(6, 6));

        if (hasVlan)
        {
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(12, 2), vlanEtherType ?? EtherTypeDot1Q);
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(14, 2), vlanTci!.Value);
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(16, 2), EtherTypeIpv4);
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(12, 2), EtherTypeIpv4);
        }

        var ip = result.AsSpan(ethernetLength, Ipv4HeaderLength);
        ip.Clear();
        ip[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(ip.Slice(2, 2), checked((ushort)ipLength));
        BinaryPrimitives.WriteUInt16BigEndian(ip.Slice(4, 2), identification);
        ip[8] = 64;
        ip[9] = IpProtocolUdp;
        sourceAddress.GetAddressBytes().AsSpan().CopyTo(ip.Slice(12, 4));
        destinationAddress.GetAddressBytes().AsSpan().CopyTo(ip.Slice(16, 4));
        BinaryPrimitives.WriteUInt16BigEndian(ip.Slice(10, 2), ComputeInternetChecksum(ip));

        var udp = result.AsSpan(ethernetLength + Ipv4HeaderLength, udpLength);
        udp.Clear();
        BinaryPrimitives.WriteUInt16BigEndian(udp.Slice(0, 2), sourcePort);
        BinaryPrimitives.WriteUInt16BigEndian(udp.Slice(2, 2), destinationPort);
        BinaryPrimitives.WriteUInt16BigEndian(udp.Slice(4, 2), checked((ushort)udpLength));
        payload.CopyTo(udp.Slice(UdpHeaderLength));

        var udpChecksum = ComputeUdpChecksum(sourceAddress, destinationAddress, udp);
        BinaryPrimitives.WriteUInt16BigEndian(udp.Slice(6, 2), udpChecksum == 0 ? (ushort)0xFFFF : udpChecksum);
        return result;
    }

    private static ushort ComputeUdpChecksum(IPAddress sourceAddress, IPAddress destinationAddress, ReadOnlySpan<byte> udp)
    {
        var pseudo = new byte[12 + udp.Length];
        sourceAddress.GetAddressBytes().AsSpan().CopyTo(pseudo.AsSpan(0, 4));
        destinationAddress.GetAddressBytes().AsSpan().CopyTo(pseudo.AsSpan(4, 4));
        pseudo[9] = IpProtocolUdp;
        BinaryPrimitives.WriteUInt16BigEndian(pseudo.AsSpan(10, 2), checked((ushort)udp.Length));
        udp.CopyTo(pseudo.AsSpan(12));
        pseudo[18] = 0;
        pseudo[19] = 0;
        return ComputeInternetChecksum(pseudo);
    }

    internal static ushort ComputeInternetChecksum(ReadOnlySpan<byte> data)
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

    private static bool IsSubnetBroadcastFor(IPAddress localAddress, IPAddress destinationAddress)
    {
        var local = localAddress.GetAddressBytes();
        var destination = destinationAddress.GetAddressBytes();
        if (local.Length != 4 || destination.Length != 4)
            return false;

        // Avoid guessing the Windows prefix here. This check only permits an IPv4 address
        // ending in .255 as a compatibility path; exact directed-broadcast validation is
        // performed by SntpNetworkRouteResolver before ARSAS transmits its own broadcasts.
        return destination[3] == 0xFF && destination[0] == local[0] && destination[1] == local[1];
    }

    private static void ValidateMac(ReadOnlySpan<byte> mac)
    {
        if (mac.Length != 6)
            throw new ArgumentException("Ethernet MAC address must contain exactly six bytes.", nameof(mac));
    }

    private static void ValidateIpv4(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new NotSupportedException("Raw SNTP transport currently supports IPv4 only.");
    }
}
