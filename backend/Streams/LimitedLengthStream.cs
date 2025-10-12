using NzbWebDAV.Utils;

namespace NzbWebDAV.Streams;

public class LimitedLengthStream(Stream stream, long length) : Stream
{
    private bool _disposed;
    private long _position = 0;

    public override void Flush() => stream.Flush();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer, offset, count, SigtermUtil.GetCancellationToken()).GetAwaiter().GetResult();

    public override async Task<int>
        ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        // If we've already read the specified length, return 0 (end of stream)
        if (_position >= length)
            return 0;

        // Calculate how many bytes we can still read
        var remainingBytes = length - _position;
        var bytesToRead = (int)Math.Min(remainingBytes, buffer.Length);

        // Read from the underlying stream
        var bytesRead = await stream.ReadAsync(buffer[..bytesToRead], cancellationToken);

        // Update the position by the number of bytes read
        _position += bytesRead;

        // Return the number of bytes read
        return bytesRead;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => stream.Write(buffer, offset, count);

    public override bool CanRead => stream.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => length;

    public override long Position
    {
        get => stream.Position;
        set => throw new NotSupportedException();
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
        await stream.DisposeAsync();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}