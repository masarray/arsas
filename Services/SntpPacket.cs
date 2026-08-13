using System.Buffers.Binary;
using System.Text;

namespace ArIED61850Tester.Services;

/// <summary>
/// Minimal, auditable SNTPv4 packet codec implemented from RFC 4330 / RFC 5905 wire semantics.
/// No third-party NTP source code is used.
/// </summary>
public static class SntpPacket
{
    public const int MinimumLength = 48;
    private static readonly DateTimeOffset NtpEpoch = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static bool TryReadClientRequest(ReadOnlySpan<byte> packet, out SntpClientRequest request)
    {
        request = default;
        if (packet.Length < MinimumLength)
            return false;

        var leapIndicator = (byte)((packet[0] >> 6) & 0x03);
        var version = (byte)((packet[0] >> 3) & 0x07);
        var mode = (byte)(packet[0] & 0x07);
        if (mode != 3 || version is < 3 or > 4)
            return false;

        request = new SntpClientRequest(
            Version: version,
            PollExponent: unchecked((sbyte)packet[2]),
            LeapIndicator: leapIndicator,
            TransmitTimestampRaw: packet.Slice(40, 8).ToArray());
        return true;
    }

    public static byte[] BuildServerReply(
        ReadOnlySpan<byte> requestPacket,
        DateTimeOffset receiveUtc,
        DateTimeOffset transmitUtc,
        SntpServerProfile profile,
        bool synchronized = true)
    {
        if (!TryReadClientRequest(requestPacket, out var request))
            throw new ArgumentException("Packet is not a supported SNTP client request.", nameof(requestPacket));

        var response = new byte[MinimumLength];
        var version = Math.Min(request.Version, (byte)4);
        var leap = synchronized ? profile.LeapIndicator : (byte)3;
        response[0] = (byte)((leap << 6) | (version << 3) | 4);
        response[1] = synchronized ? profile.Stratum : (byte)16;
        response[2] = unchecked((byte)profile.PollExponent);
        response[3] = unchecked((byte)profile.PrecisionExponent);

        WriteSignedFixed16_16(response.AsSpan(4, 4), profile.RootDelay);
        WriteUnsignedFixed16_16(response.AsSpan(8, 4), profile.RootDispersion);
        WriteReferenceId(response.AsSpan(12, 4), synchronized ? profile.ReferenceId : "INIT");
        WriteTimestamp(response.AsSpan(16, 8), profile.ReferenceUtc == default ? transmitUtc : profile.ReferenceUtc);

        request.TransmitTimestampRaw.CopyTo(response, 24);
        WriteTimestamp(response.AsSpan(32, 8), receiveUtc);
        WriteTimestamp(response.AsSpan(40, 8), transmitUtc);
        return response;
    }

    public static byte[] BuildBroadcast(
        DateTimeOffset transmitUtc,
        SntpServerProfile profile,
        bool synchronized = true)
    {
        var response = new byte[MinimumLength];
        var leap = synchronized ? profile.LeapIndicator : (byte)3;
        response[0] = (byte)((leap << 6) | (4 << 3) | 5);
        response[1] = synchronized ? profile.Stratum : (byte)16;
        response[2] = unchecked((byte)profile.PollExponent);
        response[3] = unchecked((byte)profile.PrecisionExponent);

        WriteSignedFixed16_16(response.AsSpan(4, 4), profile.RootDelay);
        WriteUnsignedFixed16_16(response.AsSpan(8, 4), profile.RootDispersion);
        WriteReferenceId(response.AsSpan(12, 4), synchronized ? profile.ReferenceId : "INIT");
        WriteTimestamp(response.AsSpan(16, 8), profile.ReferenceUtc == default ? transmitUtc : profile.ReferenceUtc);
        WriteTimestamp(response.AsSpan(40, 8), transmitUtc);
        return response;
    }

