﻿using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

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

    private readonly Func<CancellationToken, ValueTask<T>> _factory;

    /* --------------------------------- state --------------------------------------- */

    private readonly ConcurrentStack<Pooled> _available = new();
    private readonly SemaphoreSlim _gate;
    private readonly CancellationTokenSource _sweepCts = new();
    private readonly Task _sweeperTask; // keeps timer alive

    private long _live; // number of connections currently alive
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
        IdleTimeout = idleTimeout ?? TimeSpan.FromMinutes(1);

        _gate = new SemaphoreSlim(maxConnections, maxConnections);
        _sweeperTask = Task.Run(SweepLoop); // background idle-reaper
    }

    /* ============================== public API ==================================== */

    /// <summary>Gets the number of currently active connections.</summary>
    public int ActiveConnectionCount => (int)Volatile.Read(ref _live);

    /// <summary>Gets the number of available connections in the pool.</summary>
    public int AvailableConnectionCount => _available.Count;

    /// <summary>Borrow a connection; releases automatically with the lock.</summary>
    public async Task<ConnectionLock<T>> GetConnectionLockAsync(
        CancellationToken cancellationToken = default)
    {
        // Make caller cancellation also cancel the wait on the gate.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _sweepCts.Token);

        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);

        // Pool might have been disposed after wait returned:
        if (Volatile.Read(ref _disposed) == 1)
        {
            _gate.Release();
            ThrowDisposed();
        }

        // Try to reuse an existing idle connection.
        while (_available.TryPop(out var item))
        {
            if (!item.IsExpired(IdleTimeout))
                return BuildLock(item.Connection);

            // Stale – destroy and continue looking.
            await DisposeConnectionAsync(item.Connection).ConfigureAwait(false);
            Interlocked.Decrement(ref _live);
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
        return BuildLock(conn);

        ConnectionLock<T> BuildLock(T c)
            => new ConnectionLock<T>(c, Return, ReturnAsync);

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
            return;
        }

        _available.Push(new Pooled(connection, Environment.TickCount64));
        _gate.Release();
    }

    private ValueTask ReturnAsync(T connection)
    {
        Return(connection);
        return ValueTask.CompletedTask;
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

        while (_available.TryPop(out var item))
        {
            if (item.IsExpired(IdleTimeout, now))
            {
                await DisposeConnectionAsync(item.Connection).ConfigureAwait(false);
                Interlocked.Decrement(ref _live);
            }
            else
            {
                survivors.Add(item);
            }
        }

        // Preserve original LIFO order.
        for (int i = survivors.Count - 1; i >= 0; i--)
            _available.Push(survivors[i]);
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
        while (_available.TryPop(out var item))
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
}