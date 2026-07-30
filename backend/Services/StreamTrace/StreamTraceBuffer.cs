using System.Collections.Concurrent;
using System.Text.Json;
using NzbWebDAV.Database.Models.Metrics;

namespace NzbWebDAV.Services.StreamTrace;

/// <summary>
/// In-memory ring buffer of playback stream events keyed by ReadSessionId.
/// Same lifetime model as LogBufferSink: process-local, dump before restart.
/// Enablement is runtime-togglable so Docker installs can capture traces without
/// setting STREAM_TRACE_EVENTS and restarting.
/// </summary>
public sealed class StreamTraceBuffer
{
    public const string SourceEnv = "env";
    public const string SourceUi = "ui";
    public const int UiMaxCapacity = 200_000;
    public const int EnvMaxCapacity = 200_000;
    public const int DefaultUiCapacity = 100_000;
    public static readonly int[] AllowedUiMinutes = [15, 30, 60];
    public static readonly int[] AllowedUiCapacities = [20_000, 50_000, 100_000, 200_000];

    private static readonly TimeSpan RetentionWindow = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions CompactJson = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly int _maxSessions;
    private readonly object _gate = new();
    private volatile StreamTraceEvent?[] _buffer = [];
    private long _nextSequence;
    private long _nextRangeGeneration;
    private long _expiresAtUnixMs;
    private long _retainedUntilUnixMs;
    private string _source = SourceEnv;
    private int _capacity;
    private int _recording;

    // Newest session first for summary listing.
    private readonly ConcurrentDictionary<Guid, SessionMeta> _sessions = new();
    private readonly HashSet<Guid> _trimmedSessionIds = new();
    private bool _sessionHistorySaturated;

    public StreamTraceBuffer(int capacity, int maxSessions = 200, bool enabled = true)
    {
        _maxSessions = Math.Max(10, maxSessions);
        if (enabled)
            EnableFor(TimeSpan.Zero, capacity, SourceEnv);
        else
            _capacity = Math.Max(100, capacity);
    }

    public int Capacity
    {
        get
        {
            lock (_gate) return Math.Max(_capacity, 100);
        }
    }

    /// <summary>
    /// Tracing is opt-in. When disabled, Record is a no-op so production
    /// deployments pay no memory or hot-path cost. Expiry is checked lock-free
    /// against a volatile snapshot so the disabled path stays allocation-free.
    /// </summary>
    public bool Enabled
    {
        get
        {
            if (Volatile.Read(ref _recording) != 1) return false;
            var buffer = _buffer;
            if (buffer.Length == 0) return false;
            var expiresAt = Volatile.Read(ref _expiresAtUnixMs);
            return expiresAt == 0 || expiresAt > Now();
        }
    }

    /// <summary>Recording has stopped but the capture is still held for a support pack.</summary>
    public bool HasRetainedEvents =>
        Volatile.Read(ref _recording) == 0
        && _buffer.Length > 0
        && Volatile.Read(ref _nextSequence) > 0;

    /// <summary>
    /// Turns tracing on for <paramref name="ttl"/>. A zero TTL means no expiry
    /// (used by the STREAM_TRACE_EVENTS bootstrap path). UI callers must pass a
    /// positive TTL — the expiry sweeper will StopRecording() when it elapses.
    /// </summary>
    public StreamTraceStatus EnableFor(TimeSpan ttl, int capacity, string source)
    {
        var isUi = string.Equals(source, SourceUi, StringComparison.Ordinal);
        var capped = Math.Clamp(capacity, 100, isUi ? UiMaxCapacity : EnvMaxCapacity);
        var expiresAt = ttl <= TimeSpan.Zero
            ? 0L
            : Now() + (long)ttl.TotalMilliseconds;

        lock (_gate)
        {
            if (_recording == 1)
            {
                // Already capturing (UI re-enable, or UI enable while STREAM_TRACE_EVENTS
                // is recording). Refresh TTL/source only — wiping would destroy the
                // evidence StopRecording/Discard exist to protect.
                _source = isUi ? SourceUi : SourceEnv;
                Volatile.Write(ref _expiresAtUnixMs, expiresAt);
                Volatile.Write(ref _retainedUntilUnixMs, 0);
            }
            else if (_buffer.Length > 0 && _nextSequence > 0)
            {
                // Resume the retained ring exactly as-is. Capacity stays unchanged;
                // especially do not shrink an env-originated capture behind the operator's back.
                _source = isUi ? SourceUi : SourceEnv;
                Volatile.Write(ref _expiresAtUnixMs, expiresAt);
                Volatile.Write(ref _retainedUntilUnixMs, 0);
                Volatile.Write(ref _recording, 1);
            }
            else
            {
                // No retained evidence: start a fresh capture as today.
                _buffer = new StreamTraceEvent?[capped];
                _capacity = capped;
                _source = isUi ? SourceUi : SourceEnv;
                Volatile.Write(ref _expiresAtUnixMs, expiresAt);
                Volatile.Write(ref _retainedUntilUnixMs, 0);
                _sessions.Clear();
                _trimmedSessionIds.Clear();
                _sessionHistorySaturated = false;
                _nextSequence = 0;
                Volatile.Write(ref _recording, 1);
            }
        }

        return GetStatus();
    }

