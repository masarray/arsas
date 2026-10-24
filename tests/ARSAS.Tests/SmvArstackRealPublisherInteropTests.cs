using System.Buffers.Binary;
using AR.Iec61850.Ethernet;
using AR.Iec61850.SampledValues;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SmvArstackRealPublisherInteropTests
{
    private static readonly SmvLiveCaptureOptions Options = new()
    {
        AdapterSelector = "fixture-only",
        NominalFrequencyHz = 50,
        Cycles = 2, MaximumChannels = 8
    };

    [Fact]
    public void Contradictory4000SamplesPerPeriod_RequiresIndependentProofThenDisplays160Samples()
    {
        // Reproduces a field publisher with 8 INT32 + 8 quality words,
        // 4000 sample/s advertised in a samples/period smpMod=0 field.
        // An 8000-point scope is implausible; never blindly allocate it.
        var catalog = new SmvLiveStreamCatalog(Options);
        var started = new DateTimeOffset(2026,10,22,0,0,0,TimeSpan.Zero);
        for (var i = 0; i < 239; i++)
        {
            catalog.Receive(Frame(i, declaredRate: 4000, mode: 0),
                started.AddTicks(i * 2500)); // 250 us @ 4000 samples/s
        }
        var first = catalog.Publish(null, started);
        var key = Assert.Single(first.Streams).Key;
        Assert.Null(first.SelectedSnapshot);
        for (var i = 239; i < 800; i++)
            catalog.Receive(Frame(i, declaredRate: 4000, mode: 0),
                started.AddTicks(i * 2500));

        var received = catalog.Publish(key, started.AddMilliseconds(200));
        var proof = Assert.IsType<SmvSnapshotResult>(received.SelectedSnapshot);
        Assert.True(proof.IsCleanProof);
        Assert.Equal(80, proof.SamplesPerCycle);
        Assert.Equal(160, proof.CapturedSamples);
        Assert.Contains("Conflicting on-wire", proof.TimebaseReason);
        Assert.Equal("ARSTACK_SV01", proof.StreamId);
    }

    [Fact]
    public void ContradictoryMetadataWithoutClockEvidence_FailsClosed()
    {
        var cat = new SmvLiveStreamCatalog(Options);
        var stamp = DateTimeOffset.UtcNow;
        for (var i = 0; i < 1000; i++)
            cat.Receive(Frame(i, declaredRate: 4000, mode: 0), stamp);
        var state = cat.Publish(null, stamp);
        Assert.Null(state.SelectedSnapshot);
        Assert.Contains("Timebase unresolved", Assert.Single(state.Streams).Diagnostic,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContradictoryMetadataDifferentPayload_RequiresFullIndependentClockEvidence()
    {
        var cat = new SmvLiveStreamCatalog(Options);
        var stamp = new DateTimeOffset(2026,10,22,0,0,0,TimeSpan.Zero);
        for (var i = 0; i < 239; i++)
            cat.Receive(Frame(i, declaredRate: 4000, mode: 0, eightChannels: false),
                stamp.AddTicks(i * 2500));
        var pending = cat.Publish(null, stamp);
        Assert.Null(pending.SelectedSnapshot);
        for (var i = 239; i < 800; i++)
            cat.Receive(Frame(i, declaredRate: 4000, mode: 0, eightChannels: false),
                stamp.AddTicks(i * 2500));
        var selectedKey = Assert.Single(cat.Publish(null, stamp).Streams).Key;
        var state = cat.Publish(selectedKey, stamp);
        var snapshot = Assert.IsType<SmvSnapshotResult>(state.SelectedSnapshot);
        Assert.Equal(2, snapshot.Channels.Count);
        Assert.Equal(160, snapshot.CapturedSamples);
        Assert.Contains("NIC-clock evidence", snapshot.TimebaseReason);
        // No guessing Ua/Ia semantics merely because the timebase was observed.
        Assert.All(snapshot.Channels, c => Assert.Contains("Raw", c.Label));
    }

    private static SampledValuesFrame Frame(int sample, ushort declaredRate, ushort mode,
        bool eightChannels = true)
    {
        var words = eightChannels ? 16 : 4;
        var bytes = new byte[words * 4];
        for (var i = 0; i < words; i += 2)
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(i * 4, 4),
                (int)(1000 * Math.Sin(sample * Math.PI / 40)));
        var asdu = new SampledValueAsdu
        {
            SvId = "ARSTACK_SV01", DataSetReference = "MU/LLN0$SV",
            SampleCount = (ushort)sample, SampleRate = declaredRate,
            SampleMode = mode, SamplePayload = bytes
        };
        return new SampledValuesFrame
        {
            Source = MacAddress.Parse("E8:F6:0A:E0:C5:3F"),
            Destination = MacAddress.Parse("01:0C:CD:04:00:01"),
            AppId = 0x4000,
            Pdu = new SampledValuesPdu { Asdus = [asdu] }
        };
    }
}
