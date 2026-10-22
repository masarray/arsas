using System.Buffers.Binary;
using AR.Iec61850.SampledValues;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// Packet prefixes, SV BER, payload and trailing tag 0x89 come from real
/// ARStack ESP32-P4 Ethernet PCAP ARStack_SV_20261022_020528_781.pcap.
/// The live replay below varies smpCnt/timestamps/values deterministically
/// while preserving exactly the captured 802.1Q + 9-2 BER wire shape.
/// This is not an ARSVIN/KM loopback encoder test.
/// </summary>
public sealed class SmvArstackCapturedEthernetInteropTests
{
    // Exact 138-byte real frame, smpCnt=0, initial smpSynch=0.
    private const string CapturedSourceAcquisitionFrame =
        "010ccd040001e8f60ae0c53f8100800088ba4000007800000000606e800101a2693067800c4152535441434b5f53563031820200008304000000018501008740ffffff7c00000000fffffa2600000000000005850000000000000000000000000000000000000000ffffe3510000000000000e5a0000000000000000000000008908e8f60afffee0c53f";

    // Exact 138-byte real frame after SOURCE phase ACQUIRED, smpSynch=2.
    private const string CapturedGloballySynchronizedFrame =
        "010ccd040001e8f60ae0c53f8100800088ba4000007800000000606e800101a2693067800c4152535441434b5f53563031820200008304000000018501028740ffffff7c00000000fffffa260000000000000585000000000000000000000000fffffffa00000000ffffe3510000000000000e5a0000000000000000000000008908e8f60afffee0c53f";

    private static readonly SmvLiveCaptureOptions Settings = new()
    {
        AdapterSelector = "captured-arstack-8021q-pcp4-vid0",
        NominalFrequencyHz = 50,
        Cycles = 2,
        MaximumChannels = 8
    };

    [Theory]
    [InlineData(false, (byte)0)]
    [InlineData(true, (byte)2)]
    public void CapturedArstackRawEthernet_DecodesPriorityVlanGmIdAndOptionalMetadata(
        bool acquired, byte expectedSync)
    {
        var bytes = Convert.FromHexString(acquired
            ? CapturedGloballySynchronizedFrame : CapturedSourceAcquisitionFrame);
        Assert.Equal(138, bytes.Length);
        Assert.Equal(0x8100, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(12, 2)));
        Assert.Equal(0x8000, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(14, 2)));
        Assert.Equal(0x88BA, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(16, 2)));
        Assert.Equal("8908e8f60afffee0c53f",
            Convert.ToHexString(bytes.AsSpan(bytes.Length - 10)).ToLowerInvariant());

        Assert.True(SampledValuesFrameParser.TryParseEthernetFrame(bytes, out var frame));
        Assert.Equal(0x4000, frame.AppId);
        Assert.Equal("0", frame.Vlan?.VlanId.ToString());
        var asdu = Assert.Single(frame.Pdu.Asdus);
        Assert.Equal("ARSTACK_SV01", asdu.SvId);
        Assert.Equal((ushort)0, asdu.SampleCount);
        Assert.Equal(expectedSync, asdu.SampleSynchronization);
        Assert.Equal(64, asdu.SamplePayload.Length);
        Assert.Null(asdu.SampleRate);
        Assert.Null(asdu.SampleMode);
    }

    [Fact]
    public void CapturedArstackWireShape_AutoSync0To2_DoesNotEraseWaveformOrTimebase()
    {
        var raw = Convert.FromHexString(CapturedSourceAcquisitionFrame);
        var catalog = new SmvLiveStreamCatalog(Settings);
        var start = DateTimeOffset.FromUnixTimeSeconds(1792634731);
        string? streamKey = null;
        long snapshotsBeforeLock = 0;

        for (var n = 0; n < 1200; n++)
        {
            // Modify only the captured smpCnt, smpSynch, and payload instMag.
            // Every other byte, including actual 802.1Q, BER and gmIdentity,
            // is preserved from the captured ARStack ESP32-P4 frame.
            BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(51, 2),
                (ushort)(n % 4000));
            raw[61] = n >= 600 ? (byte)2 : (byte)0;
            for (var channel = 0; channel < 8; channel++)
                BinaryPrimitives.WriteInt32BigEndian(
                    raw.AsSpan(64 + 8 * channel, 4),
                    (int)(2000 * Math.Sin(2 * Math.PI * n / 80.0 +
                                           channel * Math.PI / 4)));

            Assert.True(SampledValuesFrameParser.TryParseEthernetFrame(raw, out var frame));
            // 4 kHz observed capture with bounded +-60us NIC arrival jitter.
            // Never assume the counter rate merely from operator frequency.
            var jitter = ((n * 37) % 1200) - 600;
            var timestamp = start.AddTicks(n * 2500L + jitter);
            catalog.Receive(frame, timestamp);
            if (n == 599)
            {
                var before = catalog.Publish(null, timestamp);
                streamKey = Assert.Single(before.Streams).Key;
                Assert.True(Assert.Single(before.Streams).HasCoherentWindow);
                Assert.NotNull(before.SelectedSnapshot);
                snapshotsBeforeLock++;
            }
        }

        Assert.Equal(1, snapshotsBeforeLock);
        var result = catalog.Publish(streamKey, start.AddMilliseconds(300));
        var summary = Assert.Single(result.Streams);
        Assert.Equal(1200, summary.Frames);
        Assert.Equal(1200, summary.Asdus);
        Assert.Equal(0, summary.Gaps);
        Assert.Equal(0, summary.Missing);
        Assert.Equal(0, summary.Duplicates);
        Assert.Equal(0, summary.OutOfOrder);
        Assert.Equal(0, summary.Restarts); // smpSynch transition is NOT restart
        Assert.True(summary.HasCoherentWindow);
        Assert.Equal("0", summary.Vlan);

        var proof = Assert.IsType<SmvSnapshotResult>(result.SelectedSnapshot);
        Assert.Equal("ARSTACK_SV01", proof.StreamId);
        Assert.Equal(0x4000, proof.AppId);
        Assert.Equal(80, proof.SamplesPerCycle);
        Assert.Equal(160, proof.CapturedSamples);
        Assert.Equal(8, proof.Channels.Count);
        Assert.Equal((byte)2, proof.SampleSynchronization);
        Assert.Null(proof.DeclaredSampleRate);
        Assert.Null(proof.DeclaredSampleMode);
        Assert.True(proof.IsCleanProof);
        Assert.Contains("capture rate", proof.TimebaseReason);
        Assert.True(proof.Channels[0].Maximum > proof.Channels[0].Minimum);
    }
}
