using NzbWebDAV.Clients.Usenet.Models;
using Usenet.Yenc;

namespace NzbWebDAV.Streams;

public class YencHeaderStream(YencHeader header, UsenetArticleHeaders? articleHeaders, Stream stream) : Stream
{
    public YencHeader Header => header;
    public UsenetArticleHeaders? ArticleHeaders => articleHeaders;
    private bool _disposed;

    public override void Flush() => stream.Flush();
    public override int Read(byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        stream.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        stream.ReadAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => stream.Seek(offset, origin);
    public override void SetLength(long value) => stream.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => stream.Write(buffer, offset, count);

    public override bool CanRead => stream.CanRead;
    public override bool CanSeek => stream.CanSeek;
    public override bool CanWrite => stream.CanWrite;
    public override long Length => Header.PartSize;

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