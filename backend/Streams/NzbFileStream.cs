using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Streams;

public class NzbFileStream(
    string[] fileSegmentIds,
    long fileSize,
    INntpClient client,
    int concurrentConnections,
    ConnectionUsageContext? usageContext = null,
    bool useBufferedStreaming = true,
    int bufferSize = 10
) : Stream
{
    private long _position = 0;
    private CombinedStream? _innerStream;
    private bool _disposed;
    private readonly ConnectionUsageContext _usageContext = usageContext ?? new ConnectionUsageContext(ConnectionUsageType.Unknown);
    private CancellationTokenSource? _streamCts;
    private IDisposable? _contextScope;

    public override void Flush()
    {
        _innerStream?.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_innerStream == null) _innerStream = await GetFileStream(_position, cancellationToken).ConfigureAwait(false);
        var read = await _innerStream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var absoluteOffset = origin == SeekOrigin.Begin ? offset
            : origin == SeekOrigin.Current ? _position + offset
            : throw new InvalidOperationException("SeekOrigin must be Begin or Current.");
        if (_position == absoluteOffset) return _position;
        _position = absoluteOffset;
        _innerStream?.Dispose();
        _innerStream = null;
        return _position;
    }

    public override void SetLength(long value)
    {
        throw new InvalidOperationException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new InvalidOperationException();
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => fileSize;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }


    private async Task<InterpolationSearch.Result> SeekSegment(long byteOffset, CancellationToken ct)
    {
        return await InterpolationSearch.Find(
            byteOffset,
            new LongRange(0, fileSegmentIds.Length),
            new LongRange(0, fileSize),
            async (guess) =>
            {
                var header = await client.GetSegmentYencHeaderAsync(fileSegmentIds[guess], ct).ConfigureAwait(false);
                return new LongRange(header.PartOffset, header.PartOffset + header.PartSize);
            },
            ct
        ).ConfigureAwait(false);
    }

    private async Task<CombinedStream> GetFileStream(long rangeStart, CancellationToken cancellationToken)
    {
        if (rangeStart == 0) return GetCombinedStream(0, cancellationToken);
        var foundSegment = await SeekSegment(rangeStart, cancellationToken).ConfigureAwait(false);
        var stream = GetCombinedStream(foundSegment.FoundIndex, cancellationToken);
        await stream.DiscardBytesAsync(rangeStart - foundSegment.FoundByteRange.StartInclusive).ConfigureAwait(false);
        return stream;
    }

    private CombinedStream GetCombinedStream(int firstSegmentIndex, CancellationToken ct)
    {
        // Create a child cancellation token that will live for the stream's lifetime
        _streamCts?.Dispose();
        _streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Dispose previous context scope if any
        _contextScope?.Dispose();

        // Use buffered streaming if configured for better performance
        if (useBufferedStreaming && concurrentConnections >= 3 && fileSegmentIds.Length > concurrentConnections)
        {
            // Update context to BufferedStreaming and keep the scope alive for the stream's lifetime
            var bufferedContext = new ConnectionUsageContext(
                ConnectionUsageType.BufferedStreaming,
                _usageContext.Details
            );
            _contextScope = _streamCts.Token.SetScopedContext(bufferedContext);
            var bufferedContextCt = _streamCts.Token;

            var remainingSegments = fileSegmentIds[firstSegmentIndex..];
            var bufferedStream = new BufferedSegmentStream(
                remainingSegments,
                fileSize - firstSegmentIndex * (fileSize / fileSegmentIds.Length), // Approximate remaining size
                client,
                concurrentConnections,
                bufferSize,
                bufferedContextCt
            );
            return new CombinedStream(new[] { Task.FromResult<Stream>(bufferedStream) });
        }

        // Fallback to original implementation for small files or low concurrency
        // Set context for non-buffered streaming and keep scope alive
        _contextScope = _streamCts.Token.SetScopedContext(_usageContext);
        var contextCt = _streamCts.Token;

        return new CombinedStream(
            fileSegmentIds[firstSegmentIndex..]
                .Select(async x => (Stream)await client.GetSegmentStreamAsync(x, false, contextCt).ConfigureAwait(false))
                .WithConcurrency(concurrentConnections)
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _innerStream?.Dispose();
        _streamCts?.Dispose();
        _contextScope?.Dispose();
        _disposed = true;
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (_innerStream != null) await _innerStream.DisposeAsync().ConfigureAwait(false);
        _streamCts?.Dispose();
        _contextScope?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}