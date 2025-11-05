namespace NzbWebDAV.Streams;

public class CancellableStream(Stream innerStream, CancellationToken token) : Stream
{
    private readonly Stream _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
    private bool _disposed;

    public override bool CanRead => _innerStream.CanRead;
    public override bool CanSeek => _innerStream.CanSeek;
    public override bool CanWrite => _innerStream.CanWrite;
    public override long Length => _innerStream.Length;

    public override long Position
    {
        get => _innerStream.Position;
        set => _innerStream.Position = value;
    }

    public override void Flush()
    {
        CheckDisposed();
        _innerStream.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        CheckDisposed();
        return _innerStream.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        CheckDisposed();
        return ReadAsync(buffer, offset, count, token)
            .GetAwaiter()
            .GetResult();
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        CheckDisposed();
        return _innerStream.ReadAsync(buffer, cancellationToken);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        CheckDisposed();
        return _innerStream.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override void SetLength(long value)
    {
        CheckDisposed();
        _innerStream.SetLength(value);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        CheckDisposed();
        return _innerStream.Seek(offset, origin);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        CheckDisposed();
        _innerStream.Write(buffer, offset, count);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        CheckDisposed();
        return _innerStream.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        CheckDisposed();
        return _innerStream.WriteAsync(buffer, cancellationToken);
    }

    private void CheckDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CancellableStream));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (!disposing) return;
        _disposed = true;
        _innerStream.Dispose();
        base.Dispose();
    }
}