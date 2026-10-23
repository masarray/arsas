using System;
using System.Text;
using ArIED61850Tester.Protocol.Asn1;

namespace ArIED61850Tester.Protocol.Mms;

public static class MmsVariableAccessAttributesRequest
{
    public static byte[] Build(int invokeId, string domainId, string itemId)
    {
        if (string.IsNullOrWhiteSpace(domainId)) throw new ArgumentException("MMS domain is empty.", nameof(domainId));
        if (string.IsNullOrWhiteSpace(itemId)) throw new ArgumentException("MMS item is empty.", nameof(itemId));
        var mmsPdu = BuildConfirmedGetVariableAccessAttributesPdu(invokeId, domainId, itemId);
        return MmsReadRequest.WrapIsoPresentationPData(mmsPdu);
    }

    public static byte[] BuildConfirmedGetVariableAccessAttributesPdu(int invokeId, string domainId, string itemId)
    {
        // GetVariableAccessAttributes-Request ::= CHOICE { name [0] ObjectName, address [1] Address }
        // ObjectName.domain-specific [1] ::= SEQUENCE { domainId Identifier, itemId Identifier }
        var objectName = Wrap(0xA1, Concat(VisibleString(domainId.Trim()), VisibleString(itemId.Trim())));
        var nameChoice = Wrap(0xA0, objectName);
        var request = Wrap(0xA6, nameChoice); // confirmedServiceRequest.getVariableAccessAttributes [6]
        var invoke = Integer(invokeId);
        return Wrap(0xA0, Concat(invoke, request));
    }

    private static byte[] VisibleString(string text)
        => Wrap(0x1A, Encoding.ASCII.GetBytes(text));

    private static byte[] Integer(int value)
    {
        if (value < 0 || value > 0x7FFFFFFF) throw new ArgumentOutOfRangeException(nameof(value));
        if (value <= 0x7F) return new byte[] { 0x02, 0x01, (byte)value };
        if (value <= 0xFF) return new byte[] { 0x02, 0x02, 0x00, (byte)value };
        if (value <= 0x7FFF) return new byte[] { 0x02, 0x02, (byte)(value >> 8), (byte)value };
        if (value <= 0xFFFF) return new byte[] { 0x02, 0x03, 0x00, (byte)(value >> 8), (byte)value };
        return new byte[] { 0x02, 0x04, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
    }

    private static byte[] Wrap(byte tag, byte[] value)
    {
        var writer = new BerWriter();
        writer.WriteTlv(tag, value);
        return writer.ToArray();
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var length = 0;
        foreach (var part in parts) length += part.Length;
        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }
        return result;
    }
}
