namespace NzbWebDAV.Streams;

/// <summary>
/// This class wraps an underlying stream and exposes a method to
/// probe whether the stream is empty or not, without changing the
/// position of the stream. It does this by reading a single byte
/// and buffering it in memory to relay during future Read requests.
/// </summary>
/// <param name="stream">The underlying stream to probe.</param>
public class ProbingStream(Stream stream) : Stream
{
    private bool? _isEmpty;
    private byte? _probeByte;
    private bool _disposed;

    public async Task<bool> IsEmptyAsync()
    {
        if (_isEmpty.HasValue)
            return _isEmpty.Value;

        var buffer = new byte[1];
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, 1)).ConfigureAwait(false);

        if (bytesRead == 0)
        {
            _isEmpty = true;
        }
        else
        {
            _isEmpty = false;
            _probeByte = buffer[0];
        }

        return _isEmpty.Value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (!_isEmpty.HasValue)
        {
            var read = stream.Read(buffer, offset, count);
            _isEmpty = read == 0;
            return read;
        }

        if (_probeByte.HasValue)
        {
            buffer[offset] = _probeByte.Value;
            _probeByte = null;
            var read = stream.Read(buffer, offset + 1, count - 1);
            return 1 + read;
        }

        return stream.Read(buffer, offset, count);
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (!_isEmpty.HasValue)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            _isEmpty = read == 0;
            return read;
        }

        if (_probeByte.HasValue)
        {
            buffer[offset] = _probeByte.Value;
            _probeByte = null;
            var read = await stream.ReadAsync(buffer.AsMemory(offset + 1, count - 1), cancellationToken).ConfigureAwait(false);
            return 1 + read;
        }

        return await stream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!_isEmpty.HasValue)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            _isEmpty = read == 0;
            return read;
        }

        if (_probeByte.HasValue)
        {
            var span = buffer.Span;
            if (span.Length == 0)
                return 0;

            span[0] = (byte)_probeByte.Value;
            _probeByte = null;

            var read = await stream.ReadAsync(buffer[1..], cancellationToken).ConfigureAwait(false);
            return 1 + read;
        }

        return await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override void Flush() => stream.Flush();
    public override long Seek(long offset, SeekOrigin origin) => stream.Seek(offset, origin);
    public override void SetLength(long value) => stream.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => stream.Write(buffer, offset, count);

    public override bool CanRead => stream.CanRead;
    public override bool CanSeek => stream.CanSeek;
    public override bool CanWrite => stream.CanWrite;
    public override long Length => stream.Length;

    public override long Position
    {
        get => stream.Position;
        set => stream.Position = value;
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        stream.Dispose();
        _disposed = true;
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await stream.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}