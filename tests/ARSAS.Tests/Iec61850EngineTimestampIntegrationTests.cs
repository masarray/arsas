using System.Buffers.Binary;
using System.Globalization;
using AR.Iec61850.Binding;
using AR.Iec61850.Mms;
using ArIED61850Tester;

namespace ARSAS.Tests;

public sealed class Iec61850EngineTimestampIntegrationTests
{
    [Fact]
    public void PinnedEngine_Preserves_2006000_Fraction_And_Arsas_Rounds_It_To_201ms()
    {
        var seconds = new DateTimeOffset(2026, 8, 13, 10, 0, 31, TimeSpan.Zero).ToUnixTimeSeconds();
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(bytes[..4], checked((uint)seconds));

        // IEC 61850 fractional-second bytes from the engine regression case:
        // 0x335A86 / 2^24 = 0.200600028... s -> 31.2006000 at .NET tick resolution.
        bytes[4] = 0x33;
        bytes[5] = 0x5A;
        bytes[6] = 0x86;
        bytes[7] = 0x00;

        var utc = Iec61850UtcTime.FromBytes(bytes);
        var decoded = Iec61850TimestampDecoder.Decode(MmsDataValue.UtcTime(utc));

        Assert.True(decoded.IsDecoded);
        Assert.True(decoded.DisplayTime.EndsWith("31.2006000", StringComparison.Ordinal), decoded.DisplayTime);

        Assert.True(
            DateTime.TryParseExact(
                decoded.DisplayTime,
                "yyyy-MM-dd HH:mm:ss.fffffff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed),
            decoded.DisplayTime);

        Assert.Equal(
            "31.201",
            Iec61850TimestampPresentation.FormatMilliseconds(parsed, "ss.fff"));
    }
}
