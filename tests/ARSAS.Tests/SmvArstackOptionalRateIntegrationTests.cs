using System.Buffers.Binary;
using AR.Iec61850.Ethernet;
using AR.Iec61850.SampledValues;
using ArIED61850Tester.Services;
using ArIED61850Tester.Views;

namespace ARSAS.Tests;

/// <summary>
/// Deterministic REAL-WIRE-SHAPED regression: 4I+4V with each instMag/q pair,
/// optional smpRate and smpMod omitted. A simulator fixture, not a field PCAP.
/// </summary>
public sealed class SmvArstackOptionalRateIntegrationTests
{
    private static readonly SmvLiveCaptureOptions Options = new()
    {
        AdapterSelector = "synthetic-64-byte-4I4V",
        NominalFrequencyHz = 50,
        MaximumChannels = 8,
        Cycles = 2
    };

    [Fact]
    public void OptionalTimebase_ContinuousRealShapedSamples_CreateTwoCyclesAndPhasors()
    {
        var catalog = new SmvLiveStreamCatalog(Options);
        var start = new DateTimeOffset(2026, 10, 22, 1, 0, 0, TimeSpan.Zero);
        for (var n = 0; n < 950; n++)
        {
            catalog.Receive(Frame((ushort)n, n),
                start.AddTicks(n * 2500L));
        }

        var detected = Assert.Single(catalog.Publish(null, start).Streams);
        Assert.True(detected.HasCoherentWindow);
        Assert.Equal(0, detected.Gaps);
        var snapshot = Assert.IsType<SmvSnapshotResult>(
            catalog.Publish(detected.Key, start).SelectedSnapshot);
        Assert.True(snapshot.IsCleanProof);
        Assert.Equal(80, snapshot.SamplesPerCycle);
        Assert.Equal(160, snapshot.CapturedSamples);
        Assert.Equal(8, snapshot.Channels.Count);
        Assert.Null(snapshot.DeclaredSampleRate);
        Assert.Null(snapshot.DeclaredSampleMode);
        Assert.Contains("capture rate", snapshot.TimebaseReason);
        Assert.NotEmpty(SmvDigSubPresentationAdapter.BuildWaveform(snapshot).VoltageSeries);
        Assert.True(SmvRawVectorMath.Analyze(snapshot).Count == 8);
    }

    [Fact]
    public void ObservedModulo4000Wrap_DoesNotDiscardContinuousEvidence()
    {
        var catalog = new SmvLiveStreamCatalog(Options);
        var start = new DateTimeOffset(2026, 10, 22, 1, 0, 0, TimeSpan.Zero);
        for (var n = 0; n < 1150; n++)
            catalog.Receive(Frame((ushort)((3500 + n) % 4000), n),
                start.AddTicks(n * 2500L));

        var detected = Assert.Single(catalog.Publish(null, start).Streams);
        Assert.True(detected.HasCoherentWindow);
        Assert.Equal(0, detected.Gaps);
        Assert.Equal(0, detected.OutOfOrder);
        var proof = catalog.Publish(detected.Key, start).SelectedSnapshot;
        Assert.NotNull(proof);
        Assert.True(proof.IsCleanProof);
        Assert.Equal(160, proof.TargetSamples);
    }

    [Fact]
    public void ObservedCaptureRateMismatch_WillNotMakeFalseWaveform()
    {
        var catalog = new SmvLiveStreamCatalog(Options);
        var start = new DateTimeOffset(2026, 10, 22, 1, 0, 0, TimeSpan.Zero);
        for (var n = 0; n < 1000; n++)
            catalog.Receive(Frame((ushort)n, n),
                start.AddTicks(n * 4000L)); // 2500 Hz, not observed 50-Hz × 80
        var detected = Assert.Single(catalog.Publish(null, start).Streams);
        Assert.False(detected.HasCoherentWindow);
        Assert.Null(catalog.Publish(detected.Key, start).SelectedSnapshot);
    }

    private static SampledValuesFrame Frame(ushort counter, int ordinal)
    {
        var payload = new byte[64];
        for (var channel = 0; channel < 8; channel++)
        {
            var phase = (channel % 4) * Math.PI / 2;
            var instant = (int)(1200 * Math.Sin(2 * Math.PI * ordinal / 80.0 + phase));
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(channel * 8, 4), instant);
            // quality word at +4 is zero; exactly as common 9-2LE instMag/q shape
        }
        return new SampledValuesFrame
        {
            Source = MacAddress.Parse("02:11:22:33:44:55"),
            Destination = MacAddress.Parse("01:0C:CD:04:00:01"),
            AppId = 0x4000,
            Pdu = new SampledValuesPdu
            {
                Asdus =
                [
                    new SampledValueAsdu
                    {
                        SvId = "ARSTACK_SV01",
                        DataSetReference = "ARSTACKLD0/LLN0$PhsMeas1",
                        SampleCount = counter,
                        ConfigurationRevision = 1,
                        SamplePayload = payload,
                        SampleRate = null,
                        SampleMode = null
                    }
                ]
            }
        };
    }
}
