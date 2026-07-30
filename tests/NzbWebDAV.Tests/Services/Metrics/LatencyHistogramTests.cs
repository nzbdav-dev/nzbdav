using NzbWebDAV.Services.Metrics;

namespace NzbWebDAV.Tests.Services.Metrics;

public class LatencyHistogramTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(5, 3)]
    [InlineData(10, 4)]
    [InlineData(25, 5)]
    [InlineData(3, 3)] // between 2 and 5 → upper bound 5
    [InlineData(-5, 0)]
    public void IndexOf_MapsBoundaries(long milliseconds, int expectedIndex)
    {
        Assert.Equal(expectedIndex, LatencyHistogram.IndexOf(milliseconds));
    }

    [Fact]
    public void IndexOf_MaxValue_UsesLastBucket()
    {
        Assert.Equal(
            LatencyHistogram.UpperBoundsMs.Length - 1,
            LatencyHistogram.IndexOf(long.MaxValue));
    }

    [Fact]
    public void PercentileUpperBound_EmptySamples_ReturnsZero()
    {
        var counts = new long[LatencyHistogram.UpperBoundsMs.Length];
        Assert.Equal(0, LatencyHistogram.PercentileUpperBound(counts, 0, 100, 0.95));
    }

    [Fact]
    public void PercentileUpperBound_UsesBucketUpperBounds()
    {
        var counts = new long[LatencyHistogram.UpperBoundsMs.Length];
        counts[LatencyHistogram.IndexOf(10)] = 5;
        counts[LatencyHistogram.IndexOf(100)] = 5;

        Assert.Equal(10, LatencyHistogram.PercentileUpperBound(counts, 10, 100, 0.50));
        Assert.Equal(100, LatencyHistogram.PercentileUpperBound(counts, 10, 100, 0.90));
        Assert.Equal(100, LatencyHistogram.PercentileUpperBound(counts, 10, 100, 0.99));
    }

    [Fact]
    public void PercentileUpperBound_OverflowBucket_ReturnsExactMax()
    {
        var counts = new long[LatencyHistogram.UpperBoundsMs.Length];
        counts[^1] = 3;
        Assert.Equal(250_000, LatencyHistogram.PercentileUpperBound(counts, 3, 250_000, 0.99));
        Assert.NotEqual(int.MaxValue, LatencyHistogram.PercentileUpperBound(counts, 3, 250_000, 0.50));
    }
}
