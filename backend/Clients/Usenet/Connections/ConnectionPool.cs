using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using NzbWebDAV.Clients.Usenet.Concurrency;
using NzbWebDAV.Clients.Usenet.Contexts;

namespace NzbWebDAV.Clients.Usenet.Connections;

/// <summary>
/// Lifetime connection churn for one pool. Distinguishes a pool that opened its
/// connections once from one that keeps replacing them, which the live/idle
/// gauges cannot show.
/// </summary>
public sealed record ConnectionPoolChurn(
    long ConnectionsOpened,
    long ConnectionsReused,
    long ConnectionsDestroyed,
    long StaleEvictions,
    long HandshakeFailures,
    long GateWaitMs,
    long HandshakeWaitMs);

/// <summary>
/// Thread-safe, lazy connection pool.
/// <para>
/// *  Connections are created through a user-supplied factory (sync or async).<br/>
/// *  At most <c>maxConnections</c> live instances exist at any time.<br/>
/// *  Concurrent factory invocations (connect+auth) are capped so a cold burst
///    ramps the pool instead of opening dozens of TLS handshakes at once.<br/>
/// *  Idle connections older than <see cref="IdleTimeout"/> are disposed
///    automatically by a background sweeper.<br/>
/// *  <see cref="Dispose"/> / <see cref="DisposeAsync"/> stop the sweeper and
///    dispose all cached connections.  Borrowed handles returned afterwards are
///    destroyed immediately.
/// *  Note: This class was authored by ChatGPT 3o
/// </para>
/// </summary>
public sealed class ConnectionPool<T> : IDisposable, IAsyncDisposable
{
    /* -------------------------------- configuration -------------------------------- */

    /// <summary>
    /// Caps simultaneous connect+auth factory calls so a cold burst of borrowers
    /// ramps the pool instead of slamming dozens of TLS handshakes at once.
    /// </summary>
    private const int MaxConcurrentHandshakes = 3;

    public TimeSpan IdleTimeout { get; }
    public int MaxConnections => _maxConnections;
    public int LiveConnections => _live;
    public int IdleConnections => _idleConnections.Count;
    public int ActiveConnections => _live - _idleConnections.Count;
    public int AvailableConnections => _maxConnections - ActiveConnections;

    public event EventHandler<ConnectionPoolStats.ConnectionPoolChangedEventArgs>? OnConnectionPoolChanged;

    private readonly Func<CancellationToken, ValueTask<T>> _factory;
    private readonly int _maxConnections;

    /* --------------------------------- state --------------------------------------- */

    private readonly ConcurrentStack<Pooled> _idleConnections = new();
    private readonly PrioritizedSemaphore _gate;
    private readonly SemaphoreSlim _handshakeGate = new(MaxConcurrentHandshakes, MaxConcurrentHandshakes);
    private readonly CancellationTokenSource _sweepCts = new();
    private readonly Task _sweeperTask; // keeps timer alive

    private int _live; // number of connections currently alive
    private int _disposed; // 0 == false, 1 == true

    // Lifetime churn counters. A pool that keeps destroying and re-opening connections
    // pays the handshake cost repeatedly and can never reach its configured width, which
    // is invisible from the live/idle gauges alone.
    private long _connectionsOpened;
    private long _connectionsReused;
    private long _connectionsDestroyed;
    private long _staleEvictions;
    private long _handshakeFailures;
    private long _gateWaitTicks;
    private long _handshakeWaitTicks;

    public ConnectionPoolChurn GetChurn() => new(
        ConnectionsOpened: Interlocked.Read(ref _connectionsOpened),
        ConnectionsReused: Interlocked.Read(ref _connectionsReused),
        ConnectionsDestroyed: Interlocked.Read(ref _connectionsDestroyed),
        StaleEvictions: Interlocked.Read(ref _staleEvictions),
        HandshakeFailures: Interlocked.Read(ref _handshakeFailures),
        GateWaitMs: Interlocked.Read(ref _gateWaitTicks) / TimeSpan.TicksPerMillisecond,
        HandshakeWaitMs: Interlocked.Read(ref _handshakeWaitTicks) / TimeSpan.TicksPerMillisecond);

