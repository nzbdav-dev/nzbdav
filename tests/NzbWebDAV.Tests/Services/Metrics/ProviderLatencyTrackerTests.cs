using NzbWebDAV.Services.Metrics;

namespace NzbWebDAV.Tests.Services.Metrics;

public class ProviderLatencyTrackerTests
{
    [Fact]
    public void Record_AccumulatesCountSumAndMax()
    {
        var now = 1_700_000_000_000L;
        var tracker = new ProviderLatencyTracker(() => now);

        tracker.Record("p1", LatencyPhase.Response, DownloadWorkload.Streaming, NntpOperation.Body,
            TimeSpan.FromMilliseconds(10));
        tracker.Record("p1", LatencyPhase.Response, DownloadWorkload.Streaming, NntpOperation.Body,
            TimeSpan.FromMilliseconds(40));

        var snap = tracker.SnapshotUnpersisted().Single();
        Assert.Equal(2, snap.Snapshot.Count);
        Assert.Equal(50, snap.Snapshot.SumMs);
        Assert.Equal(40, snap.Snapshot.MaxMs);
        Assert.Equal(2, snap.Snapshot.Counts.Sum());
    }

    [Fact]
    public void Record_DoesNotMergeDistinctPhases()
    {
        var now = 1_700_000_000_000L;
        var tracker = new ProviderLatencyTracker(() => now);

        tracker.Record("p1", LatencyPhase.Response, DownloadWorkload.Streaming, NntpOperation.Body,
            TimeSpan.FromMilliseconds(5));
        tracker.Record("p1", LatencyPhase.PoolWait, DownloadWorkload.Streaming, NntpOperation.Body,
            TimeSpan.FromMilliseconds(5));

        Assert.Equal(2, tracker.PendingBuckets);
        Assert.Equal(2, tracker.SnapshotUnpersisted().Count);
    }

    [Fact]
    public void PrepareClosed_OnlyDrainsOlderMinutes_AndRetriesUntilAck()
    {
        var minute0 = 1_700_000_000_000L;
        minute0 -= minute0 % 60_000;
        var minute1 = minute0 + 60_000;
        var now = minute0;
        var tracker = new ProviderLatencyTracker(() => now);

        tracker.Record("p1", LatencyPhase.Response, DownloadWorkload.Queue, NntpOperation.Stat,
            TimeSpan.FromMilliseconds(1));

        now = minute1;
        tracker.Record("p1", LatencyPhase.Response, DownloadWorkload.Queue, NntpOperation.Stat,
            TimeSpan.FromMilliseconds(2));

        var closed = tracker.PrepareClosed(cutoffMinute: minute1);
        Assert.Single(closed);
        Assert.Equal(minute0, closed[0].Key.Minute);
        Assert.Equal(1, closed[0].Snapshot.Count);

        // Unacknowledged item remains in-flight and is returned again.
        var again = tracker.PrepareClosed(cutoffMinute: minute1);
        Assert.Single(again);
        Assert.Equal(closed[0].Key, again[0].Key);

        tracker.Acknowledge(closed[0].Key);
        Assert.Empty(tracker.PrepareClosed(cutoffMinute: minute1));
        Assert.Equal(1, tracker.PendingBuckets); // current-minute active bucket
    }

    [Fact]
    public void SnapshotUnpersisted_IsNonDestructive_AndIncludesInFlight()
    {
        var minute0 = 1_700_000_000_000L;
        minute0 -= minute0 % 60_000;
        var minute1 = minute0 + 60_000;
        var now = minute0;
        var tracker = new ProviderLatencyTracker(() => now);
        tracker.Record("p1", LatencyPhase.PermitWait, DownloadWorkload.Streaming, NntpOperation.Admission,
            TimeSpan.FromMilliseconds(3));

        now = minute1;
        tracker.PrepareClosed(minute1);
        tracker.Record(null, LatencyPhase.PermitWait, DownloadWorkload.Streaming, NntpOperation.Admission,
            TimeSpan.FromMilliseconds(4));

        var before = tracker.PendingBuckets;
        var snap = tracker.SnapshotUnpersisted();
        Assert.Equal(2, snap.Count);
        Assert.Equal(before, tracker.PendingBuckets);
    }

    [Fact]
    public void ResetProvider_RemovesOnlyMatchingKeys()
    {
        var now = 1_700_000_000_000L;
        var tracker = new ProviderLatencyTracker(() => now);
        tracker.Record("a", LatencyPhase.Response, DownloadWorkload.Background, NntpOperation.Body,
            TimeSpan.FromMilliseconds(1));
        tracker.Record("b", LatencyPhase.Response, DownloadWorkload.Background, NntpOperation.Body,
            TimeSpan.FromMilliseconds(1));
        tracker.Record(null, LatencyPhase.PermitWait, DownloadWorkload.Background, NntpOperation.Admission,
            TimeSpan.FromMilliseconds(1));

        tracker.ResetProvider("a");

        var remaining = tracker.SnapshotUnpersisted();
        Assert.DoesNotContain(remaining, x => x.Key.ProviderKey == "a");
        Assert.Contains(remaining, x => x.Key.ProviderKey == "b");
        Assert.Contains(remaining, x => x.Key.ProviderKey is null);
    }

    [Fact]
    public void ConcurrentRecords_NeverExceedMaxPendingBuckets()
    {
        var now = 1_700_000_000_000L;
        var tracker = new ProviderLatencyTracker(() => now);
        Parallel.For(0, ProviderLatencyTracker.MaxPendingBuckets + 500, i =>
        {
            tracker.Record(
                $"unique-{i}",
                LatencyPhase.Response,
                DownloadWorkload.Background,
                NntpOperation.Body,
                TimeSpan.FromMilliseconds(i % 100));
        });

        Assert.True(tracker.PendingBuckets <= ProviderLatencyTracker.MaxPendingBuckets);
        Assert.True(tracker.DroppedObservations > 0);
    }
}
