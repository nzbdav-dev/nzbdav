using System.Text.Json;
using NzbWebDAV.Database.Models.Metrics;
using NzbWebDAV.Services.Metrics;
using Serilog;

namespace NzbWebDAV.Services.SupportPack;

internal static class LatencySupportPackProjection
{
    private const long FiveMinutesMs = 5 * 60_000;

    internal sealed record NormalizedLatencyRow(
        long Minute,
        long? EventId,
        string? ProviderKey,
        LatencyPhase Phase,
        DownloadWorkload Workload,
        NntpOperation Operation,
        long Samples,
        long SumMs,
        int MaxMs,
        long[] Counts,
        int SourcePriority);

    internal static bool TryNormalize(
        long at,
        long? eventId,
        string? providerKey,
        string? phaseWire,
        string? refId,
        long? num,
        string? note,
        int sourcePriority,
        out NormalizedLatencyRow row)
    {
        row = null!;
        if (!LatencyNames.TryParsePhase(phaseWire, out var phase))
            return false;
        if (refId is null)
            return false;
        var slash = refId.IndexOf('/');
        if (slash <= 0 || slash >= refId.Length - 1)
            return false;
        if (!LatencyNames.TryParseWorkload(refId[..slash], out var workload))
            return false;
        if (!LatencyNames.TryParseOperation(refId[(slash + 1)..], out var operation))
            return false;
        if (string.IsNullOrWhiteSpace(note))
            return false;

        LatencyHistogramPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LatencyHistogramPayload>(note);
        }
        catch (JsonException)
        {
            return false;
        }

        if (payload is null || payload.Version != 1)
            return false;
        if (payload.Counts is null || payload.Counts.Length != LatencyHistogram.UpperBoundsMs.Length)
            return false;
        if (payload.SumMs < 0 || payload.MaxMs < 0 || num is null or < 0)
            return false;
        if (payload.Counts.Any(c => c < 0))
            return false;
        if (payload.Counts.Sum() != num.Value)
            return false;

        row = new NormalizedLatencyRow(
            at,
            eventId,
            string.IsNullOrWhiteSpace(providerKey) ? null : providerKey,
            phase,
            workload,
            operation,
            num.Value,
            payload.SumMs,
            payload.MaxMs,
            payload.Counts,
            sourcePriority);
        return true;
    }

    internal static List<NormalizedLatencyRow> Deduplicate(IEnumerable<NormalizedLatencyRow> rows)
    {
        // Prefer persisted, then queued, then tracker. Within persisted, highest Id wins.
        return rows
            .GroupBy(r => (r.Minute, r.ProviderKey, r.Phase, r.Workload, r.Operation))
            .Select(g => g
                .OrderBy(r => r.SourcePriority)
                .ThenByDescending(r => r.EventId ?? 0)
                .First())
            .ToList();
    }

    internal static object BuildLatency24Hours(
        IReadOnlyList<NormalizedLatencyRow> rows,
        IReadOnlyDictionary<string, string?> nicknames,
        int malformedRows)
    {
        var merged = rows
            .GroupBy(r => (
                Bucket: r.Minute - r.Minute % FiveMinutesMs,
                r.ProviderKey,
                r.Phase,
                r.Workload,
                r.Operation))
            .Select(g =>
            {
                var samples = g.Sum(x => x.Samples);
                var sumMs = g.Sum(x => x.SumMs);
                var maxMs = g.Max(x => x.MaxMs);
                var counts = new long[LatencyHistogram.UpperBoundsMs.Length];
                foreach (var row in g)
                {
                    for (var i = 0; i < counts.Length; i++)
                        counts[i] += row.Counts[i];
                }

                return new
                {
                    bucket = g.Key.Bucket,
                    providerKey = g.Key.ProviderKey,
                    nickname = g.Key.ProviderKey is null
                        ? null
                        : nicknames.GetValueOrDefault(g.Key.ProviderKey),
                    phase = LatencyNames.ToWireName(g.Key.Phase),
                    workload = LatencyNames.ToWireName(g.Key.Workload),
                    operation = LatencyNames.ToWireName(g.Key.Operation),
                    samples,
                    avgMs = samples == 0 ? 0d : (double)sumMs / samples,
                    p50Ms = LatencyHistogram.PercentileUpperBound(counts, samples, maxMs, 0.50),
                    p90Ms = LatencyHistogram.PercentileUpperBound(counts, samples, maxMs, 0.90),
                    p99Ms = LatencyHistogram.PercentileUpperBound(counts, samples, maxMs, 0.99),
                    maxMs,
                    histogram = counts,
                };
            })
            .OrderBy(x => x.bucket)
            .ThenBy(x => x.providerKey, StringComparer.Ordinal)
            .ThenBy(x => x.phase, StringComparer.Ordinal)
            .ThenBy(x => x.workload, StringComparer.Ordinal)
            .ThenBy(x => x.operation, StringComparer.Ordinal)
            .ToList();

        var providerSeries = merged.Where(x => x.providerKey is not null).ToList();
        var admissionSeries = merged
            .Where(x => x.providerKey is null)
            .Select(x => new
            {
                x.bucket,
                x.phase,
                x.workload,
                x.operation,
                x.samples,
                x.avgMs,
                x.p50Ms,
                x.p90Ms,
                x.p99Ms,
                x.maxMs,
                x.histogram,
            })
            .ToList();

        if (malformedRows > 0)
            Log.Debug("Support pack skipped {Count} malformed latency MetricEvent rows", malformedRows);

        return new
        {
            schemaVersion = 1,
            bucketSizeMs = FiveMinutesMs,
            histogramUpperBoundsMs = LatencyHistogram.UpperBoundsMs,
            percentilesAreBucketUpperBounds = true,
            malformedRows,
            semantics = new
            {
                response = "Successful NNTP response availability after a provider connection is acquired; excludes body drain.",
                poolWait = "Wait to acquire a connection from the named provider pool.",
                permitWait = "Top-level workload connection-budget wait; no provider is selected yet.",
            },
            providerSeries,
            admissionSeries,
        };
    }

    // Lower is preferred: persisted=0, queued=1, tracker=2.
    internal const int SourcePersisted = 0;
    internal const int SourceQueued = 1;
    internal const int SourceTracker = 2;

    internal static IEnumerable<NormalizedLatencyRow> FromFlushItems(
        IEnumerable<LatencyFlushItem> items)
    {
        foreach (var item in items)
        {
            yield return new NormalizedLatencyRow(
                item.Key.Minute,
                EventId: null,
                item.Key.ProviderKey,
                item.Key.Phase,
                item.Key.Workload,
                item.Key.Operation,
                item.Snapshot.Count,
                item.Snapshot.SumMs,
                item.Snapshot.MaxMs,
                item.Snapshot.Counts,
                SourceTracker);
        }
    }

    internal static List<NormalizedLatencyRow> FromMetricEvents(
        IEnumerable<MetricEvent> events,
        int sourcePriority,
        out int malformedRows)
    {
        malformedRows = 0;
        var rows = new List<NormalizedLatencyRow>();
        foreach (var e in events)
        {
            if (TryNormalize(
                    e.At,
                    e.Id == 0 ? null : e.Id,
                    e.Tag1,
                    e.Tag2,
                    e.RefId,
                    e.Num,
                    e.Note,
                    sourcePriority,
                    out var row))
                rows.Add(row);
            else
                malformedRows++;
        }

        return rows;
    }
}
