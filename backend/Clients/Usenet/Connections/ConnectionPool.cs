using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using NzbWebDAV.Extensions;

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
    public int RemainingSemaphoreSlots => _gate.RemainingSemaphoreSlots;

    public event EventHandler<ConnectionPoolStats.ConnectionPoolChangedEventArgs>? OnConnectionPoolChanged;

    private readonly Func<CancellationToken, ValueTask<T>> _factory;
    private readonly int _maxConnections;

    /* --------------------------------- state --------------------------------------- */

    private readonly ConcurrentStack<Pooled> _idleConnections = new();
    private readonly CombinedSemaphoreSlim _gate;
    private readonly CancellationTokenSource _sweepCts = new();
    private readonly Task _sweeperTask; // keeps timer alive

    private int _live; // number of connections currently alive
    private int _disposed; // 0 == false, 1 == true

    // Track active connections by usage type
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ConnectionUsageContext> _activeConnections = new();

    /* ------------------------------------------------------------------------------ */

    public ConnectionPool(
        int maxConnections,
        ExtendedSemaphoreSlim pooledSemaphore,
        Func<CancellationToken, ValueTask<T>> connectionFactory,
        TimeSpan? idleTimeout = null)
    {
        if (maxConnections <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConnections));

        _factory = connectionFactory
                   ?? throw new ArgumentNullException(nameof(connectionFactory));
        IdleTimeout = idleTimeout ?? TimeSpan.FromSeconds(30);

        _maxConnections = maxConnections;
        _gate = new CombinedSemaphoreSlim(maxConnections, pooledSemaphore);
        _sweeperTask = Task.Run(SweepLoop); // background idle-reaper
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
        var reservedCount = cancellationToken.GetContext<ReservedPooledConnectionsContext>().Count;
        var usageContext = cancellationToken.GetContext<ConnectionUsageContext>();

        // Make caller cancellation also cancel the wait on the gate.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _sweepCts.Token);

        var usageBreakdown = GetUsageBreakdownString();
        Serilog.Log.Debug($"[ConnPool] Requesting connection for {usageContext}: Live={_live}, Idle={IdleConnections}, Active={ActiveConnections}, Available={AvailableConnections}, RequiredReserved={reservedCount}, RemainingSemaphore={RemainingSemaphoreSlots}, Usage={usageBreakdown}");
        await _gate.WaitAsync(reservedCount, linked.Token).ConfigureAwait(false);

        // Pool might have been disposed after wait returned:
        if (Volatile.Read(ref _disposed) == 1)
        {
            _gate.Release();
            ThrowDisposed();
        }

        // Generate connection ID for tracking
        var connectionId = Guid.NewGuid().ToString();

        // Try to reuse an existing idle connection.
        while (_idleConnections.TryPop(out var item))
        {
            if (!item.IsExpired(IdleTimeout))
            {
                TriggerConnectionPoolChangedEvent();
                _activeConnections[connectionId] = usageContext;
                Serilog.Log.Debug($"[ConnPool] Connection reused for {usageContext}: Live={_live}, Idle={IdleConnections}, Active={ActiveConnections}, ConnId={connectionId}");
                return BuildLock(item.Connection, connectionId);
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

        _activeConnections[connectionId] = usageContext;
        Serilog.Log.Debug($"[ConnPool] Connection acquired for {usageContext}: Live={_live}, Idle={IdleConnections}, Active={ActiveConnections}, ConnId={connectionId}");
        return BuildLock(conn, connectionId);

        ConnectionLock<T> BuildLock(T c, string connId)
            => new(c, conn => Return(conn, connId), conn => Destroy(conn, connId));

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

    private void Return(T connection, string connectionId)
    {
        _activeConnections.TryRemove(connectionId, out var usageContext);

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
        Serilog.Log.Debug($"[ConnPool] Connection returned from {usageContext}: Live={_live}, Idle={IdleConnections}, Active={ActiveConnections}");
    }

    private void Destroy(T connection, string connectionId)
    {
        _activeConnections.TryRemove(connectionId, out _);

        // When a lock requests replacement, we dispose the connection instead of reusing.
        _ = DisposeConnectionAsync(connection); // fire & forget
        Interlocked.Decrement(ref _live);
        if (Volatile.Read(ref _disposed) == 0)
        {
            _gate.Release();
        }

        TriggerConnectionPoolChangedEvent();
    }

    public Dictionary<ConnectionUsageType, int> GetUsageBreakdown()
    {
        return _activeConnections.Values
            .GroupBy(x => x.UsageType)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private string GetUsageBreakdownString()
    {
        var breakdown = GetUsageBreakdown();
        var parts = breakdown
            .OrderBy(x => x.Key)
            .Select(kv => $"{kv.Key}={kv.Value}")
            .ToArray();
        return parts.Length > 0 ? string.Join(",", parts) : "none";
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
            await _sweeperTask.ConfigureAwait(false); // await clean sweep exit
        }
        catch (OperationCanceledException)
        {
            /* ignore */
        }

        // Drain and dispose cached items.
        while (_idleConnections.TryPop(out var item))
            await DisposeConnectionAsync(item.Connection).ConfigureAwait(false);

        _sweepCts.Dispose();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    /* ----------------------------- IDisposable ------------------------------------ */

    public void Dispose()
    {
        _ = DisposeAsync().AsTask(); // fire-and-forget synchronous path
    }
}