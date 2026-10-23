using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ComtradeScreenSpaceRenderPolicyTests
{
    [Fact]
    public void FindVisibleRange_UsesViewportAndKeepsBoundaryNeighbors()
    {
        var timestamps = Enumerable.Range(0, 10_000).Select(index => (uint)(index * 1_000)).ToArray();

        var range = ComtradeScreenSpaceRenderPolicy.FindVisibleRange(
            timestamps,
            timestamps.Length,
            2_000_000,
            3_000_000);

        Assert.Equal(1_999, range.StartIndex);
        Assert.Equal(3_002, range.EndExclusive);
        Assert.Equal(1_003, range.Count);
    }

    [Fact]
    public void DenseRecord_UsesPixelBoundedExtremaEnvelope()
    {
        Assert.True(ComtradeScreenSpaceRenderPolicy.UseEnvelope(100_000, 1_000));
        Assert.False(ComtradeScreenSpaceRenderPolicy.UseEnvelope(7_999, 1_000));
        Assert.Equal(1_000, ComtradeScreenSpaceRenderPolicy.PixelBucketCount(999.2));
    }

    [Fact]
    public void SparseRecord_IsBoundedNearTwoPointsPerPixel()
    {
        var stride = ComtradeScreenSpaceRenderPolicy.SparseStride(5_000, 1_000, preserveAllPoints: false);
        Assert.Equal(3, stride);
        Assert.True((5_000 + stride - 1) / stride <= 2_000);
    }

    [Fact]
    public void AlreadyReducedEnvelope_CanPreserveItsExtremaPoints()
    {
        Assert.Equal(1, ComtradeScreenSpaceRenderPolicy.SparseStride(4_096, 1_000, preserveAllPoints: true));
    }

    [Fact]
    public void InvalidDescendingTimestampDomain_DegradesToFullValidatedRange()
    {
        var timestamps = new uint[] { 30, 20, 10 };
        var range = ComtradeScreenSpaceRenderPolicy.FindVisibleRange(timestamps, 3, 10, 20);
        Assert.Equal(0, range.StartIndex);
        Assert.Equal(3, range.EndExclusive);
    }
}
