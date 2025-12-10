using System.Buffers;
using UsenetSharp.Streams;

namespace NzbWebDAV.Streams;

public class CancellableStream(Stream innerStream, CancellationToken token) : FastReadOnlyStream
{
    private readonly Stream _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
    private bool _disposed;

    public override bool CanSeek => _innerStream.CanSeek;
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

    public override int Read(Span<byte> buffer)
    {
        CheckDisposed();
        var array = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            var buffer1 = new Memory<byte>(array, 0, buffer.Length);
            var result = this.ReadAsync(buffer1, token).AsTask().GetAwaiter().GetResult();
            buffer1.Span[..result].CopyTo(buffer);
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(array);
        }
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        CheckDisposed();
        return _innerStream.ReadAsync(buffer, cancellationToken);
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