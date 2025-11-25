namespace NzbWebDAV.Streams;

public class CombinedStream(IEnumerable<Task<Stream>> streams) : Stream
{
    private readonly IEnumerator<Task<Stream>> _streams = streams.GetEnumerator();
    private Stream? _currentStream;
    private long _position;
    private bool _isDisposed;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (count == 0) return 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            // If we haven't read the first stream, read it.
            if (_currentStream == null)
            {
                if (!_streams.MoveNext()) return 0;
                _currentStream = await _streams.Current.ConfigureAwait(false);
            }

            // read from our current stream
            var readCount = await _currentStream.ReadAsync
            (
                buffer.AsMemory(offset, count),
                cancellationToken
            ).ConfigureAwait(false);
            _position += readCount;
            if (readCount > 0) return readCount;

            // If we couldn't read anything from our current stream,
            // it's time to advance to the next stream.
            await _currentStream.DisposeAsync().ConfigureAwait(false);
            if (!_streams.MoveNext()) return 0;
            _currentStream = await _streams.Current.ConfigureAwait(false);
        }

        return 0;
    }

    public async Task DiscardBytesAsync(long count)
    {
        if (count == 0) return;
        var remaining = count;
        var throwaway = new byte[1024];
        while (remaining > 0)
        {
            var toRead = (int)Math.Min(remaining, throwaway.Length);
            var read = await ReadAsync(throwaway, 0, toRead).ConfigureAwait(false);
            remaining -= read;
            if (read == 0) break;
        }

        _position += count;
    }

    public override void Flush()
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed) return;
        if (!disposing) return;
        _streams.Dispose();
        _currentStream?.Dispose();
        _isDisposed = true;
    }

    public override async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        if (_currentStream != null) await _currentStream.DisposeAsync().ConfigureAwait(false);
        _streams.Dispose();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}