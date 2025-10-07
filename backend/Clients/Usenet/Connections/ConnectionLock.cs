namespace NzbWebDAV.Clients.Usenet.Connections;

/// <summary>
/// Disposable wrapper that automatically returns a borrowed connection to the
/// originating <see cref="ConnectionPool{T}"/>.
///
/// Note: This class was authored by ChatGPT 3o
/// </summary>
public sealed class ConnectionLock<T> : IDisposable, IAsyncDisposable
{
    private readonly Action<T>            _syncReturn;
    private readonly Func<T, ValueTask>?  _asyncReturn;
    private readonly Action<T>            _syncDestroy;
    private readonly Func<T, ValueTask>?  _asyncDestroy;
    private          T?                   _connection;
    private          int                  _disposed; // 0 == false, 1 == true
    private          int                  _replace;  // 0 == false, 1 == true

    internal ConnectionLock(
        T                       connection,
        Action<T>               syncReturn,
        Func<T,ValueTask>?      asyncReturn = null,
        Action<T>?              syncDestroy = null,
        Func<T,ValueTask>?      asyncDestroy = null)
    {
        _connection  = connection;
        _syncReturn  = syncReturn;
        _asyncReturn = asyncReturn;
        _syncDestroy = syncDestroy ?? (_ => { /* no-op fallback */ });
        _asyncDestroy = asyncDestroy; // may be null; will fall back to _syncDestroy
    }

    public T Connection
        => _connection ?? throw new ObjectDisposedException(nameof(ConnectionLock<T>));

    /// <summary>
    /// Marks the underlying connection to be replaced. When this lock is disposed,
    /// the underlying connection will be destroyed instead of returned to the pool.
    /// </summary>
    public void Replace()
    {
        Volatile.Write(ref _replace, 1);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;   // already done
        var conn = Interlocked.Exchange(ref _connection, default);
        if (conn is not null)
        {
            var replace = Volatile.Read(ref _replace) == 1;
            if (replace)
                _syncDestroy(conn);
            else
                _syncReturn(conn);
        }
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        var conn = Interlocked.Exchange(ref _connection, default);
        if (conn is not null)
        {
            var replace = Volatile.Read(ref _replace) == 1;
            if (replace)
            {
                if (_asyncDestroy is not null)
                    await _asyncDestroy(conn).ConfigureAwait(false);
                else
                    _syncDestroy(conn);
            }
            else
            {
                if (_asyncReturn is not null)
                    await _asyncReturn(conn).ConfigureAwait(false);
                else
                    _syncReturn(conn);
            }
        }
        GC.SuppressFinalize(this);
    }
}