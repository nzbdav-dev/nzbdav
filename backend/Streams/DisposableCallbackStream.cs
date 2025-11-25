namespace NzbWebDAV.Streams;

/// <summary>
/// A wrapper stream that delegates all operations to an inner stream and
/// invokes optional callbacks when the stream is disposed synchronously or asynchronously.
///
/// Use this class to hook into the disposal lifecycle of a stream without modifying its implementation.
/// </summary>
public class DisposableCallbackStream : Stream
{
    private readonly Stream _inner;
    private readonly Action? _onDispose;
    private readonly Func<ValueTask>? _onDisposeAsync;
    private bool _disposed;

    // ReSharper disable once ConvertToPrimaryConstructor
    public DisposableCallbackStream(Stream inner, Action? onDispose = null, Func<ValueTask>? onDisposeAsync = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _onDispose = onDispose;
        _onDisposeAsync = onDisposeAsync;
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _inner.Dispose();
            _onDispose?.Invoke();
        }

        _disposed = true;
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        await _inner.DisposeAsync().ConfigureAwait(false);

        if (_onDisposeAsync != null)
            await _onDisposeAsync().ConfigureAwait(false);
        else
            _onDispose?.Invoke();

        _disposed = true;
        await base.DisposeAsync().ConfigureAwait(false);
    }

    // Core stream overrides
    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => _inner.SetLength(value);

    // Read/Write (sync)
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

    // Read/Write (async)
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _inner.WriteAsync(buffer, offset, count, cancellationToken);

    // Modern Span/Memory overloads
    public override int Read(Span<byte> buffer) => _inner.Read(buffer);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => _inner.ReadAsync(buffer, cancellationToken);

    public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => _inner.WriteAsync(buffer, cancellationToken);
}