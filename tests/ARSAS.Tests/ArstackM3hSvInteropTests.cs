using System.Buffers.Binary;
using System.Text;
using AR.Iec61850.SampledValues;
using Xunit;

namespace ARSAS.Tests;

/// <summary>
/// Checks that ARSAS's pinned native SV parser accepts the ARStack M3.H
/// reference layout, including priority-tagged VLAN ID 0.
/// This is a wire-decoder test, not evidence of physical Ethernet delivery.
/// </summary>
public sealed class ArstackM3hSvInteropTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SnapshotEngine_RecognizesM3h4I4VFrame(bool vlanPriorityTag)
    {
        var sampleData = new byte[64];
        var analogValues = new[] { 2500, -1250, -1250, 0, 330000, -165000, -165000, 0 };
        for (var i = 0; i < analogValues.Length; i++)
            BinaryPrimitives.WriteInt32BigEndian(sampleData.AsSpan(i * 8, 4), analogValues[i]);

        var asdu = Tlv(0x30,
            Tlv(0x80, Encoding.ASCII.GetBytes("MU01_SV1")),
            Tlv(0x82, U16(3999)),
            Tlv(0x83, new byte[] { 0x00, 0x00, 0x00, 0x01 }),
            Tlv(0x85, new byte[] { 0x02 }),
            Tlv(0x87, sampleData),
            Tlv(0x89, new byte[] { 0x02, 0x11, 0x22, 0xFF, 0xFE, 0x33, 0x44, 0x55 }));

        var apdu = Tlv(0x60,
            Tlv(0x80, new byte[] { 0x01 }),
            Tlv(0xA2, asdu));
        var payload = Join(U16(0x4000), U16((ushort)(8 + apdu.Length)),
            U16(0), U16(0), apdu);
        var ethernet = vlanPriorityTag
            ? Join(DestinationMac, SourceMac, U16(0x8100), U16(0x8000), U16(0x88BA), payload)
            : Join(DestinationMac, SourceMac, U16(0x88BA), payload);

        Assert.True(SampledValuesFrameParser.TryParseEthernetFrame(ethernet, out var frame));
        Assert.Equal((ushort)0x4000, frame.AppId);
        Assert.Equal(vlanPriorityTag, frame.Vlan is not null);
        if (vlanPriorityTag)
            Assert.Equal((ushort)0, frame.Vlan?.VlanId);
        var decoded = Assert.Single(frame.Pdu.Asdus);
        Assert.Equal("MU01_SV1", decoded.SvId);
        Assert.Equal((ushort)3999, decoded.SampleCount);
        Assert.Equal(64, decoded.SamplePayload.Length);
    }

    private static readonly byte[] DestinationMac = { 0x01, 0x0C, 0xCD, 0x04, 0x00, 0x01 };
    private static readonly byte[] SourceMac = { 0x02, 0x11, 0x22, 0x33, 0x44, 0x55 };

    private static byte[] U16(ushort number) => new[] { (byte)(number >> 8), (byte)number };

    private static byte[] Tlv(byte tag, params byte[][] parts)
    {
        var content = Join(parts);
        if (content.Length < 128)
            return Join(new byte[] { tag, (byte)content.Length }, content);
        if (content.Length < 256)
            return Join(new byte[] { tag, 0x81, (byte)content.Length }, content);
        return Join(new byte[] { tag, 0x82, (byte)(content.Length >> 8), (byte)content.Length }, content);
    }

    private static byte[] Join(params byte[][] arrays)
    {
        var size = arrays.Sum(a => a.Length);
        var result = new byte[size];
        var index = 0;
        foreach (var array in arrays)
        {
            array.CopyTo(result, index);
            index += array.Length;
        }
        return result;
    }
}
