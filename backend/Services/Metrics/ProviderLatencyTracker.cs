namespace NzbWebDAV.Services.Metrics;

internal enum LatencyPhase { Response, PoolWait, PermitWait, LocalCapWait }

internal enum DownloadWorkload { Streaming, Queue, Maintenance, Background }

internal enum NntpOperation
{
    Admission, Body, Article, Stat, Head, Date,
    PipelinedBody, PipelinedArticle, PipelinedStat,
}

/// <summary>
/// Wire names for support-pack / MetricEvent tags. Persisted names are a contract and
/// must not change if an enum member is renamed.
/// </summary>
internal static class LatencyNames
{
    public static string ToWireName(LatencyPhase phase) => phase switch
    {
        LatencyPhase.Response => "response",
        LatencyPhase.PoolWait => "pool-wait",
        LatencyPhase.PermitWait => "permit-wait",
        LatencyPhase.LocalCapWait => "local-cap-wait",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null),
    };

    public static string ToWireName(DownloadWorkload workload) => workload switch
    {
        DownloadWorkload.Streaming => "streaming",
        DownloadWorkload.Queue => "queue",
        DownloadWorkload.Maintenance => "maintenance",
        DownloadWorkload.Background => "background",
        _ => throw new ArgumentOutOfRangeException(nameof(workload), workload, null),
    };

    public static string ToWireName(NntpOperation operation) => operation switch
    {
        NntpOperation.Admission => "admission",
        NntpOperation.Body => "body",
        NntpOperation.Article => "article",
        NntpOperation.Stat => "stat",
        NntpOperation.Head => "head",
        NntpOperation.Date => "date",
        NntpOperation.PipelinedBody => "pipelined-body",
        NntpOperation.PipelinedArticle => "pipelined-article",
        NntpOperation.PipelinedStat => "pipelined-stat",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
    };

    public static bool TryParsePhase(string? wire, out LatencyPhase phase)
    {
        phase = default;
        if (wire is null) return false;
        switch (wire)
        {
            case "response": phase = LatencyPhase.Response; return true;
            case "pool-wait": phase = LatencyPhase.PoolWait; return true;
            case "permit-wait": phase = LatencyPhase.PermitWait; return true;
            case "local-cap-wait": phase = LatencyPhase.LocalCapWait; return true;
            default: return false;
        }
    }

    public static bool TryParseWorkload(string? wire, out DownloadWorkload workload)
    {
        workload = default;
        if (wire is null) return false;
        switch (wire)
        {
            case "streaming": workload = DownloadWorkload.Streaming; return true;
            case "queue": workload = DownloadWorkload.Queue; return true;
            case "maintenance": workload = DownloadWorkload.Maintenance; return true;
            case "background": workload = DownloadWorkload.Background; return true;
            default: return false;
        }
    }

    public static bool TryParseOperation(string? wire, out NntpOperation operation)
    {
        operation = default;
        if (wire is null) return false;
        switch (wire)
        {
            case "admission": operation = NntpOperation.Admission; return true;
            case "body": operation = NntpOperation.Body; return true;
            case "article": operation = NntpOperation.Article; return true;
            case "stat": operation = NntpOperation.Stat; return true;
            case "head": operation = NntpOperation.Head; return true;
            case "date": operation = NntpOperation.Date; return true;
            case "pipelined-body": operation = NntpOperation.PipelinedBody; return true;
            case "pipelined-article": operation = NntpOperation.PipelinedArticle; return true;
            case "pipelined-stat": operation = NntpOperation.PipelinedStat; return true;
            default: return false;
        }
    }

    public static NntpOperation FromCommandName(string name) => name.ToUpperInvariant() switch
    {
        "BODY" => NntpOperation.Body,
        "ARTICLE" => NntpOperation.Article,
        "STAT" => NntpOperation.Stat,
        "HEAD" => NntpOperation.Head,
        "DATE" => NntpOperation.Date,
        _ => NntpOperation.Body,
    };
}

internal readonly record struct LatencyKey(
    long Minute,
    string? ProviderKey,
    LatencyPhase Phase,
    DownloadWorkload Workload,
    NntpOperation Operation);

internal sealed record LatencyAccumulatorSnapshot(
    long[] Counts, long Count, long SumMs, int MaxMs);

internal sealed record LatencyFlushItem(
    LatencyKey Key, LatencyAccumulatorSnapshot Snapshot);

public sealed class ProviderLatencyTracker
{
    private const long OneMinuteMs = 60_000;
    internal const int MaxPendingBuckets = 10_000;

