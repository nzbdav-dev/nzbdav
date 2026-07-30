namespace NzbWebDAV.Services.StreamTrace;

/// <summary>
/// Identifies the HTTP range generation that started a fetch or stall measurement.
/// Captured when the stopwatch starts so late completions bill the originating range.
/// </summary>
public readonly record struct StreamTraceRangeContext(Guid SessionId, long Generation);

/// <summary>
/// Live per-range stall totals. <see cref="StreamTraceEvent"/> holds a reference so
/// completions after <c>RangeEnd</c> still update the exported event.
/// </summary>
internal sealed class StreamTraceRangeStalls
{
    private long _connectionWaitTicks;
    private long _providerWaitTicks;
    private long _bodyDrainTicks;
    private long _consumerWaitTicks;
    private long _clientWriteTicks;
    private long _connectionsOpened;
    private long _connectionsReused;
    private long _fetches;

    public long? ConnectionWaitMs => Milliseconds(Interlocked.Read(ref _connectionWaitTicks));
    public long? ProviderWaitMs => Milliseconds(Interlocked.Read(ref _providerWaitTicks));
    public long? BodyDrainMs => Milliseconds(Interlocked.Read(ref _bodyDrainTicks));
    public long? ConsumerWaitMs => Milliseconds(Interlocked.Read(ref _consumerWaitTicks));
    public long? ClientWriteMs => Milliseconds(Interlocked.Read(ref _clientWriteTicks));
    public long? ConnectionsOpened => Count(Interlocked.Read(ref _connectionsOpened));
    public long? ConnectionsReused => Count(Interlocked.Read(ref _connectionsReused));
    public long? Fetches => Count(Interlocked.Read(ref _fetches));

    public void Add(StreamStallKind kind, long ticks)
    {
        switch (kind)
        {
            case StreamStallKind.ConnectionWait:
                Interlocked.Add(ref _connectionWaitTicks, ticks);
                break;
            case StreamStallKind.ProviderWait:
                Interlocked.Add(ref _providerWaitTicks, ticks);
                break;
            case StreamStallKind.BodyDrain:
                Interlocked.Add(ref _bodyDrainTicks, ticks);
                break;
            case StreamStallKind.ConsumerWait:
                Interlocked.Add(ref _consumerWaitTicks, ticks);
                break;
            case StreamStallKind.ClientWrite:
                Interlocked.Add(ref _clientWriteTicks, ticks);
                break;
        }
    }

    public void AddConnection(long waitTicks, bool wasReused)
    {
        if (waitTicks > 0) Interlocked.Add(ref _connectionWaitTicks, waitTicks);
        if (wasReused)
            Interlocked.Increment(ref _connectionsReused);
        else
            Interlocked.Increment(ref _connectionsOpened);
    }

    public void AddFetch(long providerWaitTicks)
    {
        if (providerWaitTicks > 0)
            Interlocked.Add(ref _providerWaitTicks, providerWaitTicks);
        Interlocked.Increment(ref _fetches);
    }

    /// <summary>
    /// Point-in-time copy of stall totals so export serialization cannot observe
    /// late fetch completions that arrive after the line is written.
    /// </summary>
    public StreamTraceRangeStallsSnapshot Snapshot() => new(
        ConnectionWaitMs: ConnectionWaitMs,
        ProviderWaitMs: ProviderWaitMs,
        BodyDrainMs: BodyDrainMs,
        ConsumerWaitMs: ConsumerWaitMs,
        ClientWriteMs: ClientWriteMs,
        ConnectionsOpened: ConnectionsOpened,
        ConnectionsReused: ConnectionsReused,
        Fetches: Fetches);

    private static long? Milliseconds(long ticks) =>
        ticks <= 0 ? null : ticks / TimeSpan.TicksPerMillisecond;

    private static long? Count(long value) => value <= 0 ? null : value;
}

/// <summary>Immutable stall totals captured at export time.</summary>
internal sealed record StreamTraceRangeStallsSnapshot(
    long? ConnectionWaitMs,
    long? ProviderWaitMs,
    long? BodyDrainMs,
    long? ConsumerWaitMs,
    long? ClientWriteMs,
    long? ConnectionsOpened,
    long? ConnectionsReused,
    long? Fetches);
