using System;
using System.IO;

namespace ArIED61850Tester.Protocol.Asn1;

public readonly record struct BerTlv(byte Tag, int Length, ReadOnlyMemory<byte> Value);

public sealed class BerReader
{
    private readonly ReadOnlyMemory<byte> _buffer;
    private int _offset;

    public BerReader(ReadOnlyMemory<byte> buffer)
    {
        _buffer = buffer;
    }

    public bool EndOfBuffer => _offset >= _buffer.Length;

    public BerTlv ReadTlv()
    {
        if (_offset >= _buffer.Length) throw new InvalidDataException("Unexpected end of BER buffer.");
        var span = _buffer.Span;
        var tag = span[_offset++];
        var length = ReadLength(span);
        if (_offset + length > _buffer.Length) throw new InvalidDataException("BER length exceeds buffer size.");
        var value = _buffer.Slice(_offset, length);
        _offset += length;
        return new BerTlv(tag, length, value);
    }

    private int ReadLength(ReadOnlySpan<byte> span)
    {
        if (_offset >= span.Length) throw new InvalidDataException("Missing BER length byte.");
        var first = span[_offset++];
        if ((first & 0x80) == 0) return first;

        var count = first & 0x7F;
        if (count == 0) throw new InvalidDataException("Indefinite BER length is not supported in MMS client phase N0.");
        if (count > 4) throw new InvalidDataException("BER length-of-length is too large.");
        if (_offset + count > span.Length) throw new InvalidDataException("Truncated BER length.");

        var length = 0;
        for (var i = 0; i < count; i++)
            length = (length << 8) | span[_offset++];
        return length;
    }
}
