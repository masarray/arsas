using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ArIED61850Tester.Protocol.Asn1;
using ArIED61850Tester.Protocol.Diagnostics;

namespace ArIED61850Tester.Protocol.Mms;

public sealed class MmsReadDecodeResult
{
    public bool IsSuccess { get; init; }
    public object? Value { get; init; }
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;
}

public static class MmsReadResponseDecoder
{
    public static MmsReadDecodeResult DecodeSingleVariable(byte[] presentationPayload, string dataTypeHint)
        => DecodeSingleVariable(presentationPayload, dataTypeHint, expectedInvokeId: null);

    public static MmsReadDecodeResult DecodeSingleVariable(byte[] presentationPayload, string dataTypeHint, int? expectedInvokeId)
    {
        var hex = HexDump.ToCompactString(presentationPayload);
        try
        {
            var mms = StripPresentationPrefix(presentationPayload);
            if (mms.Length == 0)
                return Fail("Empty MMS response payload.", hex);

            if (ContainsConfirmedError(mms, out var errorText))
                return Fail(errorText, hex);

            if (!TryValidateConfirmedResponse(mms, expectedInvokeId, out var responseMessage))
                return Fail(responseMessage, hex);

            var values = new List<BerTlv>();
            CollectTlv(mms, values, depth: 0);

            if (TryFindAccessFailure(values, out var accessFailure))
                return Fail(accessFailure, hex);

            var hint = (dataTypeHint ?? string.Empty).Trim().ToLowerInvariant();

            if (TryDecodeByHint(values, hint, out var hinted))
                return Ok(hinted, hex);

            if (TryDecodeAny(values, out var value))
                return Ok(value, hex);

            return Fail("MMS read response was received, but this native decoder does not yet recognize the returned data type.", hex);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            return Fail($"MMS read response decode failed: {ex.GetType().Name}: {ex.Message}", hex);
        }
    }

    private static MmsReadDecodeResult Ok(object? value, string hex) => new()
    {
        IsSuccess = true,
        Value = value,
        Message = $"Native MMS Confirmed-Read decoded value: {value ?? "<null>"}.",
        ResponseHexPreview = hex
    };

    private static MmsReadDecodeResult Fail(string message, string hex) => new()
    {
        IsSuccess = false,
        Message = message,
        ResponseHexPreview = hex
    };

    private static byte[] StripPresentationPrefix(byte[] payload)
    {
        if (payload.Length == 0) return payload;

        // Normal post-association IEC 61850 MMS P-DATA shape:
        // Session DT SPDU 01 00 + presentation selector 01 00 + User-data fully-encoded-data 61 ...
        // Then PDV-list carries context-id 3 and presentation-data-values.single-ASN1-type [0]
        // whose value is the MMS PDU.
        if (payload.Length > 5 && payload[0] == 0x01 && payload[1] == 0x00 && payload[2] == 0x01 && payload[3] == 0x00 && payload[4] == 0x61)
        {
            if (TryExtractMmsFromFullyEncodedData(payload.AsMemory(4), out var mms))
                return mms;
        }

        // Some traces/tools expose the presentation user-data without the two session bytes.
        if (payload.Length > 3 && payload[0] == 0x01 && payload[1] == 0x00 && payload[2] == 0x61)
        {
            if (TryExtractMmsFromFullyEncodedData(payload.AsMemory(2), out var mms))
                return mms;
        }

        // Some decoders expose only the fully-encoded-data node.
        if (payload[0] == 0x61 && TryExtractMmsFromFullyEncodedData(payload, out var directMms))
            return directMms;

        // Diagnostic fallback: some captures expose only the session data-transfer SPDU
        // before the MMS PDU. Strip it only when the next byte already looks like MMS.
        if (payload.Length > 2 && payload[0] == 0x01 && payload[1] == 0x00 && (payload[2] & 0xE0) == 0xA0)
            return payload.Skip(2).ToArray();

        return payload;
    }

