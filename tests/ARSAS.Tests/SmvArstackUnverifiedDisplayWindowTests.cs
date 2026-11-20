using System.Buffers.Binary;
using AR.Iec61850.Ethernet;
using AR.Iec61850.SampledValues;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SmvArstackUnverifiedDisplayWindowTests
{
    private static SampledValuesFrame Frame(int counter) {
        var bytes = new byte[64];
        for(var channel=0;channel<8;channel++)
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(channel*8,4),
                (int)(1000*Math.Sin(2*Math.PI*counter/80.0)));
        return new SampledValuesFrame {
            Source=MacAddress.Parse("E8:F6:0A:E0:C5:3F"),
            Destination=MacAddress.Parse("01:0C:CD:04:00:01"),
            AppId=0x4000,
            Pdu=new SampledValuesPdu { Asdus=[
                new SampledValueAsdu {
                    SvId="ARSTACK_SV01", DataSetReference="MU/LLN0$PhsMeas1",
                    SampleCount=(ushort)counter,SamplePayload=bytes,
                    SampleRate=null,SampleMode=null
                }
            ]}
        };
    }

    [Fact]
    public void UnreliableFrozenNicTimestamps_ExplicitDisplayOnly80StillRendersRealCounterCoherentSamples()
    {
        var t=DateTimeOffset.UtcNow;
        var options=new SmvLiveCaptureOptions {
            AdapterSelector="fixture", NominalFrequencyHz=50,
            Cycles=2,DisplaySamplesPerCycleOverride=80
        };
        var cat=new SmvLiveStreamCatalog(options);
        for(var i=0;i<340;i++)
            cat.Receive(Frame(i),t);
        var identity=Assert.Single(cat.Publish(null,t).Streams);
        var snapshot=Assert.IsType<SmvSnapshotResult>(
            cat.Publish(identity.Key,t).SelectedSnapshot);
        Assert.True(snapshot.IsCleanProof);
        Assert.Equal(80,snapshot.SamplesPerCycle);
        Assert.Equal(160,snapshot.CapturedSamples);
        Assert.Contains("ASSUMED",snapshot.TimebaseReason);
        Assert.Contains("NOT verified",snapshot.TimebaseReason,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoOperatorOverride_FrozenClockStillFailsClosed()
    {
        var t=DateTimeOffset.UtcNow;
        var cat=new SmvLiveStreamCatalog(new SmvLiveCaptureOptions { AdapterSelector="fixture" });
        for(var i=0;i<340;i++)
            cat.Receive(Frame(i),t);
        var selected=Assert.Single(cat.Publish(null,t).Streams);
        Assert.Null(cat.Publish(selected.Key,t).SelectedSnapshot);
    }

    [Fact]
    public void OperatorOverride_StillRejectsGappedCounterWindows()
    {
        var t=DateTimeOffset.UtcNow;
        var cat=new SmvLiveStreamCatalog(new SmvLiveCaptureOptions {
            AdapterSelector="fixture",DisplaySamplesPerCycleOverride=80
        });
        for(var i=0;i<100;i++) cat.Receive(Frame(i),t);
        for(var i=105;i<220;i++) cat.Receive(Frame(i),t);
        var selected=Assert.Single(cat.Publish(null,t).Streams);
        Assert.Null(cat.Publish(selected.Key,t).SelectedSnapshot);
        Assert.True(selected.Gaps>0);
    }
}
