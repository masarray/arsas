using System;
using System.Collections.Generic;
using System.Text;
using ArIED61850Tester.Protocol.Asn1;

namespace ArIED61850Tester.Protocol.Mms;

public enum MmsGetNameListObjectClass
{
    NamedVariable = 0,
    NamedVariableList = 2,
    Domain = 9
}

public sealed class MmsNameListResult
{
    public bool IsSuccess { get; init; }
    public IReadOnlyList<string> Names { get; init; } = Array.Empty<string>();
    public bool MoreFollows { get; init; }
    public bool MoreFollowsWasPresent { get; init; }
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;
}

public static class MmsGetNameListRequest
{
    public static byte[] Build(int invokeId, MmsGetNameListObjectClass objectClass, string? domainId = null, string? continueAfter = null)
    {
        var mmsPdu = BuildConfirmedGetNameListPdu(invokeId, objectClass, domainId, continueAfter);
        return MmsReadRequest.WrapIsoPresentationPData(mmsPdu);
    }

    public static byte[] BuildConfirmedGetNameListPdu(int invokeId, MmsGetNameListObjectClass objectClass, string? domainId = null, string? continueAfter = null)
    {
        var objectClassValue = RawIntegerValue((int)objectClass);
        var objectClassNode = Wrap(0x80, objectClassValue); // objectClass [0] IMPLICIT INTEGER
        var objectClassField = Wrap(0xA0, objectClassNode);

        byte[] objectScopeChoice;
        if (string.IsNullOrWhiteSpace(domainId))
        {
            objectScopeChoice = Wrap(0x80, Array.Empty<byte>()); // vmdSpecific [0] NULL-ish empty payload
        }
        else
        {
            objectScopeChoice = Wrap(0x81, Encoding.ASCII.GetBytes(domainId.Trim())); // domainSpecific [1] Identifier
        }

        var objectScopeField = Wrap(0xA1, objectScopeChoice);
        var body = Concat(objectClassField, objectScopeField);

        if (!string.IsNullOrWhiteSpace(continueAfter))
            body = Concat(body, Wrap(0x82, Encoding.ASCII.GetBytes(continueAfter.Trim()))); // continueAfter [2] Identifier

        var getNameList = Wrap(0xA1, body); // confirmedServiceRequest.getNameList [1]
        var invoke = Integer(invokeId);
        return Wrap(0xA0, Concat(invoke, getNameList)); // Confirmed-RequestPDU [0]
    }


    private static byte[] RawIntegerValue(int value)
    {
        if (value < 0 || value > 0x7FFFFFFF) throw new ArgumentOutOfRangeException(nameof(value));
        if (value <= 0x7F) return new byte[] { (byte)value };
        if (value <= 0xFF) return new byte[] { 0x00, (byte)value };
        if (value <= 0x7FFF) return new byte[] { (byte)(value >> 8), (byte)value };
        if (value <= 0xFFFF) return new byte[] { 0x00, (byte)(value >> 8), (byte)value };
        return new byte[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
    }

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