    private static bool TryExtractMmsFromFullyEncodedData(ReadOnlyMemory<byte> payload, out byte[] mms)
    {
        mms = Array.Empty<byte>();

        try
        {
            var outer = new BerReader(payload).ReadTlv();
            if (outer.Tag != 0x61) return false;

            var listReader = new BerReader(outer.Value);
            if (listReader.EndOfBuffer) return false;
            var pdvList = listReader.ReadTlv();
            if (pdvList.Tag != 0x30) return false;

            var pdvReader = new BerReader(pdvList.Value);
            while (!pdvReader.EndOfBuffer)
            {
                var item = pdvReader.ReadTlv();
                if (item.Tag == 0xA0)
                {
                    mms = item.Value.ToArray();
                    return mms.Length > 0;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            return false;
        }

        return false;
    }

    private static bool ContainsConfirmedError(byte[] mms, out string message)
    {
        message = string.Empty;
        if (mms.Length > 0 && mms[0] == 0xA2)
        {
            message = $"MMS Confirmed-Error PDU received: {HexDump.ToCompactString(mms)}";
            return true;
        }
        if (mms.Length > 0 && (mms[0] == 0xA3 || mms[0] == 0xA4))
        {
            message = $"MMS Reject/Abort PDU received: {HexDump.ToCompactString(mms)}";
            return true;
        }
        return false;
    }

    private static bool TryValidateConfirmedResponse(byte[] mms, int? expectedInvokeId, out string message)
    {
        message = string.Empty;
        if (mms.Length == 0) { message = "Empty MMS PDU."; return false; }
        if (mms[0] != 0xA1)
        {
            // Some vendor traces may include session/presentation bytes the stripper has not learned yet.
            // Keep this as a failure with hex preview, not a fake decode.
            message = $"Expected MMS Confirmed-Response PDU [1] (0xA1), received 0x{mms[0]:X2}.";
            return false;
        }

        if (!expectedInvokeId.HasValue)
            return true;

        var outer = new BerReader(mms).ReadTlv();
        var reader = new BerReader(outer.Value);
        if (reader.EndOfBuffer)
        {
            message = "MMS Confirmed-Response PDU is empty.";
            return false;
        }

        var invoke = reader.ReadTlv();
        if (invoke.Tag != 0x02)
        {
            message = $"MMS Confirmed-Response did not start with invokeID. First inner tag=0x{invoke.Tag:X2}.";
            return false;
        }

        var actual = DecodeUnsigned(invoke.Value.Span);
        if (actual != expectedInvokeId.Value)
        {
            message = $"MMS invokeID mismatch. Expected {expectedInvokeId.Value}, received {actual}.";
            return false;
        }

        return true;
    }

    private static bool TryFindAccessFailure(List<BerTlv> values, out string message)
    {
        message = string.Empty;
        var failure = values.LastOrDefault(v => v.Tag == 0x81 && v.Length > 0 && v.Length <= 4);
        if (failure.Tag != 0x81)
            return false;

        var code = DecodeUnsigned(failure.Value.Span);
        message = $"MMS read returned AccessResult.failure code {code}. Raw failure: {HexDump.ToCompactString(failure.Value.ToArray())}";
        return true;
    }

    private static void CollectTlv(ReadOnlyMemory<byte> buffer, List<BerTlv> output, int depth)
    {
        if (depth > 24 || buffer.Length < 2) return;
        var reader = new BerReader(buffer);
        while (!reader.EndOfBuffer)
        {
            var tlv = reader.ReadTlv();
            output.Add(tlv);
            if ((tlv.Tag & 0x20) == 0x20)
                CollectTlv(tlv.Value, output, depth + 1);
        }
    }

    private static bool TryDecodeByHint(List<BerTlv> values, string hint, out object? value)
    {
        value = null;
        if (hint.Contains("bool") || hint.Contains("boolean"))
            return TryDecodeBoolean(values, out value);
        if (hint.Contains("dbpos"))
            return TryDecodeDbpos(values, out value) || TryDecodeInteger(values, out value);
        if (hint.Contains("quality") || hint == "q")
            return TryDecodeQuality(values, out value);
        if (hint.Contains("bit"))
            return TryDecodeBitString(values, out value);
        if (hint.Contains("float") || hint.Contains("analogue") || hint.Contains("mag") || hint.Contains("mv") || hint.Contains("meas"))
            return TryDecodeFloat(values, out value) || TryDecodeInteger(values, out value);
        if (hint.Contains("int") || hint.Contains("enum") || hint.Contains("ctl") || hint.Contains("stval"))
            return TryDecodeInteger(values, out value) || TryDecodeDbpos(values, out value) || TryDecodeBoolean(values, out value);
        if (hint.Contains("time") || hint.Contains("timestamp") || hint == "t")
            return TryDecodeTime(values, out value);
        if (hint.Contains("string") || hint.Contains("visible") || hint.Contains("id") || hint.Contains("datset"))
            return TryDecodeVisibleString(values, out value);
        return false;
    }

    private static bool TryDecodeAny(List<BerTlv> values, out object? value)
    {
        return TryDecodeBoolean(values, out value)
            || TryDecodeInteger(values, out value)
            || TryDecodeFloat(values, out value)
            || TryDecodeDbpos(values, out value)
            || TryDecodeQuality(values, out value)
            || TryDecodeTime(values, out value)
            || TryDecodeVisibleString(values, out value)
            || TryDecodeBitString(values, out value);
    }

    private static bool TryDecodeBoolean(List<BerTlv> values, out object? value)
    {
        var tlv = values.LastOrDefault(v => v.Tag == 0x83 && v.Length >= 1);
        if (tlv.Tag == 0x83)
        {
            value = tlv.Value.Span[0] != 0;
            return true;
        }
        value = null;
        return false;
    }

    private static bool TryDecodeInteger(List<BerTlv> values, out object? value)
    {
        var tlv = values.LastOrDefault(v => (v.Tag == 0x85 || v.Tag == 0x86) && v.Length is > 0 and <= 8);
        if (tlv.Tag == 0x85 || tlv.Tag == 0x86)
        {
            var span = tlv.Value.Span;
            long result = 0;
            foreach (var b in span) result = (result << 8) | b;
            if (tlv.Tag == 0x85 && span.Length > 0 && (span[0] & 0x80) != 0)
            {
                var bits = span.Length * 8;
                result -= 1L << bits;
            }
            value = result;
            return true;
        }
        value = null;
        return false;
    }

    private static bool TryDecodeDbpos(List<BerTlv> values, out object? value)
    {
        var tlv = values.LastOrDefault(v => v.Tag == 0x84 && v.Length >= 2);
        if (tlv.Tag == 0x84)
        {
            var span = tlv.Value.Span;
            var bytes = span[1..];
            if (bytes.Length > 0)
            {
                var code = (bytes[0] >> 6) & 0x03;
                value = code switch
                {
                    0 => "Intermediate [00]",
                    1 => "Open [01]",
                    2 => "Close [10]",
                    3 => "Invalid [11]",
                    _ => code.ToString(CultureInfo.InvariantCulture)
                };
                return true;
            }
        }
        value = null;
        return false;
    }

    private static bool TryDecodeQuality(List<BerTlv> values, out object? value)
    {
        var tlv = values.LastOrDefault(v => v.Tag == 0x84 && v.Length >= 2);
        if (tlv.Tag == 0x84)
        {
            value = MmsQualityDecoder.Decode(tlv.Value.Span);
            return true;
        }
        value = null;
        return false;
    }

    private static bool TryDecodeBitString(List<BerTlv> values, out object? value)
    {
        var tlv = values.LastOrDefault(v => v.Tag == 0x84 && v.Length >= 2);
        if (tlv.Tag == 0x84)
        {
            var span = tlv.Value.Span;
            var unusedBits = span[0];
            var bytes = span[1..].ToArray();
            value = $"BIT_STRING unused={unusedBits} data={BitConverter.ToString(bytes)}";
            return true;
        }
        value = null;
        return false;
    }

    private static bool TryDecodeFloat(List<BerTlv> values, out object? value)
    {
        var tlv = values.LastOrDefault(v => v.Tag == 0x87 && (v.Length == 5 || v.Length == 9));
        if (tlv.Tag == 0x87)
        {
            var span = tlv.Value.Span;
            if (span.Length == 5)
            {
                var raw = span[1..5].ToArray();
                if (BitConverter.IsLittleEndian) Array.Reverse(raw);
                value = BitConverter.ToSingle(raw, 0);
                return true;
            }
            if (span.Length == 9)
            {
                var raw = span[1..9].ToArray();
                if (BitConverter.IsLittleEndian) Array.Reverse(raw);
                value = BitConverter.ToDouble(raw, 0);
                return true;
            }
        }
        value = null;
        return false;
    }

    private static bool TryDecodeTime(List<BerTlv> values, out object? value)
    {
        var utc = values.LastOrDefault(v => v.Tag == 0x91 && v.Length >= 8);
        if (utc.Tag == 0x91)
        {
            value = MmsUtcTimeDecoder.Decode(utc.Value.Span);
            return true;
        }
        var binary = values.LastOrDefault(v => v.Tag == 0x8C && v.Length > 0);
        if (binary.Tag == 0x8C)
        {
            value = $"BINARY_TIME {HexDump.ToCompactString(binary.Value.ToArray())}";
            return true;
        }
        value = null;
        return false;
    }

    private static bool TryDecodeVisibleString(List<BerTlv> values, out object? value)
    {
        var tlv = values.LastOrDefault(v => v.Tag == 0x8A && v.Length > 0);
        if (tlv.Tag == 0x8A)
        {
            value = System.Text.Encoding.ASCII.GetString(tlv.Value.Span);
            return true;
        }
        value = null;
        return false;
    }

    private static long DecodeUnsigned(ReadOnlySpan<byte> span)
    {
        long result = 0;
        foreach (var b in span) result = (result << 8) | b;
        return result;
    }
}
