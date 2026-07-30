using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models.Metrics;
using NzbWebDAV.Utils;
using Serilog;

namespace NzbWebDAV.Services.Metrics;

/// <summary>
/// Materializes per-minute and per-hour rollups from the raw SegmentFetch /
/// ReadSession event tables. Runs once a minute, idempotently upserting the
/// last fully-elapsed minute. On the hour boundary it folds the 60 finished
/// minutes into ProviderHourly. Re-running any window is safe.
///
/// Errors are hard fetch failures only (Status NOT IN Ok/Missing). Expected
/// provider misses (Status = Missing) are counted separately as Misses.
/// </summary>
public class MetricsRollupService(
    ProviderBytesTracker bytesTracker,
    ProviderLatencyTracker latencyTracker,
    MetricsWriter metricsWriter
) : BackgroundService
{
    private const long OneMinute = 60_000;
    private const long OneHour = 60 * OneMinute;
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions CompactJson = new();

    private long _lastMinuteRolled;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TickInterval, stoppingToken).ConfigureAwait(false);
                await RollupTickAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (SigtermUtil.IsSigtermTriggered())
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "MetricsRollupService tick failed");
            }
        }
    }

    private async Task RollupTickAsync()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var currentMinute = FloorTo(nowMs, OneMinute);
        var targetMinute = currentMinute - OneMinute;

        // Catch up at most 60 minutes if the service was paused/restarted.
        var start = _lastMinuteRolled == 0
            ? targetMinute
            : Math.Max(_lastMinuteRolled + OneMinute, targetMinute - 59 * OneMinute);

        await using var db = new MetricsDbContext();
        for (var minute = start; minute <= targetMinute; minute += OneMinute)
        {
            await RollupMinuteAsync(db, minute).ConfigureAwait(false);
            if (minute % OneHour == 0 && minute > 0)
            {
                await RollupHourAsync(db, minute - OneHour).ConfigureAwait(false);
                await RollupFailoverHourAsync(db, minute - OneHour).ConfigureAwait(false);
            }
            _lastMinuteRolled = minute;
        }

        // After fetch-row rollups have written/refreshed ProviderMinute rows, fold in
        // the per-provider byte counts captured by the streaming wrapper. UPSERT-adds
        // so re-runs (catch-up after restart) are idempotent: any closed minute
        // contributes at most once because DrainClosed pops the bucket.
        await ApplyByteCountersAsync(db, currentMinute).ConfigureAwait(false);
        FlushClosedLatency(currentMinute);
    }

    internal void FlushClosedLatency(long currentMinute)
    {
        var generation = metricsWriter.CaptureResetGeneration();
        foreach (var item in latencyTracker.PrepareClosed(currentMinute))
        {
            var value = new MetricEvent
            {
                At = item.Key.Minute,
                Kind = "latency",
                Tag1 = item.Key.ProviderKey,
                Tag2 = LatencyNames.ToWireName(item.Key.Phase),
                RefId = $"{LatencyNames.ToWireName(item.Key.Workload)}/" +
                        LatencyNames.ToWireName(item.Key.Operation),
                Num = item.Snapshot.Count,
                Note = JsonSerializer.Serialize(new LatencyHistogramPayload(
                    Version: 1,
                    Counts: item.Snapshot.Counts,
                    SumMs: item.Snapshot.SumMs,
                    MaxMs: item.Snapshot.MaxMs), CompactJson),
            };

            switch (metricsWriter.TryRecordEvent(value, generation))
            {
                case EventEnqueueResult.Accepted:
                    latencyTracker.Acknowledge(item.Key);
                    break;
                case EventEnqueueResult.ResetRejected:
                    break;
                case EventEnqueueResult.QueueFull:
                    break;
            }
        }
    }

    private async Task ApplyByteCountersAsync(MetricsDbContext db, long currentMinute)
    {
        var drained = bytesTracker.DrainClosed(currentMinute);
        if (drained.Count == 0) return;

        foreach (var (minute, providerKey, bytes) in drained)
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO ProviderMinutes
                    (Minute, Provider, Articles, BytesFetched, Misses, Errors, Retries, SumDurationMs, Hist)
                VALUES ({0}, {1}, 0, {2}, 0, 0, 0, 0, NULL)
                ON CONFLICT(Minute, Provider) DO UPDATE SET
                    BytesFetched = ProviderMinutes.BytesFetched + excluded.BytesFetched;
                """,
                minute, providerKey, bytes).ConfigureAwait(false);

            var hour = FloorTo(minute, OneHour);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO ProviderHourly
                    (Hour, Provider, Articles, BytesFetched, Misses, Errors, Retries, SumDurationMs, P95DurationMs)
                VALUES ({0}, {1}, 0, {2}, 0, 0, 0, 0, NULL)
                ON CONFLICT(Hour, Provider) DO UPDATE SET
                    BytesFetched = ProviderHourly.BytesFetched + excluded.BytesFetched;
                """,
                hour, providerKey, bytes).ConfigureAwait(false);

            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO ThroughputMinutes
                    (Minute, BytesServed, BytesFetched, Articles, Misses, Errors, ActiveReadsMax)
                VALUES ({0}, 0, {1}, 0, 0, 0, 0)
                ON CONFLICT(Minute) DO UPDATE SET
                    BytesFetched = ThroughputMinutes.BytesFetched + excluded.BytesFetched;
                """,
                minute, bytes).ConfigureAwait(false);
        }
    }

    private static async Task RollupMinuteAsync(MetricsDbContext db, long minute)
    {
        var next = minute + OneMinute;

        // ThroughputMinute: read-session bytes (downstream) + fetch bytes (upstream).
        // BytesFetched intentionally omitted from ON CONFLICT — owned by ProviderBytesTracker.
        // Re-rolling a minute (e.g. on catch-up after restart) must not zero out bytes the
        // tracker already deposited via ApplyByteCountersAsync.
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO ThroughputMinutes (Minute, BytesServed, BytesFetched, Articles, Misses, Errors, ActiveReadsMax)
            SELECT
                {0} AS Minute,
                COALESCE((SELECT SUM(BytesServed) FROM ReadSessions WHERE EndedAt >= {0} AND EndedAt < {1}), 0) AS BytesServed,
                0 AS BytesFetched,
                COALESCE((SELECT COUNT(*) FROM SegmentFetches WHERE At >= {0} AND At < {1}), 0) AS Articles,
                COALESCE((SELECT COUNT(*) FROM SegmentFetches WHERE At >= {0} AND At < {1} AND Status = 1), 0) AS Misses,
                COALESCE((SELECT COUNT(*) FROM SegmentFetches WHERE At >= {0} AND At < {1} AND Status NOT IN (0, 1)), 0) AS Errors,
                0 AS ActiveReadsMax
            ON CONFLICT(Minute) DO UPDATE SET
                BytesServed  = excluded.BytesServed,
                Articles     = excluded.Articles,
                Misses       = excluded.Misses,
                Errors       = excluded.Errors;
            """,
            minute, next).ConfigureAwait(false);

        // ProviderMinute: per-provider counters. BytesFetched intentionally omitted from
        // ON CONFLICT — the tracker is the sole writer of that column.
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO ProviderMinutes (Minute, Provider, Articles, BytesFetched, Misses, Errors, Retries, FailoverSaves, SumDurationMs, Hist)
            SELECT {0}, Provider,
                COUNT(*),
                0,
                SUM(CASE WHEN Status = 1 THEN 1 ELSE 0 END),
                SUM(CASE WHEN Status NOT IN (0, 1) THEN 1 ELSE 0 END),
                SUM(Retries),
                SUM(CASE WHEN Status = 0 AND Retries > 0 THEN 1 ELSE 0 END),
                -- Ok-only durations so Overview "Avg ok ms" is not inflated by misses/errors.
                SUM(CASE WHEN Status = 0 THEN DurationMs ELSE 0 END),
                NULL
            FROM SegmentFetches
            WHERE At >= {0} AND At < {1}
            GROUP BY Provider
            ON CONFLICT(Minute, Provider) DO UPDATE SET
                Articles      = excluded.Articles,
                Misses        = excluded.Misses,
                Errors        = excluded.Errors,
                Retries       = excluded.Retries,
                FailoverSaves = excluded.FailoverSaves,
                SumDurationMs = excluded.SumDurationMs;
            """,
            minute, next).ConfigureAwait(false);
    }

    private static async Task RollupHourAsync(MetricsDbContext db, long hour)
    {
        var next = hour + OneHour;
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO ProviderHourly (Hour, Provider, Articles, BytesFetched, Misses, Errors, Retries, FailoverSaves, SumDurationMs, P95DurationMs)
            SELECT {0}, Provider,
                SUM(Articles),
                SUM(BytesFetched),
                SUM(Misses),
                SUM(Errors),
                SUM(Retries),
                SUM(FailoverSaves),
                SUM(SumDurationMs),
                NULL
            FROM ProviderMinutes
            WHERE Minute >= {0} AND Minute < {1}
            GROUP BY Provider
            ON CONFLICT(Hour, Provider) DO UPDATE SET
                Articles      = excluded.Articles,
                BytesFetched  = excluded.BytesFetched,
                Misses        = excluded.Misses,
                Errors        = excluded.Errors,
                Retries       = excluded.Retries,
                FailoverSaves = excluded.FailoverSaves,
                SumDurationMs = excluded.SumDurationMs;
            """,
            hour, next).ConfigureAwait(false);
    }

    private static async Task RollupFailoverHourAsync(MetricsDbContext db, long hour)
    {
        var next = hour + OneHour;
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO FailoverHourly (Hour, FromProvider, ToProvider, Reason, Count)
            SELECT {0}, FromProvider, ToProvider, Reason, COUNT(*)
            FROM FailoverMisses
            WHERE At >= {0} AND At < {1}
            GROUP BY FromProvider, ToProvider, Reason
            ON CONFLICT(Hour, FromProvider, ToProvider, Reason) DO UPDATE SET
                Count = excluded.Count;
            """,
            hour, next).ConfigureAwait(false);
    }

    private static long FloorTo(long value, long step) => value - (value % step);
}