    public static DateTimeOffset ReadTimestamp(ReadOnlySpan<byte> timestamp, DateTimeOffset? eraHint = null)
    {
        if (timestamp.Length < 8)
            throw new ArgumentException("NTP timestamp requires 8 bytes.", nameof(timestamp));

        var seconds32 = BinaryPrimitives.ReadUInt32BigEndian(timestamp[..4]);
        var fraction = BinaryPrimitives.ReadUInt32BigEndian(timestamp.Slice(4, 4));

        // Era 0 covers 1900-2036. Around the 2036 rollover, choose the era nearest the hint.
        var hint = eraHint ?? DateTimeOffset.UtcNow;
        var hintSeconds = (hint - NtpEpoch).Ticks / TimeSpan.TicksPerSecond;
        var era = Math.Max(0L, (hintSeconds + (1L << 31)) >> 32);
        var seconds = (era << 32) | seconds32;
        if (era > 0)
        {
            var previous = seconds - (1L << 32);
            if (Math.Abs(previous - hintSeconds) < Math.Abs(seconds - hintSeconds))
                seconds = previous;
        }

        var fractionalTicks = (long)((fraction * (ulong)TimeSpan.TicksPerSecond) >> 32);
        return NtpEpoch.AddTicks(seconds * TimeSpan.TicksPerSecond + fractionalTicks);
    }

    public static void WriteTimestamp(Span<byte> destination, DateTimeOffset utc)
    {
        if (destination.Length < 8)
            throw new ArgumentException("NTP timestamp requires 8 bytes.", nameof(destination));

        utc = utc.ToUniversalTime();
        var ticksSinceEpoch = (utc - NtpEpoch).Ticks;
        if (ticksSinceEpoch < 0)
            throw new ArgumentOutOfRangeException(nameof(utc), "SNTP timestamp predates 1900-01-01 UTC.");

        var seconds = (ulong)(ticksSinceEpoch / TimeSpan.TicksPerSecond);
        var remainderTicks = (ulong)(ticksSinceEpoch % TimeSpan.TicksPerSecond);
        var fraction = (uint)((remainderTicks << 32) / (ulong)TimeSpan.TicksPerSecond);

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], unchecked((uint)seconds));
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4, 4), fraction);
    }

    private static void WriteReferenceId(Span<byte> destination, string referenceId)
    {
        destination.Clear();
        var bytes = Encoding.ASCII.GetBytes(string.IsNullOrWhiteSpace(referenceId) ? "LOCL" : referenceId.Trim());
        bytes.AsSpan(0, Math.Min(4, bytes.Length)).CopyTo(destination);
    }

    private static void WriteSignedFixed16_16(Span<byte> destination, TimeSpan value)
    {
        var seconds = value.TotalSeconds;
        var scaled = (long)Math.Round(seconds * 65536d, MidpointRounding.AwayFromZero);
        scaled = Math.Clamp(scaled, int.MinValue, int.MaxValue);
        BinaryPrimitives.WriteInt32BigEndian(destination, (int)scaled);
    }

    private static void WriteUnsignedFixed16_16(Span<byte> destination, TimeSpan value)
    {
        var seconds = Math.Max(0d, value.TotalSeconds);
        var scaled = (ulong)Math.Round(seconds * 65536d, MidpointRounding.AwayFromZero);
        BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)Math.Min(scaled, uint.MaxValue));
    }
}

public readonly record struct SntpClientRequest(
    byte Version,
    sbyte PollExponent,
    byte LeapIndicator,
    byte[] TransmitTimestampRaw);

public sealed record SntpServerProfile
{
    /// <summary>
    /// ARSAS intentionally advertises the Windows clock as a low-priority local reference.
    /// This avoids pretending that the laptop is a GPS/PTP grandmaster.
    /// </summary>
    public byte Stratum { get; init; } = 15;
    public byte LeapIndicator { get; init; }
    public sbyte PollExponent { get; init; } = 4;
    public sbyte PrecisionExponent { get; init; } = -20;
    public TimeSpan RootDelay { get; init; } = TimeSpan.Zero;
    public TimeSpan RootDispersion { get; init; } = TimeSpan.FromMilliseconds(20);
    public string ReferenceId { get; init; } = "LOCL";
    public DateTimeOffset ReferenceUtc { get; init; }
}
