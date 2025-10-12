using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using NzbWebDAV.Extensions;
using Serilog;

namespace NzbWebDAV.Clients.Connections;

/// <summary>
/// Thread-safe, lazy connection pool.
/// <para>
/// *  Connections are created through a user-supplied factory (sync or async).<br/>
/// *  At most <c>maxConnections</c> live instances exist at any time.<br/>
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

    public TimeSpan IdleTimeout { get; }
    public event EventHandler<ConnectionPoolChangedEventArgs>? OnConnectionPoolChanged;

    private readonly Func<CancellationToken, ValueTask<T>> _factory;
    private readonly int _maxConnections;

    /* --------------------------------- state --------------------------------------- */

    private readonly ConcurrentStack<Pooled> _idleConnections = new();
    private readonly ExtendedSemaphoreSlim _gate;
    private readonly CancellationTokenSource _sweepCts = new();
    private readonly Task _sweeperTask; // keeps timer alive
    private readonly Task _healthCheckTask; // keeps health checker alive

    private int _live; // number of connections currently alive
    private int _disposed; // 0 == false, 1 == true

    /* ------------------------------------------------------------------------------ */

    public ConnectionPool(
        int maxConnections,
        Func<CancellationToken, ValueTask<T>> connectionFactory,
        TimeSpan? idleTimeout = null)
    {
        if (maxConnections <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConnections));

        _factory = connectionFactory
                   ?? throw new ArgumentNullException(nameof(connectionFactory));
        IdleTimeout = idleTimeout ?? TimeSpan.FromSeconds(30);

        _maxConnections = maxConnections;
        _gate = new ExtendedSemaphoreSlim(maxConnections, maxConnections);
        _sweeperTask = Task.Run(SweepLoop); // background idle-reaper
        _healthCheckTask = Task.Run(HealthCheckLoop); // background health checker
    }

    /* ============================== public API ==================================== */

    /// <summary>
    /// Borrow a connection while reserving capacity for higher-priority callers.
    /// Waits until at least (`reservedCount` + 1) slots are free before acquiring one,
    /// ensuring that after acquisition at least `reservedCount` remain available.
    /// </summary>
    public async Task<ConnectionLock<T>> GetConnectionLockAsync(
        CancellationToken cancellationToken = default)
    {
        var reservedCount = cancellationToken.GetContext<ReservedConnectionsContext>().Count;
        if (reservedCount < 0 || reservedCount > _maxConnections)
            reservedCount = 0;

        // Make caller cancellation also cancel the wait on the gate.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _sweepCts.Token);

        await _gate.WaitAsync(reservedCount, linked.Token).ConfigureAwait(false);

        // Pool might have been disposed after wait returned:
        if (Volatile.Read(ref _disposed) == 1)
        {
            _gate.Release();
            ThrowDisposed();
        }

        // Try to reuse an existing idle connection.
        while (_idleConnections.TryPop(out var item))
        {
            if (!item.IsExpired(IdleTimeout))
            {
                TriggerConnectionPoolChangedEvent();
                return BuildLock(item.Connection);
            }

            // Stale – destroy and continue looking.
            await DisposeConnectionAsync(item.Connection).ConfigureAwait(false);
            Interlocked.Decrement(ref _live);
            TriggerConnectionPoolChangedEvent();
        }

        // Need a fresh connection.
        T conn;
        try
        {
            conn = await _factory(linked.Token).ConfigureAwait(false);
        }
        catch
        {
            _gate.Release(); // free the permit on failure
            throw;
        }

        Interlocked.Increment(ref _live);
        TriggerConnectionPoolChangedEvent();
        return BuildLock(conn);

        ConnectionLock<T> BuildLock(T c)
            => new ConnectionLock<T>(c, Return, ReturnAsync, Destroy, DestroyAsync);

        static void ThrowDisposed()
            => throw new ObjectDisposedException(nameof(ConnectionPool<T>));
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
            _ = DisposeConnectionAsync(connection); // fire & forget
            Interlocked.Decrement(ref _live);
            TriggerConnectionPoolChangedEvent();
            return;
        }

        _idleConnections.Push(new Pooled(connection, Environment.TickCount64));
        _gate.Release();
        TriggerConnectionPoolChangedEvent();
    }

    private ValueTask ReturnAsync(T connection)
    {
        Return(connection);
        return ValueTask.CompletedTask;
    }

    private void Destroy(T connection)
    {
        // When a lock requests replacement, we dispose the connection instead of reusing.
        _ = DisposeConnectionAsync(connection); // fire & forget
        Interlocked.Decrement(ref _live);
        if (Volatile.Read(ref _disposed) == 0)
        {
            _gate.Release();
        }
        TriggerConnectionPoolChangedEvent();
    }

    private ValueTask DestroyAsync(T connection)
    {
        _ = DisposeConnectionAsync(connection); // fire & forget
        Interlocked.Decrement(ref _live);
        if (Volatile.Read(ref _disposed) == 0)
        {
            _gate.Release();
        }
        TriggerConnectionPoolChangedEvent();
        return ValueTask.CompletedTask;
    }

    private void TriggerConnectionPoolChangedEvent()
    {
        OnConnectionPoolChanged?.Invoke(this, new ConnectionPoolChangedEventArgs(
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
                await SweepOnce().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            /* normal on disposal */
        }
    }

    private async Task SweepOnce()
    {
        var now = Environment.TickCount64;
        var survivors = new List<Pooled>();
        var isAnyConnectionFreed = false;

        while (_idleConnections.TryPop(out var item))
        {
            if (item.IsExpired(IdleTimeout, now))
            {
                await DisposeConnectionAsync(item.Connection).ConfigureAwait(false);
                Interlocked.Decrement(ref _live);
                isAnyConnectionFreed = true;
            }
            else
            {
                survivors.Add(item);
            }
        }

        // Preserve original LIFO order.
        for (int i = survivors.Count - 1; i >= 0; i--)
            _idleConnections.Push(survivors[i]);

        if (isAnyConnectionFreed)
            TriggerConnectionPoolChangedEvent();
    }

    /* =================== health checker (background) =============================== */

    private async Task HealthCheckLoop()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
            while (await timer.WaitForNextTickAsync(_sweepCts.Token).ConfigureAwait(false))
                ReconcileConnectionCounts();
        }
        catch (OperationCanceledException)
        {
            /* normal on disposal */
        }
    }

    private void ReconcileConnectionCounts()
    {
        // Calculate what the gate's available count should be:
        // Available = MaxConnections - (Live - Idle)
        int live = Volatile.Read(ref _live);
        int idle = _idleConnections.Count;
        int active = live - idle;
        int expectedAvailable = _maxConnections - active;
        int actualAvailable = _gate.CurrentCount;

        int drift = expectedAvailable - actualAvailable;

        if (drift > 0)
        {
            // We have fewer available slots in the gate than we should.
            // This means some connection locks were never released.
            // Release the missing slots back to the gate.
            int released = 0;
            for (int i = 0; i < drift; i++)
            {
                try
                {
                    _gate.Release();
                    released++;
                }
                catch (SemaphoreFullException)
                {
                    // Already at max, counts are now synchronized
                    break;
                }
            }

            if (released > 0)
            {
                Log.Warning(
                    "Connection pool health check detected {Drift} stuck connection(s). " +
                    "Released {Released} slot(s) back to pool. " +
                    "(Live: {Live}, Idle: {Idle}, Active: {Active}, Gate Available: {GateAvailable}→{NewGateAvailable})",
                    drift, released, live, idle, active, actualAvailable, _gate.CurrentCount);
                TriggerConnectionPoolChangedEvent();
            }
        }
        else if (drift < 0)
        {
            // We have MORE available slots than expected.
            // This is unusual but could happen if Release() was called too many times.
            // We can't easily fix this without potentially breaking things, so just log it.
            Log.Warning(
                "Connection pool health check detected negative drift of {Drift}. " +
                "More slots available than expected. " +
                "(Live: {Live}, Idle: {Idle}, Active: {Active}, Gate Available: {GateAvailable})",
                drift, live, idle, active, actualAvailable);
        }
    }

    /* ------------------------- dispose helpers ------------------------------------ */

    private static async ValueTask DisposeConnectionAsync(T conn)
    {
        switch (conn)
        {
            case IAsyncDisposable ad:
                await ad.DisposeAsync().ConfigureAwait(false);
                break;
            case IDisposable d:
                d.Dispose();
                break;
        }
    }

    /* -------------------------- IAsyncDisposable ---------------------------------- */

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        _sweepCts.Cancel();

        try
        {
            await Task.WhenAll(_sweeperTask, _healthCheckTask).ConfigureAwait(false); // await both background tasks
        }
        catch (OperationCanceledException)
        {
            /* ignore */
        }

        // Drain and dispose cached items.
        while (_idleConnections.TryPop(out var item))
            await DisposeConnectionAsync(item.Connection).ConfigureAwait(false);

        _sweepCts.Dispose();
        // Intentionally NOT disposing _gate: outstanding handles may still try
        // to Release().  A SemaphoreSlim with no waiters is effectively inert.
        GC.SuppressFinalize(this);
    }

    /* ----------------------------- IDisposable ------------------------------------ */

    public void Dispose()
    {
        _ = DisposeAsync().AsTask(); // fire-and-forget synchronous path
    }

    public sealed class ConnectionPoolChangedEventArgs(int live, int idle, int max) : EventArgs
    {
        public int Live { get; } = live;
        public int Idle { get; } = idle;
        public int Max { get; } = max;
        public int Active => Live - Idle;
    }
}