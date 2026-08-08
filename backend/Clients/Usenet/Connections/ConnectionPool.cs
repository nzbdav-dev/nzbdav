using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using NzbWebDAV.Clients.Usenet.Concurrency;
using NzbWebDAV.Clients.Usenet.Contexts;

namespace NzbWebDAV.Clients.Usenet.Connections;

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
    private readonly CancellationTokenSource _sweepCts = new();
    private readonly Task _sweeperTask; // keeps timer alive

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
        _gate = new PrioritizedSemaphore(maxConnections, maxConnections);
        _sweeperTask = Task.Run(SweepLoop); // background idle-reaper
    }

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
        // Pool/provider shutdown is cancellation, not a provider failure.
        if (Volatile.Read(ref _disposed) == 1)
            throw new OperationCanceledException(
                "Connection pool has been disposed.",
                cancellationToken);

        CancellationTokenSource? linked = null;

        try
        {
            try
            {
                linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _sweepCts.Token);
            }
            catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) == 1)
            {
                throw new OperationCanceledException(
                    "Connection pool was disposed while acquiring a connection.",
                    cancellationToken);
            }

            try
            {
                await _gate.WaitAsync(priority, linked.Token).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) == 1)
            {
                throw new OperationCanceledException(
                    "Connection pool was disposed while waiting for a connection.",
                    cancellationToken);
            }

            if (Volatile.Read(ref _disposed) == 1)
            {
                try
                {
                    _gate.Release();
                }
                catch (ObjectDisposedException)
                {
                    // Normal concurrent shutdown.
                }

                throw new OperationCanceledException(
                    "Connection pool was disposed while acquiring a connection.",
                    cancellationToken);
            }

            while (_idleConnections.TryPop(out var item))
            {
                if (!item.IsExpired(IdleTimeout))
                {
                    TriggerConnectionPoolChangedEvent();
                    return BuildLock(item.Connection);
                }

                DisposeConnection(item.Connection);
                Interlocked.Decrement(ref _live);
                TriggerConnectionPoolChangedEvent();
            }

            T conn;

            try
            {
                conn = await _factory(linked.Token).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    _gate.Release();
                }
                catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) == 1)
                {
                    // Normal concurrent shutdown.
                }

                throw;
            }

            // Pool could have been disposed while the socket was connecting.
            if (Volatile.Read(ref _disposed) == 1)
            {
                DisposeConnection(conn);

                try
                {
                    _gate.Release();
                }
                catch (ObjectDisposedException)
                {
                    // Gate already gone.
                }

                throw new OperationCanceledException(
                    "Connection pool was disposed while creating a connection.",
                    cancellationToken);
            }

            Interlocked.Increment(ref _live);
            TriggerConnectionPoolChangedEvent();

            return BuildLock(conn);
        }
        finally
        {
            linked?.Dispose();
        }

        ConnectionLock<T> BuildLock(T c)
            => new(c, Return, Destroy);
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
        GC.SuppressFinalize(this);
    }

    /* ----------------------------- IDisposable ------------------------------------ */

    public void Dispose()
    {
        // Complete pool teardown before returning.  Fire-and-forget disposal
        // allowed callers to race with _sweepCts and _gate destruction.
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}