    /// <summary>
    /// Stops recording but keeps the capture addressable so a support pack collected
    /// afterwards still contains it. The natural flow is enable, reproduce, turn off,
    /// download — clearing here silently destroyed the evidence that was just captured.
    /// The retention deadline releases the memory later if nobody discards it.
    /// </summary>
    public StreamTraceStatus StopRecording()
    {
        lock (_gate)
        {
            // Idempotent: repeated "off" calls must not extend the retention deadline.
            if (_recording == 0)
                return GetStatusNoLock();

            Volatile.Write(ref _recording, 0);
            Volatile.Write(ref _expiresAtUnixMs, 0);
            if (_nextSequence > 0)
                Volatile.Write(ref _retainedUntilUnixMs, Now() + (long)RetentionWindow.TotalMilliseconds);
            else
                ReleaseBufferNoLock();

            return GetStatusNoLock();
        }
    }

    /// <summary>
    /// Releases the ring buffer and session index so the GC can reclaim the RAM,
    /// discarding anything captured. Safe to call at any time.
    /// </summary>
    public StreamTraceStatus Discard()
    {
        lock (_gate)
        {
            ReleaseBufferNoLock();
            return GetStatusNoLock();
        }
    }

    /// <summary>
    /// True when a positive TTL has elapsed and StopRecording() has not yet run.
    /// </summary>
    public bool IsExpired
    {
        get
        {
            var expiresAt = Volatile.Read(ref _expiresAtUnixMs);
            return Volatile.Read(ref _recording) != 0
                && expiresAt > 0
                && expiresAt <= Now()
                && _buffer.Length > 0;
        }
    }

    /// <summary>True once the retention deadline has passed and Discard() has not yet run.</summary>
    public bool IsRetentionExpired
    {
        get
        {
            var until = Volatile.Read(ref _retainedUntilUnixMs);
            return until > 0 && until <= Now() && HasRetainedEvents;
        }
    }

    public StreamTraceStatus GetStatus()
    {
        lock (_gate)
            return GetStatusNoLock();
    }

    private StreamTraceStatus GetStatusNoLock()
    {
        var expiresAt = Volatile.Read(ref _expiresAtUnixMs);
        var retainedUntil = Volatile.Read(ref _retainedUntilUnixMs);
        var recording = Volatile.Read(ref _recording) == 1;
        var enabled = recording && _buffer.Length > 0 && (expiresAt == 0 || expiresAt > Now());
        var total = Volatile.Read(ref _nextSequence);
        var ringLength = _buffer.Length;
        var retainedCount = ringLength == 0 ? 0L : Math.Min(total, ringLength);
        var oldestSequence = retainedCount == 0 ? 0L : total - retainedCount + 1;
        var newestSequence = retainedCount == 0 ? 0L : total;
        var oldest = retainedCount == 0
            ? null
            : _buffer[(int)((oldestSequence - 1) % ringLength)];
        var newest = retainedCount == 0
            ? null
            : _buffer[(int)((newestSequence - 1) % ringLength)];
#if DEBUG
        if (oldest is not null)
            System.Diagnostics.Debug.Assert(oldest.Sequence == oldestSequence);
        if (newest is not null)
            System.Diagnostics.Debug.Assert(newest.Sequence == newestSequence);
#endif
        return new StreamTraceStatus(
            Enabled: enabled,
            Source: _source,
            ExpiresAtUnixMs: expiresAt,
            Capacity: Math.Max(_capacity, 100),
            EventCount: total,
            SessionCount: _sessions.Count,
            Retained: !recording && _buffer.Length > 0 && _nextSequence > 0,
            RetainedUntilUnixMs: retainedUntil,
            RetainedEventCount: retainedCount,
            OverwrittenEventCount: Math.Max(0, total - retainedCount),
            OldestRetainedSequence: oldestSequence,
            NewestRetainedSequence: newestSequence,
            OldestRetainedAtUnixMs: oldest?.AtUnixMs ?? 0,
            NewestRetainedAtUnixMs: newest?.AtUnixMs ?? 0);
    }

