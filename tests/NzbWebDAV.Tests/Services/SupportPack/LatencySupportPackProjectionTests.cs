using System.Text.Json;
using NzbWebDAV.Database.Models.Metrics;
using NzbWebDAV.Services.Metrics;
using NzbWebDAV.Services.SupportPack;

namespace NzbWebDAV.Tests.Services.SupportPack;

public class LatencySupportPackProjectionTests
{
    [Fact]
    public void BuildLatency24Hours_MergesFiveMinuteBucketsAndComputesStats()
    {
        var bounds = LatencyHistogram.UpperBoundsMs;
        var countsA = new long[bounds.Length];
        var countsB = new long[bounds.Length];
        countsA[LatencyHistogram.IndexOf(10)] = 2;
        countsB[LatencyHistogram.IndexOf(100)] = 2;

        var minuteBase = 1_700_000_000_000L;
        minuteBase -= minuteBase % (5 * 60_000);
        var rows = new[]
        {
            new LatencySupportPackProjection.NormalizedLatencyRow(
                minuteBase, 1, "prov-a", LatencyPhase.Response, DownloadWorkload.Streaming,
                NntpOperation.Body, 2, 20, 10, countsA, LatencySupportPackProjection.SourcePersisted),
            new LatencySupportPackProjection.NormalizedLatencyRow(
                minuteBase + 60_000, 2, "prov-a", LatencyPhase.Response, DownloadWorkload.Streaming,
                NntpOperation.Body, 2, 200, 100, countsB, LatencySupportPackProjection.SourcePersisted),
        };

        var nicknames = new Dictionary<string, string?> { ["prov-a"] = "Primary" };
        var projected = LatencySupportPackProjection.BuildLatency24Hours(rows, nicknames, malformedRows: 0);
        var json = JsonSerializer.Serialize(projected);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.True(root.GetProperty("percentilesAreBucketUpperBounds").GetBoolean());
        Assert.Equal(0, root.GetProperty("malformedRows").GetInt32());

        var series = root.GetProperty("providerSeries");
        Assert.Equal(1, series.GetArrayLength());
        var row = series[0];
        Assert.Equal("prov-a", row.GetProperty("providerKey").GetString());
        Assert.Equal("Primary", row.GetProperty("nickname").GetString());
        Assert.Equal("response", row.GetProperty("phase").GetString());
        Assert.Equal(4, row.GetProperty("samples").GetInt64());
        Assert.Equal(55d, row.GetProperty("avgMs").GetDouble());
        Assert.Equal(100, row.GetProperty("maxMs").GetInt32());
        Assert.Equal(10, row.GetProperty("p50Ms").GetInt32());
    }

    [Fact]
    public void TryNormalize_RejectsMalformedPayloads()
    {
        Assert.False(LatencySupportPackProjection.TryNormalize(
            1, 1, "p", "response", "streaming/body", 1, "{not-json", 0, out _));
        Assert.False(LatencySupportPackProjection.TryNormalize(
            1, 1, "p", "unknown-phase", "streaming/body", 1, "{}", 0, out _));
        Assert.False(LatencySupportPackProjection.TryNormalize(
            1, 1, null, "permit-wait", "streaming/admission", 1,
            JsonSerializer.Serialize(new LatencyHistogramPayload(2, new long[LatencyHistogram.UpperBoundsMs.Length], 0, 0)),
            0, out _));
    }

    [Fact]
    public void Deduplicate_PrefersPersistedOverQueuedAndTracker()
    {
        var counts = new long[LatencyHistogram.UpperBoundsMs.Length];
        counts[0] = 1;
        var keyMinute = 1_700_000_000_000L;
        var persisted = new LatencySupportPackProjection.NormalizedLatencyRow(
            keyMinute, 9, "p", LatencyPhase.PoolWait, DownloadWorkload.Queue, NntpOperation.Body,
            1, 1, 1, counts, LatencySupportPackProjection.SourcePersisted);
        var queued = persisted with { EventId = null, Samples = 99, SourcePriority = LatencySupportPackProjection.SourceQueued };
        var tracker = persisted with { EventId = null, Samples = 50, SourcePriority = LatencySupportPackProjection.SourceTracker };

        var deduped = LatencySupportPackProjection.Deduplicate([queued, tracker, persisted]);
        Assert.Single(deduped);
        Assert.Equal(1, deduped[0].Samples);
        Assert.Equal(9, deduped[0].EventId);
    }

    [Fact]
    public void FromMetricEvents_CountsMalformedWithoutThrowing()
    {
        var goodCounts = new long[LatencyHistogram.UpperBoundsMs.Length];
        goodCounts[1] = 1;
        var events = new[]
        {
            new MetricEvent
            {
                Id = 1,
                At = 1000,
                Kind = "latency",
                Tag1 = "p",
                Tag2 = "response",
                RefId = "streaming/body",
                Num = 1,
                Note = JsonSerializer.Serialize(new LatencyHistogramPayload(1, goodCounts, 1, 1)),
            },
            new MetricEvent
            {
                Id = 2,
                At = 1000,
                Kind = "latency",
                Tag2 = "not-a-phase",
                RefId = "streaming/body",
                Num = 1,
                Note = "{}",
            },
        };

        var rows = LatencySupportPackProjection.FromMetricEvents(
            events, LatencySupportPackProjection.SourcePersisted, out var malformed);
        Assert.Single(rows);
        Assert.Equal(1, malformed);
    }
}
