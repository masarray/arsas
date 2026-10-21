using System.Buffers.Binary;
using AR.Iec61850.Ethernet;
using AR.Iec61850.SampledValues;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SmvLiveCaptureTests
{
    private static readonly SmvLiveCaptureOptions Options = new()
    {
        AdapterSelector = "fixture-only", NominalFrequencyHz = 50,
        Cycles = 2, MaximumChannels = 8, MaximumStreams = 2
    };

    [Fact]
    public void CompleteCoherentWindow_IsSelectedByStrongPublisherIdentity()
    {
        var catalog = new SmvLiveStreamCatalog(Options);
        for (var i = 0; i < 160; i++)
            catalog.Receive(Frame("MU01", 0x4000, i, (int)(1000 * Math.Cos(2 * Math.PI * i / 80.0))),
                DateTimeOffset.UtcNow);

        var summary = Assert.Single(catalog.Publish(null, DateTimeOffset.UtcNow).Streams);
        var update = catalog.Publish(summary.Key, DateTimeOffset.UtcNow);
        var proof = Assert.IsType<SmvSnapshotResult>(update.SelectedSnapshot);
        Assert.Equal("MU01", proof.StreamId);
        Assert.Equal(160, proof.CapturedSamples);
        Assert.True(proof.IsCleanProof);
        Assert.Equal(2, proof.CycleCount);
        Assert.InRange(SmvRawVectorMath.Analyze(proof)[0].FundamentalRms, 705, 709);
    }

    [Fact]
    public void GapAndDuplicate_ClearOldWaveformInsteadOfMixingTwoWindows()
    {
        var stream = new SmvLiveStreamCatalog(Options);
        for (var i = 0; i < 160; i++)
            stream.Receive(Frame("MU01", 0x4000, i, i), DateTimeOffset.UtcNow);
        var key = Assert.Single(stream.Publish(null, DateTimeOffset.UtcNow).Streams).Key;
        stream.Receive(Frame("MU01", 0x4000, 165, 165), DateTimeOffset.UtcNow);
        var afterGap = stream.Publish(key, DateTimeOffset.UtcNow);
        Assert.Null(afterGap.SelectedSnapshot);
        Assert.Equal(1, Assert.Single(afterGap.Streams).Gaps);
        stream.Receive(Frame("MU01", 0x4000, 165, 165), DateTimeOffset.UtcNow);
        var afterDuplicate = stream.Publish(key, DateTimeOffset.UtcNow);
        Assert.Null(afterDuplicate.SelectedSnapshot);
        Assert.Equal(1, Assert.Single(afterDuplicate.Streams).Duplicates);
    }

    [Fact]
    public void MultiplePublishersRemainIsolatedAndCatalogEvictsAtFixedLimit()
    {
        var catalog = new SmvLiveStreamCatalog(Options);
        catalog.Receive(Frame("A", 0x4000, 0, 10), DateTimeOffset.UtcNow);
        catalog.Receive(Frame("B", 0x4001, 0, 20), DateTimeOffset.UtcNow);
        Assert.Equal(2, catalog.StreamCount);
        catalog.Receive(Frame("C", 0x4002, 0, 30), DateTimeOffset.UtcNow);
        Assert.Equal(2, catalog.StreamCount);
        var summaries = catalog.Publish(null, DateTimeOffset.UtcNow).Streams;
        Assert.Contains(summaries, row => row.SvId == "C");
        Assert.All(summaries, row => Assert.True(row.Frames > 0));
    }

    [Fact]
    public void IdenticalAppIdAndSvId_FromDifferentEthernetSourcesStayIndependent()
    {
        var catalog = new SmvLiveStreamCatalog(Options);
        for (var i = 0; i < 160; i++)
        {
            catalog.Receive(Frame("MU_COLLIDE", 0x4000, i, i,
                source: "02:00:00:00:00:01"), DateTimeOffset.UtcNow);
            catalog.Receive(Frame("MU_COLLIDE", 0x4000, i, -i,
                source: "02:00:00:00:00:02"), DateTimeOffset.UtcNow);
        }
        var streams = catalog.Publish(null, DateTimeOffset.UtcNow).Streams;
        Assert.Equal(2, streams.Count);
        Assert.NotEqual(streams[0].Key, streams[1].Key);
        var a = Assert.IsType<SmvSnapshotResult>(
            catalog.Publish(streams[0].Key, DateTimeOffset.UtcNow).SelectedSnapshot);
        var b = Assert.IsType<SmvSnapshotResult>(
            catalog.Publish(streams[1].Key, DateTimeOffset.UtcNow).SelectedSnapshot);
        Assert.NotEqual(a.SourceMac, b.SourceMac);
        Assert.Equal(-a.Channels[0].Samples[80], b.Channels[0].Samples[80]);
    }

    [Fact]
    public void UnresolvedTimebaseNeverProducesSyntheticSamples()
    {
        var catalog = new SmvLiveStreamCatalog(Options);
        var frame = Frame("UNKNOWN", 0x4000, 0, 99, declaredRate: false);
        for (var i = 0; i < 160; i++)
        {
            var asdu = frame.Pdu.Asdus[0];
            catalog.Receive(Frame("UNKNOWN", 0x4000, i, 99, declaredRate: false), DateTimeOffset.UtcNow);
        }
        var update = catalog.Publish(null, DateTimeOffset.UtcNow);
        Assert.Null(update.SelectedSnapshot);
        Assert.Contains("Timebase", Assert.Single(update.Streams).Diagnostic);
    }

    [Fact]
    public void UiUsesExplicitLiveStartAndOriginalSnapshotProofPath()
    {
        var xaml = ReadFile("SmvViewerWindow.xaml");
        var code = ReadFile("SmvViewerWindow.Live.cs");
        Assert.Contains("StartLive_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("StopLive_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("Capture 2 cycles", xaml, StringComparison.Ordinal);
        Assert.Contains("Volatile.Read(ref _liveSelectedKey)", code, StringComparison.Ordinal);
        Assert.Contains("SelectedStream.LiveKey", code, StringComparison.Ordinal);
    }

    private static SampledValuesFrame Frame(string svId, ushort appId, int counter, int value, bool declaredRate = true,
        string source = "02:00:00:00:00:01")
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, 4), value);
        var asdu = new SampledValueAsdu
        {
            SvId = svId, DataSetReference = "LD0/LLN0$DS", SampleCount = (ushort)counter,
            SampleRate = declaredRate ? (ushort)80 : null,
            SampleMode = declaredRate ? (ushort)0 : null,
            SamplePayload = bytes
        };
        return new SampledValuesFrame
        {
            Source = MacAddress.Parse(source),
            Destination = MacAddress.Parse("01:0C:CD:04:00:01"),
            AppId = appId,
            Pdu = new SampledValuesPdu { Asdus = [asdu] }
        };
    }

    private static string ReadFile(string path)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var test = Path.Combine(directory.FullName, path);
            if (File.Exists(test)) return File.ReadAllText(test);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
