using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SmvArstackMultiAsduTests
{
    [Theory]
    [InlineData(50, 4000, 80)]
    [InlineData(60, 4800, 80)]
    [InlineData(50, 12800, 256)]
    [InlineData(60, 15360, 256)]
    public void BatchedCaptureClock_DeclaredRatePlusTwoCyclesResolves(int hz, ushort rate, int spc)
    {
        var verifier = new SmvDeclaredRateCounterVerifier(hz, 2);
        var t = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        for (var n = 0; n < spc * 2 - 1; n++)
            Assert.Null(verifier.Observe((ushort)n, t.AddTicks(n * TimeSpan.TicksPerSecond / rate), rate, 0));
        var proof = verifier.Observe((ushort)(spc * 2 - 1), t.AddTicks((spc * 2 - 1) * TimeSpan.TicksPerSecond / rate), rate, 0);
        Assert.NotNull(proof);
        Assert.Equal(spc, proof.SamplesPerCycle);
        Assert.Equal(rate, proof.SampleCounterWrap);
        Assert.Contains("per-ASDU timestamp cadence unverified", proof.Reason);
    }

    [Fact]
    public void MissingCounter_ResetsWindowAndRefusesOldEvidence()
    {
        var verifier = new SmvDeclaredRateCounterVerifier(50, 2);
        var t = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        for (var n = 0; n < 120; n++)
            Assert.Null(verifier.Observe((ushort)n, t.AddTicks(n * 2500), 4000, 0));
        Assert.Null(verifier.Observe(125, t.AddTicks(125 * 2500), 4000, 0));
        for (var n = 126; n < 260; n++)
            Assert.Null(verifier.Observe((ushort)n, t.AddTicks(n * 2500), 4000, 0));
    }

    [Fact]
    public void NoRateWrongRateOrBadMode_DoesNotGuess80FromPayloadOrNominal()
    {
        foreach (var rate in new ushort?[] {null, 79, 2500, 4800})
        {
            var verifier = new SmvDeclaredRateCounterVerifier(50, 2);
            for(var i=0;i<200;i++)
                Assert.Null(verifier.Observe((ushort)i, DateTimeOffset.UtcNow.AddTicks(i * 2500), rate, 0));
        }
        var mode = new SmvDeclaredRateCounterVerifier(50, 2);
        for(var i=0;i<200;i++)
            Assert.Null(mode.Observe((ushort)i, DateTimeOffset.UtcNow.AddTicks(i * 2500), 4000, 2));
    }

    [Fact]
    public void FrozenCaptureClock_FailsClosedEvenWithDeclaredRateAndCounterContinuity()
    {
        var verifier = new SmvDeclaredRateCounterVerifier(50, 2);
        var frozen = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 300; i++)
            Assert.Null(verifier.Observe((ushort)i, frozen, 4000, 0));
    }

    [Fact]
    public void ObservedClockAndCounterProof_AreBothAllowedButNeverFakeSamples()
    {
        var source = Read("Services/SmvLiveCaptureService.cs");
        Assert.Contains("_declaredRateVerifier.Observe(asdu.SampleCount", source);
        Assert.Contains("_observedTimebase.Observe(asdu.SampleCount, timestamp)", source);
        Assert.Contains("var resolved = observed ?? declaredCounterProof;", source);
        Assert.Contains("_declaredRateVerifier.Reset();", source);
    }

    private static string Read(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir=dir.Parent)
        {
            var p=Path.Combine(dir.FullName, relative);
            if(File.Exists(p))return File.ReadAllText(p);
        }
        throw new FileNotFoundException(relative);
    }
}