    /* ------------------------------------------------------------------------------ */

    public ConnectionPool(
        int maxConnections,
        Func<CancellationToken, ValueTask<T>> connectionFactory,
        TimeSpan? idleTimeout = null,
        SemaphorePriorityOdds? priorityOdds = null)
    {
        if (maxConnections <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConnections));

        _factory = connectionFactory
                   ?? throw new ArgumentNullException(nameof(connectionFactory));
        // Keep this below typical NNTP server-side idle timeouts (30-180s);
        // connections idled longer are closed by the server and fail on next use.
        IdleTimeout = idleTimeout ?? TimeSpan.FromSeconds(60);
        if (IdleTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(idleTimeout));

        _maxConnections = maxConnections;
        _gate = new PrioritizedSemaphore(maxConnections, maxConnections, priorityOdds);
        _sweeperTask = Task.Run(SweepLoop); // background idle-reaper
    }

    /// <summary>
    /// Re-arms the gate's High-vs-Low admission odds (Streaming Priority) in place, so a
    /// settings save changes contention behavior without replacing live TLS connections.
    /// </summary>
    public void UpdatePriorityOdds(SemaphorePriorityOdds odds) => _gate.UpdatePriorityOdds(odds);

    /* ============================== public API ==================================== */

    /// <summary>
    /// Borrow a connection while reserving capacity for higher-priority callers.
    /// Waits until at least (`reservedCount` + 1) slots are free before acquiring one,
    /// ensuring that after acquisition at least `reservedCount` remain available.
    /// </summary>
    public async Task<ConnectionLock<T>> GetConnectionLockAsync
    (
        SemaphorePriority priority,
        CancellationToken cancellationToken = default
    )
    {
        // Make caller cancellation also cancel the wait on the gate.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _sweepCts.Token);

        var gateWaitStarted = Stopwatch.GetTimestamp();
        await _gate.WaitAsync(priority, linked.Token).ConfigureAwait(false);
        Interlocked.Add(ref _gateWaitTicks, Stopwatch.GetElapsedTime(gateWaitStarted).Ticks);

        // Pool might have been disposed after wait returned:
        if (Volatile.Read(ref _disposed) == 1)
        {
            _gate.Release();
            ThrowDisposed();
        }

        // Try to reuse an existing idle connection.
        if (TryTakeIdleConnection(out var reused))
        {
            Interlocked.Increment(ref _connectionsReused);
            TriggerConnectionPoolChangedEvent();
            return BuildLock(reused, wasReused: true);
        }

        // Need a fresh connection. Pace handshakes so a cold burst of borrowers
        // does not open dozens of TLS sessions in parallel. While waiting, other
        // connections may return to the idle stack — prefer those over a new handshake.
        try
        {
            var handshakeWaitStarted = Stopwatch.GetTimestamp();
            await _handshakeGate.WaitAsync(linked.Token).ConfigureAwait(false);
            Interlocked.Add(ref _handshakeWaitTicks, Stopwatch.GetElapsedTime(handshakeWaitStarted).Ticks);
        }
        catch
        {
            _gate.Release();
            throw;
        }

        try
        {
            if (Volatile.Read(ref _disposed) == 1)
            {
                _gate.Release();
                ThrowDisposed();
            }

            if (TryTakeIdleConnection(out reused))
            {
                Interlocked.Increment(ref _connectionsReused);
                TriggerConnectionPoolChangedEvent();
                return BuildLock(reused, wasReused: true);
            }

            T conn;
            try
            {
                conn = await _factory(linked.Token).ConfigureAwait(false);
            }
            catch
            {
                Interlocked.Increment(ref _handshakeFailures);
                _gate.Release(); // free the permit on failure
                throw;
            }

            Interlocked.Increment(ref _connectionsOpened);
            Interlocked.Increment(ref _live);
            TriggerConnectionPoolChangedEvent();
            return BuildLock(conn, wasReused: false);
        }
        finally
        {
            _handshakeGate.Release();
        }

        ConnectionLock<T> BuildLock(T c, bool wasReused)
            => new(c, Return, Destroy, wasReused);

        static void ThrowDisposed()
            => throw new ObjectDisposedException(nameof(ConnectionPool<T>));
    }

    private bool TryTakeIdleConnection(out T connection)
    {
        while (_idleConnections.TryPop(out var item))
        {
            if (!item.IsExpired(IdleTimeout))
            {
                connection = item.Connection;
                return true;
            }

            // Stale – destroy and continue looking.
            DisposeConnection(item.Connection);
            Interlocked.Decrement(ref _live);
            Interlocked.Increment(ref _staleEvictions);
            TriggerConnectionPoolChangedEvent();
        }

        connection = default!;
        return false;
    }

    /* ========================== core helpers ====================================== */

    private readonly record struct Pooled(T Connection, long LastTouchedMillis)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsExpired(TimeSpan idle, long nowMillis = 0)
        {
            if (nowMillis == 0) nowMillis = Environment.TickCount64;
            return unchecked(nowMillis - LastTouchedMillis) >= idle.TotalMilliseconds;
        }
    }

    private void Return(T connection)
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            DisposeConnection(connection);
            Interlocked.Decrement(ref _live);
            TriggerConnectionPoolChangedEvent();
            return;
        }

        _idleConnections.Push(new Pooled(connection, Environment.TickCount64));
        _gate.Release();
        TriggerConnectionPoolChangedEvent();
    }

    private void Destroy(T connection)
    {
        // When a lock requests replacement, we dispose the connection instead of reusing.
        DisposeConnection(connection);
        Interlocked.Decrement(ref _live);
        Interlocked.Increment(ref _connectionsDestroyed);
        if (Volatile.Read(ref _disposed) == 0)
        {
            _gate.Release();
        }

        TriggerConnectionPoolChangedEvent();
    }

    private void TriggerConnectionPoolChangedEvent()
    {
        OnConnectionPoolChanged?.Invoke(this, new ConnectionPoolStats.ConnectionPoolChangedEventArgs(
            _live,
            _idleConnections.Count,
            _maxConnections
        ));
    }

    /* =================== idle sweeper (background) ================================= */

    private async Task SweepLoop()
    {
        try
        {
            using var timer = new PeriodicTimer(IdleTimeout / 2);
            while (await timer.WaitForNextTickAsync(_sweepCts.Token).ConfigureAwait(false))
                SweepOnce();
        }
        catch (OperationCanceledException)
        {
            /* normal on disposal */
        }
    }

    private void SweepOnce()
    {
        var now = Environment.TickCount64;
        var survivors = new List<Pooled>();
        var isAnyConnectionFreed = false;

        while (_idleConnections.TryPop(out var item))
        {
            if (item.IsExpired(IdleTimeout, now))
            {
                DisposeConnection(item.Connection);
                Interlocked.Decrement(ref _live);
                isAnyConnectionFreed = true;
            }
            else
            {
                survivors.Add(item);
            }
        }

        // Preserve original LIFO order.
        for (var i = survivors.Count - 1; i >= 0; i--)
            _idleConnections.Push(survivors[i]);

        if (isAnyConnectionFreed)
            TriggerConnectionPoolChangedEvent();
    }

    /* ------------------------- dispose helpers ------------------------------------ */

    private static void DisposeConnection(T conn)
    {
        if (conn is IDisposable d)
            d.Dispose();
    }

    /* -------------------------- IAsyncDisposable ---------------------------------- */

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        await _sweepCts.CancelAsync();

        try
        {
            await _sweeperTask.ConfigureAwait(false); // await clean sweep exit
        }
        catch (OperationCanceledException)
        {
            /* ignore */
        }

        // Drain and dispose cached items.
        while (_idleConnections.TryPop(out var item))
            DisposeConnection(item.Connection);

        _sweepCts.Dispose();
        _gate.Dispose();
        _handshakeGate.Dispose();
        GC.SuppressFinalize(this);
    }

    /* ----------------------------- IDisposable ------------------------------------ */

    public void Dispose()
    {
        _ = DisposeAsync().AsTask(); // fire-and-forget synchronous path
    }
}
