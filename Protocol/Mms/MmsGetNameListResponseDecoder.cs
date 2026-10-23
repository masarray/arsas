using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ArIED61850Tester.Protocol.Asn1;
using ArIED61850Tester.Protocol.Diagnostics;

namespace ArIED61850Tester.Protocol.Mms;

public static class MmsGetNameListResponseDecoder
{
    public static MmsNameListResult Decode(byte[] presentationPayload, int expectedInvokeId)
    {
        var hex = HexDump.ToCompactString(presentationPayload);
        try
        {
            var mms = StripPresentationPrefix(presentationPayload);
            if (mms.Length == 0)
                return Fail("Empty MMS GetNameList response payload.", hex);

            if (mms[0] == 0xA2)
                return Fail($"MMS Confirmed-Error PDU during GetNameList: {HexDump.ToCompactString(mms)}", hex);
            if (mms[0] == 0xA3 || mms[0] == 0xA4)
                return Fail($"MMS Reject/Abort PDU during GetNameList: {HexDump.ToCompactString(mms)}", hex);
            if (mms[0] != 0xA1)
                return Fail($"Expected MMS Confirmed-Response PDU [1] (0xA1), received 0x{mms[0]:X2}.", hex);

            var outer = new BerReader(mms).ReadTlv();
            var reader = new BerReader(outer.Value);
            if (reader.EndOfBuffer) return Fail("MMS Confirmed-Response PDU is empty.", hex);

            var invoke = reader.ReadTlv();
            if (invoke.Tag != 0x02)
                return Fail($"MMS GetNameList response did not start with invokeID. First inner tag=0x{invoke.Tag:X2}.", hex);

            var actualInvoke = DecodeUnsigned(invoke.Value.Span);
            if (actualInvoke != expectedInvokeId)
                return Fail($"MMS GetNameList invokeID mismatch. Expected {expectedInvokeId}, received {actualInvoke}.", hex);

            if (reader.EndOfBuffer) return Fail("MMS GetNameList response has no service response node.", hex);
            var service = reader.ReadTlv();
            if (service.Tag != 0xA1)
                return Fail($"Expected MMS GetNameList service response [1] (0xA1), received 0x{service.Tag:X2}.", hex);

            var names = new List<string>();
            var moreFollows = true;
            var moreFollowsWasPresent = false;
            var serviceReader = new BerReader(service.Value);
            while (!serviceReader.EndOfBuffer)
            {
                var field = serviceReader.ReadTlv();
                if (field.Tag == 0xA0)
                {
                    var listReader = new BerReader(field.Value);
                    while (!listReader.EndOfBuffer)
                    {
                        var id = listReader.ReadTlv();
                        if (IsIdentifierTag(id.Tag))
                            names.Add(Encoding.ASCII.GetString(id.Value.Span));
                        else if ((id.Tag & 0x20) == 0x20 || id.Tag == 0x30)
                            names.AddRange(ExtractIdentifierStrings(id.Value));
                    }
                }
                else if (field.Tag == 0x81 && field.Length > 0)
                {
                    moreFollowsWasPresent = true;
                    moreFollows = field.Value.Span[0] != 0;
                }
            }

            return new MmsNameListResult
            {
                IsSuccess = true,
                Names = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                MoreFollows = moreFollows,
                MoreFollowsWasPresent = moreFollowsWasPresent,
                Message = $"MMS GetNameList decoded {names.Count} name(s), moreFollows={moreFollows}, moreFollowsPresent={moreFollowsWasPresent}.",
                ResponseHexPreview = hex
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            return Fail($"MMS GetNameList response decode failed: {ex.GetType().Name}: {ex.Message}", hex);
        }
    }

    private static MmsNameListResult Fail(string message, string hex) => new()
    {
        IsSuccess = false,
        Names = Array.Empty<string>(),
        MoreFollows = false,
        Message = message,
        ResponseHexPreview = hex
    };

    private static byte[] StripPresentationPrefix(byte[] payload)
    {
        if (payload.Length == 0) return payload;

        if (payload.Length > 5 && payload[0] == 0x01 && payload[1] == 0x00 && payload[2] == 0x01 && payload[3] == 0x00 && payload[4] == 0x61)
        {
            if (TryExtractMmsFromFullyEncodedData(payload.AsMemory(4), out var mms)) return mms;
        }

        if (payload.Length > 3 && payload[0] == 0x01 && payload[1] == 0x00 && payload[2] == 0x61)
        {
            if (TryExtractMmsFromFullyEncodedData(payload.AsMemory(2), out var mms)) return mms;
        }

        if (payload[0] == 0x61 && TryExtractMmsFromFullyEncodedData(payload, out var directMms))
            return directMms;

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

    private static bool IsIdentifierTag(byte tag)
        => tag is 0x1A or 0x16 or 0x0C or 0x19 or 0x1B;

    private static IEnumerable<string> ExtractIdentifierStrings(ReadOnlyMemory<byte> value)
    {
        var reader = new BerReader(value);
        while (!reader.EndOfBuffer)
        {
            var field = reader.ReadTlv();
            if (IsIdentifierTag(field.Tag))
            {
                var text = Encoding.ASCII.GetString(field.Value.Span);
                if (!string.IsNullOrWhiteSpace(text)) yield return text;
            }
            else if ((field.Tag & 0x20) == 0x20 || field.Tag == 0x30)
            {
                foreach (var nested in ExtractIdentifierStrings(field.Value))
                    yield return nested;
            }
        }
    }

    private static long DecodeUnsigned(ReadOnlySpan<byte> span)
    {
        long result = 0;
        foreach (var b in span) result = (result << 8) | b;
        return result;
    }
}
