namespace NzbWebDAV.Services.Metrics;

internal static class LatencyHistogram
{
    // Inclusive upper bounds in milliseconds. The final bucket catches larger values;
    // exact MaxMs remains available so exports never display int.MaxValue as a latency.
    public static readonly int[] UpperBoundsMs =
    [
        0, 1, 2, 5, 10, 25, 50, 100, 200, 400, 800, 1_500, 3_000,
        6_000, 12_000, 30_000, 60_000, 120_000, int.MaxValue
    ];

    public static int IndexOf(long milliseconds)
    {
        var value = (int)Math.Clamp(milliseconds, 0, int.MaxValue);
        var index = Array.BinarySearch(UpperBoundsMs, value);
        return index >= 0 ? index : Math.Min(~index, UpperBoundsMs.Length - 1);
    }

    public static int PercentileUpperBound(
        IReadOnlyList<long> counts, long samples, int maxMs, double percentile)
    {
        if (samples <= 0) return 0;
        var target = (long)Math.Ceiling(Math.Clamp(percentile, 0, 1) * samples);
        var cumulative = 0L;
        for (var index = 0; index < Math.Min(counts.Count, UpperBoundsMs.Length); index++)
        {
            cumulative += counts[index];
            if (cumulative < target) continue;
            return UpperBoundsMs[index] == int.MaxValue ? maxMs : UpperBoundsMs[index];
        }
        return maxMs;
    }
}

internal sealed record LatencyHistogramPayload(
    int Version,
    long[] Counts,
    long SumMs,
    int MaxMs);
