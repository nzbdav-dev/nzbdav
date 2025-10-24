namespace NzbWebDAV.Streams;

using System;
using System.IO;

public sealed class SubStream : Stream
{
    private readonly Stream _innerStream;
    private readonly long _start;
    private readonly long _length;
    private long _position;
    private bool _disposed;

    public SubStream(Stream innerStream, long offset, long count)
    {
        if (innerStream == null)
            throw new ArgumentNullException(nameof(innerStream));
        if (!innerStream.CanSeek)
            throw new ArgumentException("Inner stream must support seeking.", nameof(innerStream));
        if (offset < 0 || count < 0)
            throw new ArgumentOutOfRangeException("Offset and count must be non-negative.");
        if (offset + count > innerStream.Length)
            throw new ArgumentException("The specified range exceeds the length of the inner stream.");

        _innerStream = innerStream;
        _start = offset;
        _length = count;
        _position = 0;
    }

    public override bool CanRead => !_disposed && _innerStream.CanRead;
    public override bool CanSeek => !_disposed && _innerStream.CanSeek;
    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            ThrowIfDisposed();
            return _length;
        }
    }

    public override long Position
    {
        get
        {
            ThrowIfDisposed();
            return _position;
        }
        set
        {
            ThrowIfDisposed();
            if (value < 0 || value > _length)
                throw new ArgumentOutOfRangeException(nameof(value));
            _position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ThrowIfDisposed();

        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || count < 0 || offset + count > buffer.Length)
            throw new ArgumentOutOfRangeException();

        if (_position >= _length)
            return 0;

        long remaining = _length - _position;
        if (count > remaining)
            count = (int)remaining;

        _innerStream.Seek(_start + _position, SeekOrigin.Begin);
        int read = _innerStream.Read(buffer, offset, count);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ThrowIfDisposed();

        long newPos = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentException("Invalid seek origin.", nameof(origin))
        };

        if (newPos < 0 || newPos > _length)
            throw new IOException("Attempted to seek outside the bounds of the substream.");

        _position = newPos;
        return _position;
    }

    public override void Flush()
    {
        /* read-only, nothing to flush */
    }

    public override void SetLength(long value) =>
        throw new NotSupportedException("SubStream is read-only.");

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("SubStream is read-only.");

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        // We do not dispose the inner stream — caller owns it.
        base.Dispose(disposing);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SubStream));
    }
}