    private void ReleaseBufferNoLock()
    {
        _buffer = [];
        Volatile.Write(ref _recording, 0);
        Volatile.Write(ref _expiresAtUnixMs, 0);
        Volatile.Write(ref _retainedUntilUnixMs, 0);
        _sessions.Clear();
        _trimmedSessionIds.Clear();
        _sessionHistorySaturated = false;
        _nextSequence = 0;
    }

    internal void ExpireRetentionForTests()
    {
        lock (_gate)
        {
            if (_recording == 0 && _buffer.Length > 0 && _nextSequence > 0)
                Volatile.Write(ref _retainedUntilUnixMs, Now() - 1);
        }
    }

    private SessionMeta? Record(StreamTraceEvent entry)
    {
        if (!Enabled) return null;
        lock (_gate)
        {
            var expiresAt = Volatile.Read(ref _expiresAtUnixMs);
            if (_recording != 1
                || _buffer.Length == 0
                || (expiresAt > 0 && expiresAt <= Now()))
                return null;

            var sequence = ++_nextSequence;
            var withSeq = entry with { Sequence = sequence };
            var buffer = _buffer;
            buffer[(sequence - 1) % buffer.Length] = withSeq;

            var session = _sessions.AddOrUpdate(
                entry.SessionId,
                _ => new SessionMeta
                {
                    SessionId = entry.SessionId,
                    FirstAt = entry.AtUnixMs,
                    LastAt = entry.AtUnixMs,
                    Path = entry.Path,
                    EventCount = 1,
                    EventCountKnown = !_sessionHistorySaturated
                        && !_trimmedSessionIds.Contains(entry.SessionId),
                    LastKind = entry.Kind,
                },
                (_, existing) =>
                {
                    existing.LastAt = entry.AtUnixMs;
                    existing.EventCount++;
                    existing.LastKind = entry.Kind;
                    if (!string.IsNullOrEmpty(entry.Path)) existing.Path = entry.Path;
                    return existing;
                });

            TrimSessionsIfNeeded();
            return session;
        }
    }

    /// <summary>
    /// Opens a trace generation for this exact HTTP range and returns the token the
    /// request must carry through its async flow. Returning null means tracing is off.
    /// </summary>
    public StreamTraceRangeContext? RangeOpen(
        Guid sessionId,
        string path,
        string method,
        long rangeStart,
        long? rangeEnd,
        long? fileSize,
        string? userAgent,
        string? clientIp)
    {
        if (!Enabled) return null;
        var generation = Interlocked.Increment(ref _nextRangeGeneration);
        var session = Record(new StreamTraceEvent
        {
            Sequence = 0,
            AtUnixMs = Now(),
            SessionId = sessionId,
            Kind = StreamTraceKind.RangeOpen.ToString(),
            Path = path,
            Method = method,
            RangeStart = rangeStart,
            RangeEnd = rangeEnd,
            FileSize = fileSize,
            UserAgent = userAgent,
            ClientIp = clientIp,
            RangeGeneration = generation,
        });
        if (session is null)
            return null;
        session.OpenGeneration(generation);
        return new StreamTraceRangeContext(sessionId, generation);
    }

    public void Seek(Guid sessionId, long offset)
    {
        Record(new StreamTraceEvent
        {
            Sequence = 0,
            AtUnixMs = Now(),
            SessionId = sessionId,
            Kind = StreamTraceKind.Seek.ToString(),
            Offset = offset,
        });
    }

