using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ArdIrecLocusNativeSessionIntegrationTests
{
    [Fact]
    public void PackagedBridge_ProvidesSixLoopCursorAndBoundedLocus_WhenConfigured()
    {
        var bridgePath = Environment.GetEnvironmentVariable("ARSAS_ARDIREC_BRIDGE_PATH");
        var cfgPath = Environment.GetEnvironmentVariable("ARSAS_NATIVE_LOCUS_TEST_CFG");
        if (string.IsNullOrWhiteSpace(bridgePath) || string.IsNullOrWhiteSpace(cfgPath))
            return;

        Assert.True(File.Exists(bridgePath), $"Native bridge was not found: {bridgePath}");
        Assert.True(File.Exists(cfgPath), $"Distance COMTRADE fixture was not found: {cfgPath}");

        Assert.True(ArdIrecLocusNativeSession.TryOpen(cfgPath, out var session, out var error), error);
        Assert.NotNull(session);
        using (session!)
        {
            Assert.Equal((ulong)48, session.FrameCount);
            Assert.True(session.TryReadLoops(
                22, ArdIrecNativeBridge.ValueSecondary, 0.0, 0.0,
                out var loops, out error), error);
            Assert.Equal(6, loops.Length);
            Assert.All(loops, point => Assert.True(point.Valid));
            Assert.Equal(ArdIrecLocusNativeSession.LoopL1E, loops[0].Loop);
            Assert.InRange(loops[0].R, 99.8, 100.2);
            Assert.InRange(Math.Abs(loops[0].X), 0.0, 0.2);

            Assert.True(session.TryReadLocus(
                ArdIrecLocusNativeSession.LoopL1E,
                0, session.FrameCount, 16,
                ArdIrecNativeBridge.ValueSecondary, 0.0, 0.0,
                out var locus, out error), error);
            Assert.InRange(locus.Length, 2, 16);
            Assert.Equal((ulong)0, locus[0].ReferenceFrame);
            Assert.Equal((ulong)47, locus[^1].ReferenceFrame);
            Assert.Contains(locus, point => point.Valid && point.R is > 99.8 and < 100.2);
            Assert.False(locus[^1].Valid);
        }
    }
}
