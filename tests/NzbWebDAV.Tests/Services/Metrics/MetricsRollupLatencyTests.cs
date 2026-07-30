using System.Text.Json;
using NzbWebDAV.Database.Models.Metrics;
using NzbWebDAV.Services.Metrics;

namespace NzbWebDAV.Tests.Services.Metrics;

public class MetricsRollupLatencyTests
{
    [Fact]
    public void FlushClosedLatency_EnqueuesOneEventPerKey_AndAcknowledges()
    {
        var minute0 = 1_700_000_000_000L;
        minute0 -= minute0 % 60_000;
        var minute1 = minute0 + 60_000;
        var now = minute0;
        var tracker = new ProviderLatencyTracker(() => now);
        for (var i = 0; i < 100; i++)
        {
            tracker.Record("p1", LatencyPhase.Response, DownloadWorkload.Streaming, NntpOperation.Body,
                TimeSpan.FromMilliseconds(i));
        }

        now = minute1;
        var writer = new MetricsWriter(() => throw new InvalidOperationException("no db needed"));
        var rollup = new MetricsRollupService(new ProviderBytesTracker(), tracker, writer);

        rollup.FlushClosedLatency(currentMinute: minute1);

        var queued = writer.SnapshotQueuedEvents("latency");
        Assert.Single(queued);
        Assert.Equal("latency", queued[0].Kind);
        Assert.Equal("p1", queued[0].Tag1);
        Assert.Equal("response", queued[0].Tag2);
        Assert.Equal("streaming/body", queued[0].RefId);
        Assert.Equal(100, queued[0].Num);

        var payload = JsonSerializer.Deserialize<LatencyHistogramPayload>(queued[0].Note!);
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Version);
        Assert.Equal(100, payload.Counts.Sum());
        Assert.Equal(0, tracker.PendingBuckets);
    }

    [Fact]
    public void FlushClosedLatency_QueueFull_LeavesInFlightForRetry()
    {
        var minute0 = 1_700_000_000_000L;
        minute0 -= minute0 % 60_000;
        var minute1 = minute0 + 60_000;
        var now = minute0;
        var tracker = new ProviderLatencyTracker(() => now);
        tracker.Record("p1", LatencyPhase.PoolWait, DownloadWorkload.Queue, NntpOperation.Stat,
            TimeSpan.FromMilliseconds(5));
        now = minute1;

        var writer = new MetricsWriter(() => throw new InvalidOperationException("no db needed"));
        // Fill event queue to capacity.
        for (var i = 0; i < 10_000; i++)
            writer.RecordEvent(new MetricEvent { At = i, Kind = "circuit", Tag1 = "x" });

        var rollup = new MetricsRollupService(new ProviderBytesTracker(), tracker, writer);
        rollup.FlushClosedLatency(minute1);

        Assert.Equal(1, tracker.PendingBuckets);
        Assert.DoesNotContain(writer.SnapshotQueuedEvents("latency"), e => e.Tag1 == "p1");
    }
}