    public void Segment(
        Guid sessionId,
        string provider,
        SegmentFetch.FetchStatus status,
        int durationMs,
        int retries,
        string? segmentId = null)
    {
        Record(new StreamTraceEvent
        {
            Sequence = 0,
            AtUnixMs = Now(),
            SessionId = sessionId,
            Kind = StreamTraceKind.Segment.ToString(),
            Provider = provider,
            Status = StreamTraceEvent.StatusName(status),
            DurationMs = durationMs,
            Retries = retries,
            SegmentId = StreamTraceEvent.TruncateSegmentId(segmentId),
        });
    }

    public void ZeroFill(Guid sessionId, string segmentId, long bytes, string? message = null)
    {
        Record(new StreamTraceEvent
        {
            Sequence = 0,
            AtUnixMs = Now(),
            SessionId = sessionId,
            Kind = StreamTraceKind.ZeroFill.ToString(),
            SegmentId = StreamTraceEvent.TruncateSegmentId(segmentId),
            Bytes = bytes,
            Message = message,
        });
    }

    public void Failover(Guid sessionId, string fromProvider, string toProvider, string? reason = null)
    {
        Record(new StreamTraceEvent
        {
            Sequence = 0,
            AtUnixMs = Now(),
            SessionId = sessionId,
            Kind = StreamTraceKind.Failover.ToString(),
            FromProvider = fromProvider,
            ToProvider = toProvider,
            Status = reason,
        });
    }

    public void Retry(Guid sessionId, string segmentId, int attempt, string? message = null)
    {
        Record(new StreamTraceEvent
        {
            Sequence = 0,
            AtUnixMs = Now(),
            SessionId = sessionId,
            Kind = StreamTraceKind.Retry.ToString(),
            SegmentId = StreamTraceEvent.TruncateSegmentId(segmentId),
            Attempt = attempt,
            Message = message,
        });
    }

    public void PrefetchWidth(Guid sessionId, int previousBatchSize, int batchSize)
    {
        Record(new StreamTraceEvent
        {
            Sequence = 0,
            AtUnixMs = Now(),
            SessionId = sessionId,
            Kind = StreamTraceKind.PrefetchWidth.ToString(),
            PreviousBatchSize = previousBatchSize,
            BatchSize = batchSize,
        });
    }

    /// <summary>
    /// Adds time spent blocked on <paramref name="kind"/> to the range identified by
    /// <paramref name="range"/>. Ticks are accumulated rather than milliseconds so
    /// the many sub-millisecond client writes in a range still add up. No-ops when
    /// the token is null or the generation bucket is gone.
    /// </summary>
    public void AddStall(StreamTraceRangeContext? range, StreamStallKind kind, TimeSpan elapsed)
    {
        if (range is not { } value) return;
        if (elapsed <= TimeSpan.Zero) return;
        if (!_sessions.TryGetValue(value.SessionId, out var session)) return;
        session.Bucket(value.Generation)?.Add(kind, elapsed.Ticks);
    }

    /// <summary>
    /// Records provider-wait time for a fetch started under <paramref name="range"/>.
    /// Late completions still credit the originating generation via the live totals
    /// referenced by that range's <c>RangeEnd</c> event.
    /// </summary>
    public void AddFetchWait(StreamTraceRangeContext? range, TimeSpan elapsed)
    {
        if (range is not { } value) return;
        if (!_sessions.TryGetValue(value.SessionId, out var session)) return;
        session.Bucket(value.Generation)?.AddFetch(Math.Max(0, elapsed.Ticks));
    }

    /// <summary>
    /// Records a connection acquisition for the given range: how long the borrower
    /// waited, and whether the pool handed back an idle connection or had to open a
    /// new one. A range full of fresh handshakes points at connection churn.
    /// </summary>
    public void ConnectionAcquired(StreamTraceRangeContext? range, TimeSpan wait, bool wasReused)
    {
        if (range is not { } value) return;
        if (!_sessions.TryGetValue(value.SessionId, out var session)) return;
        session.Bucket(value.Generation)?.AddConnection(wait.Ticks, wasReused);
    }

