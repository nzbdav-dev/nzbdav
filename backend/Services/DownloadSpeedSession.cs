namespace NzbWebDAV.Services;

/// <summary>
/// Tracks the number of bytes downloaded from usenet on behalf of a single
/// named consumer (a webdav file-stream or a queue import).
/// Dispose the session to stop tracking it.
/// </summary>
public class DownloadSpeedSession : IDisposable
{
    private readonly Action<DownloadSpeedSession> _onDisposed;
    private long _bytes;
    private bool _disposed;

    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; }
    public SessionCategory Category { get; }

    internal DownloadSpeedSession(string name, SessionCategory category, Action<DownloadSpeedSession> onDisposed)
    {
        Name = name;
        Category = category;
        _onDisposed = onDisposed;
    }

    public void AddBytes(int byteCount)
    {
        Interlocked.Add(ref _bytes, byteCount);
    }

    /// <summary>
    /// Atomically returns the number of bytes added since the previous call.
    /// Called by the tracker's publish loop and on session removal.
    /// </summary>
    internal long TakeBytesDelta()
    {
        return Interlocked.Exchange(ref _bytes, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _onDisposed(this);
        GC.SuppressFinalize(this);
    }

    public enum SessionCategory
    {
        Stream,
        QueueImport,
    }
}
