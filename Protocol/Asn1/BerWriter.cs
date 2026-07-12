using System;
using System.Collections.Generic;
using System.IO;

namespace ArIED61850Tester.Protocol.Asn1;

public sealed class BerWriter
{
    private readonly MemoryStream _stream = new();

    public void WriteTlv(byte tag, ReadOnlySpan<byte> value)
    {
        _stream.WriteByte(tag);
        WriteLength(value.Length);
        _stream.Write(value);
    }

    public void WriteLength(int length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (length < 0x80)
        {
            _stream.WriteByte((byte)length);
            return;
        }

        var bytes = new List<byte>();
        var value = length;
        while (value > 0)
        {
            bytes.Insert(0, (byte)(value & 0xFF));
            value >>= 8;
        }

        _stream.WriteByte((byte)(0x80 | bytes.Count));
        foreach (var b in bytes) _stream.WriteByte(b);
    }

    public byte[] ToArray() => _stream.ToArray();
}
