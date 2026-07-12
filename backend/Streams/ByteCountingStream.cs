namespace NzbWebDAV.Streams;

/// <summary>
/// A read-only pass-through stream that reports the
/// number of bytes read from the underlying stream.
/// </summary>
public class ByteCountingStream(Stream inner, Action<int> onBytesRead) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => throw new NotSupportedException();
    }

    public override void Flush() => inner.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Count(inner.Read(buffer, offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        return Count(inner.Read(buffer));
    }

    public override int ReadByte()
    {
        var value = inner.ReadByte();
        if (value >= 0) onBytesRead(1);
        return value;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        return Count(await inner.ReadAsync(buffer.AsMemory(offset, count), ct).ConfigureAwait(false));
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        return Count(await inner.ReadAsync(buffer, ct).ConfigureAwait(false));
    }

    private int Count(int bytesRead)
    {
        if (bytesRead > 0) onBytesRead(bytesRead);
        return bytesRead;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