    /// <summary>
    /// Records how a range finished. <paramref name="range"/> may be null when no range was
    /// opened — tracing started mid-read, or the read timed out while the stream was still
    /// opening. That terminal event is the most useful one in the trace, so it is still
    /// recorded; it simply carries no generation and no stall attribution.
    /// </summary>
    public void RangeEnd(
        Guid sessionId,
        StreamTraceRangeContext? range,
        ReadSession.EndReasonCode endReason,
        long bytesServed,
        string? message = null)
    {
        var stalls = range is { } value && _sessions.TryGetValue(value.SessionId, out var session)
            ? session.Bucket(value.Generation)
            : null;

        Record(new StreamTraceEvent
        {
            Sequence = 0,
            AtUnixMs = Now(),
            SessionId = sessionId,
            Kind = StreamTraceKind.RangeEnd.ToString(),
            EndReason = StreamTraceEvent.EndReasonName(endReason),
            BytesServed = bytesServed,
            Message = message,
            RangeGeneration = range?.Generation,
            RangeStalls = stalls,
        });
    }

    public IReadOnlyList<StreamTraceSessionSummary> ListSessions(int limit = 50)
    {
        return _sessions.Values
            .OrderByDescending(s => s.LastAt)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(s => new StreamTraceSessionSummary(
                s.SessionId,
                s.Path,
                s.FirstAt,
                s.LastAt,
                s.EventCount,
                s.LastKind))
            .ToList();
    }

    public IReadOnlyList<StreamTraceEvent> GetSessionEvents(Guid sessionId)
    {
        lock (_gate)
        {
            return WalkRetainedEventsNoLock()
                .Where(e => e.SessionId == sessionId)
                .ToList();
        }
    }

    /// <summary>
    /// Newest <paramref name="limit"/> events across all sessions, oldest-first within
    /// the returned window. Used by the support pack exporter and scripts.
    /// </summary>
    public IReadOnlyList<StreamTraceEvent> GetRecentEvents(int limit)
    {
        lock (_gate)
        {
            if (_buffer.Length == 0) return [];
            var max = Math.Clamp(limit, 1, Math.Max(_buffer.Length, 1));
            var all = WalkRetainedEventsNoLock();
            if (all.Count <= max) return all;
            return all.Skip(all.Count - max).ToList();
        }
    }

    /// <summary>
    /// Serialize recent events as newline-delimited JSON. Prefer
    /// <see cref="CaptureSnapshot"/> + line-by-line export for large packs.
    /// </summary>
    public string FormatEventsJsonl(int limit)
    {
        var events = GetRecentEvents(limit);
        if (events.Count == 0) return "";
        return string.Join('\n', events.Select(e => JsonSerializer.Serialize(e.FreezeForExport(), CompactJson))) + "\n";
    }

