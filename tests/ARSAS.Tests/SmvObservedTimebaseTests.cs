using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SmvObservedTimebaseTests
{
    [Theory]
    [InlineData(50, 4000, 80)]
    [InlineData(60, 4800, 80)]
    [InlineData(50, 12800, 256)]
    [InlineData(60, 15360, 256)]
    public void CounterAndIndependentClock_ResolveOptionalMetadata(int hz, int rate, int spc)
    {
        var detector = new SmvObservedTimebaseDetector(hz);
        var started = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        AR.Iec61850.SampledValues.Measurements.SvTimebaseResolution? resolved = null;
        for (var n = 0; n < rate / 4; n++)
        {
            resolved = detector.Observe((ushort)n, started.AddTicks((long)n * TimeSpan.TicksPerSecond / rate));
            if (resolved is not null) break;
        }
        Assert.NotNull(resolved);
        Assert.Equal(spc, resolved.SamplesPerCycle);
        Assert.Equal(hz, resolved.NominalFrequencyHz);
        Assert.Contains("contiguous", resolved.Reason);
    }

    [Fact]
    public void UnrelatedRateOrFrozenNpcapTimestamp_CannotInventPhasors()
    {
        var d = new SmvObservedTimebaseDetector(50);
        var t = DateTimeOffset.UtcNow;
        for (var n = 0; n < 1000; n++)
            Assert.Null(d.Observe((ushort)n, t));
        d.Reset();
        for (var n = 0; n < 1000; n++)
            Assert.Null(d.Observe((ushort)n, t.AddTicks(n * TimeSpan.TicksPerSecond / 2500)));
    }

    [Fact]
    public void MissingAndDuplicateSamples_RestartObservation()
    {
        var d = new SmvObservedTimebaseDetector(50);
        var t = DateTimeOffset.UtcNow;
        for (var n = 0; n < 300; n++)
            d.Observe((ushort)n, t.AddTicks(n * 2500));
        Assert.Null(d.Observe(308, t.AddTicks(300 * 2500)));
        for (var n = 309; n < 380; n++)
            Assert.Null(d.Observe((ushort)n, t.AddTicks(n * 2500)));
    }

    [Fact]
    public void KnownCounterWrap_IsLearnedFromActualEdgeOnly()
    {
        Assert.True(SmvObservedTimebaseDetector.IsSupportedWrap(3999, 80, 50));
        Assert.True(SmvObservedTimebaseDetector.IsSupportedWrap(4799, 80, 60));
        Assert.False(SmvObservedTimebaseDetector.IsSupportedWrap(65535, 80, 50));
        Assert.False(SmvObservedTimebaseDetector.IsSupportedWrap(3999, 80, 60));
    }


    [Fact]
    public void RealPublisher_ConflictingDeclaredTimebase_StillObservesIndependentClockForAnyPayload()
    {
        var runtime = Read("Services/SmvLiveCaptureService.cs");
        Assert.Contains("invalidDeclaredTimebase ||", runtime);
        Assert.DoesNotContain("fixedLegacyPayload && invalidDeclaredTimebase", runtime);
        Assert.Contains("source channel semantics remain unverified", runtime);
        Assert.Contains("checking independent capture timestamps and contiguous smpCnt", runtime);
        // The existing clean timebase detector still enforces independent
        // timestamp evidence and counter continuity before permitting samples.
        var detector = new SmvObservedTimebaseDetector(50);
        var started = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        AR.Iec61850.SampledValues.Measurements.SvTimebaseResolution? resolved = null;
        for (var n = 0; n < 500; n++)
            resolved ??= detector.Observe((ushort)n, started.AddTicks(n * 2500));
        Assert.NotNull(resolved);
        Assert.Equal(80, resolved.SamplesPerCycle);
    }

    [Fact]
    public void LiveBuffer_UsesObservationRatherThanAssuming80ForAnyUnknownStream()
    {
        var text = Read("Services/SmvLiveCaptureService.cs");
        Assert.Contains("_observedTimebase.Observe(asdu.SampleCount, timestamp)", text);
        Assert.Contains("AllocateResolvedWindow();", text);
        Assert.Contains("checking smpCnt continuity and capture rate", text);
        Assert.Contains("_observedTimebase.Reset()", text);
    }

    private static string Read(string path)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, path);
            if (File.Exists(p)) return File.ReadAllText(p);
        }
        throw new FileNotFoundException(path);
    }
}