    private readonly object _gate = new();
    private readonly Dictionary<LatencyKey, Accumulator> _active = new();
    private readonly Dictionary<LatencyKey, LatencyFlushItem> _inFlight = new();
    private readonly Func<long> _nowMs;
    private long _droppedObservations;

    public ProviderLatencyTracker() : this(static () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
    {
    }

    internal ProviderLatencyTracker(Func<long> nowMs)
    {
        _nowMs = nowMs;
    }

    internal void Record(
        string? providerKey,
        LatencyPhase phase,
        DownloadWorkload workload,
        NntpOperation operation,
        TimeSpan elapsed)
    {
        var now = _nowMs();
        var key = new LatencyKey(
            now - now % OneMinuteMs,
            string.IsNullOrWhiteSpace(providerKey) ? null : providerKey,
            phase,
            workload,
            operation);
        var milliseconds = Math.Max(0, (long)elapsed.TotalMilliseconds);
        lock (_gate)
        {
            if (!_active.TryGetValue(key, out var accumulator))
            {
                if (_active.Count + _inFlight.Count >= MaxPendingBuckets)
                {
                    Interlocked.Increment(ref _droppedObservations);
                    return;
                }
                accumulator = new Accumulator();
                _active.Add(key, accumulator);
            }
            accumulator.Record(milliseconds);
        }
    }

    internal IReadOnlyList<LatencyFlushItem> PrepareClosed(long cutoffMinute)
    {
        lock (_gate)
        {
            var toMove = new List<LatencyKey>();
            foreach (var (key, accumulator) in _active)
            {
                if (key.Minute >= cutoffMinute) continue;
                toMove.Add(key);
                _inFlight[key] = new LatencyFlushItem(key, accumulator.Snapshot());
            }

            foreach (var key in toMove)
                _active.Remove(key);

            return OrderItems(_inFlight.Values);
        }
    }

    internal void Acknowledge(LatencyKey key)
    {
        lock (_gate)
        {
            _inFlight.Remove(key);
        }
    }

    internal IReadOnlyList<LatencyFlushItem> SnapshotUnpersisted()
    {
        lock (_gate)
        {
            var byKey = new Dictionary<LatencyKey, LatencyFlushItem>();
            foreach (var (key, accumulator) in _active)
                byKey[key] = new LatencyFlushItem(key, accumulator.Snapshot());
            foreach (var (key, item) in _inFlight)
                byKey[key] = item;
            return OrderItems(byKey.Values);
        }
    }

    internal void ResetCounters()
    {
        lock (_gate)
        {
            _active.Clear();
            _inFlight.Clear();
            Interlocked.Exchange(ref _droppedObservations, 0);
        }
    }

    internal void ResetProvider(string providerKey)
    {
        if (string.IsNullOrEmpty(providerKey)) return;
        lock (_gate)
        {
            RemoveMatching(_active, key => key.ProviderKey == providerKey);
            RemoveMatching(_inFlight, key => key.ProviderKey == providerKey);
        }
    }

    internal int PendingBuckets
    {
        get { lock (_gate) return _active.Count + _inFlight.Count; }
    }

    internal long DroppedObservations => Interlocked.Read(ref _droppedObservations);

    private static List<LatencyFlushItem> OrderItems(IEnumerable<LatencyFlushItem> items) =>
        items
            .OrderBy(item => item.Key.Minute)
            .ThenBy(item => item.Key.ProviderKey, StringComparer.Ordinal)
            .ThenBy(item => item.Key.Phase)
            .ThenBy(item => item.Key.Workload)
            .ThenBy(item => item.Key.Operation)
            .ToList();

    private static void RemoveMatching<TValue>(
        Dictionary<LatencyKey, TValue> dictionary,
        Func<LatencyKey, bool> predicate)
    {
        var remove = dictionary.Keys.Where(predicate).ToList();
        foreach (var key in remove)
            dictionary.Remove(key);
    }

    private sealed class Accumulator
    {
        private readonly long[] _counts = new long[LatencyHistogram.UpperBoundsMs.Length];
        private long _count;
        private long _sumMs;
        private int _maxMs;

        public void Record(long milliseconds)
        {
            var clamped = (int)Math.Clamp(milliseconds, 0, int.MaxValue);
            _counts[LatencyHistogram.IndexOf(milliseconds)]++;
            _count++;
            _sumMs += milliseconds;
            if (clamped > _maxMs) _maxMs = clamped;
        }

        public LatencyAccumulatorSnapshot Snapshot()
        {
            var copy = new long[_counts.Length];
            Array.Copy(_counts, copy, _counts.Length);
            return new LatencyAccumulatorSnapshot(copy, _count, _sumMs, _maxMs);
        }
    }
}