    /// <summary>
    /// Atomically capture status, retained events, and per-session export summaries
    /// for a support pack. Holds <c>_gate</c> only for reference/scalar copies.
    /// </summary>
    internal StreamTraceSnapshot CaptureSnapshot(int sessionLimit = 50)
    {
        StreamTraceStatus status;
        List<StreamTraceEvent> events;
        List<(Guid SessionId, string? Path, long FirstAt, long LastAt, int EventCount, bool EventCountKnown, string? LastKind)> sessionScalars;
        bool overflowed;

        lock (_gate)
        {
            status = GetStatusNoLock();
            overflowed = status.Overflowed;
            events = WalkRetainedEventsNoLock();
            sessionScalars = _sessions.Values
                .Select(s => (
                    s.SessionId,
                    s.Path,
                    s.FirstAt,
                    s.LastAt,
                    s.EventCount,
                    s.EventCountKnown,
                    s.LastKind))
                .ToList();
        }

        var retainedBySession = events
            .GroupBy(e => e.SessionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var candidates = new Dictionary<Guid, StreamTraceExportSessionSummary>();
        foreach (var meta in sessionScalars)
        {
            retainedBySession.TryGetValue(meta.SessionId, out var retainedEvents);
            var retainedCount = retainedEvents?.Count ?? 0;
            candidates[meta.SessionId] = BuildExportSummary(
                meta.SessionId,
                meta.Path,
                meta.FirstAt,
                meta.LastAt,
                meta.EventCount,
                meta.EventCountKnown,
                meta.LastKind,
                retainedCount,
                overflowed);
        }

        foreach (var (sessionId, retainedEvents) in retainedBySession)
        {
            if (candidates.ContainsKey(sessionId)) continue;
            var first = retainedEvents[0];
            var last = retainedEvents[^1];
            candidates[sessionId] = BuildExportSummary(
                sessionId,
                retainedEvents.Select(e => e.Path).LastOrDefault(p => !string.IsNullOrEmpty(p)) ?? first.Path,
                first.AtUnixMs,
                last.AtUnixMs,
                eventCount: null,
                eventCountKnown: false,
                last.Kind,
                retainedEvents.Count,
                overflowed);
        }

        var sessions = candidates.Values
            .OrderByDescending(s => s.LastAt)
            .Take(Math.Clamp(sessionLimit, 1, 500))
            .ToList();

        return new StreamTraceSnapshot(
            status,
            RetainedSessionCount: retainedBySession.Count,
            Sessions: sessions,
            Events: events);
    }

    private static StreamTraceExportSessionSummary BuildExportSummary(
        Guid sessionId,
        string? path,
        long firstAt,
        long lastAt,
        int? eventCount,
        bool eventCountKnown,
        string? lastKind,
        int retainedCount,
        bool overflowed)
    {
        if (eventCountKnown && eventCount is int known)
        {
            return new StreamTraceExportSessionSummary(
                sessionId, path, firstAt, lastAt,
                EventCount: known,
                RetainedEventCount: retainedCount,
                EventsComplete: retainedCount == known,
                LastKind: lastKind);
        }

        if (!overflowed)
        {
            return new StreamTraceExportSessionSummary(
                sessionId, path, firstAt, lastAt,
                EventCount: retainedCount,
                RetainedEventCount: retainedCount,
                EventsComplete: true,
                LastKind: lastKind);
        }

        return new StreamTraceExportSessionSummary(
            sessionId, path, firstAt, lastAt,
            EventCount: null,
            RetainedEventCount: retainedCount,
            EventsComplete: false,
            LastKind: lastKind);
    }

    private List<StreamTraceEvent> WalkRetainedEventsNoLock()
    {
        if (_buffer.Length == 0) return [];
        var status = GetStatusNoLock();
        if (status.RetainedEventCount == 0) return [];
        var result = new List<StreamTraceEvent>((int)status.RetainedEventCount);
        for (var sequence = status.OldestRetainedSequence; sequence <= status.NewestRetainedSequence; sequence++)
        {
            var entry = _buffer[(int)((sequence - 1) % _buffer.Length)];
            if (entry is null) continue;
            result.Add(entry);
        }
        return result;
    }

    private void TrimSessionsIfNeeded()
    {
        if (_sessions.Count <= _maxSessions) return;
        var excess = _sessions.Values
            .OrderBy(s => s.LastAt)
            .Take(_sessions.Count - _maxSessions)
            .Select(s => s.SessionId)
            .ToList();
        var tombstoneCap = _maxSessions * 4;
        foreach (var id in excess)
        {
            if (_sessions.TryRemove(id, out _))
            {
                if (_sessionHistorySaturated) continue;
                if (_trimmedSessionIds.Count >= tombstoneCap)
                {
                    _sessionHistorySaturated = true;
                    _trimmedSessionIds.Clear();
                }
                else
                {
                    _trimmedSessionIds.Add(id);
                }
            }
        }
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private sealed class SessionMeta
    {
        // Ranges are attributed by generation because a fetch started in one range can
        // resolve after it ended. A handful of closed generations stay addressable so
        // late completions can still update the RangeEnd event that references that bucket.
        // Sixteen remains tiny but avoids dropping ordinary rapid-scrub completions.
        private const int RetainedGenerations = 16;
        private readonly object _generationGate = new();
        private readonly Dictionary<long, StreamTraceRangeStalls> _buckets = new();

        public Guid SessionId { get; init; }
        public long FirstAt { get; set; }
        public long LastAt { get; set; }
        public string? Path { get; set; }
        public int EventCount { get; set; }
        public bool EventCountKnown { get; set; } = true;
        public string? LastKind { get; set; }

        public void OpenGeneration(long generation)
        {
            lock (_generationGate)
            {
                _buckets[generation] = new StreamTraceRangeStalls();
                while (_buckets.Count > RetainedGenerations)
                    _buckets.Remove(_buckets.Keys.Min());
            }
        }

        public StreamTraceRangeStalls? Bucket(long generation)
        {
            if (generation <= 0) return null;
            lock (_generationGate) return _buckets.GetValueOrDefault(generation);
        }
    }
}

public sealed record StreamTraceSessionSummary(
    Guid SessionId,
    string? Path,
    long FirstAt,
    long LastAt,
    int EventCount,
    string? LastKind);
