using UsenetSharp.Streams;

namespace NzbWebDAV.Streams;

public class LimitedLengthStream(Stream stream, long length) : FastReadOnlyNonSeekableStream
{
    private long _position;
    private bool _disposed;

    public override long Length => length;
    public override void Flush() => stream.Flush();

    public override long Position
    {
        get => stream.Position;
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        // If we've already read the specified length, return 0 (end of stream)
        if (_position >= length)
            return 0;

        // Calculate how many bytes we can still read
        var remainingBytes = length - _position;
        var bytesToRead = (int)Math.Min(remainingBytes, buffer.Length);

        // Read from the underlying stream
        var bytesRead = await stream.ReadAsync(buffer[..bytesToRead], cancellationToken).ConfigureAwait(false);

        // Update the position by the number of bytes read
        _position += bytesRead;

        // Return the number of bytes read
        return bytesRead;
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