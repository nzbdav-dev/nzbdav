using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Streams;

public class NzbFileStream(
    string[] fileSegmentIds,
    long fileSize,
    INntpClient client,
    int concurrentConnections
) : Stream
{
    private long _position = 0;
    private CombinedStream? _innerStream;
    private bool _disposed;

    // Cache for segment seek results to avoid recalculating positions
    // Key: byte offset, Value: (segment index, segment byte range)
    private readonly Dictionary<long, InterpolationSearch.Result> _segmentCache = new();

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
        if (_innerStream == null) _innerStream = await GetFileStream(_position, cancellationToken);
        var read = await _innerStream.ReadAsync(buffer, offset, count, cancellationToken);
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
        // Check if we have a cached result for this exact offset
        if (_segmentCache.TryGetValue(byteOffset, out var cached))
            return cached;

        // Check if we have a cached result that contains this offset
        // This reuses work from previous seeks to nearby positions
        foreach (var (_, cachedResult) in _segmentCache)
        {
            if (cachedResult.FoundByteRange.Contains(byteOffset))
                return cachedResult;
        }

        // Not in cache, perform the search
        var result = await InterpolationSearch.Find(
            byteOffset,
            new LongRange(0, fileSegmentIds.Length),
            new LongRange(0, fileSize),
            async (guess) =>
            {
                var header = await client.GetSegmentYencHeaderAsync(fileSegmentIds[guess], ct);
                return new LongRange(header.PartOffset, header.PartOffset + header.PartSize);
            },
            ct
        );

        // Cache the result for future seeks
        // Keep cache size reasonable (max 100 entries)
        if (_segmentCache.Count < 100)
            _segmentCache[byteOffset] = result;

        return result;
    }

    private async Task<CombinedStream> GetFileStream(long rangeStart, CancellationToken cancellationToken)
    {
        if (rangeStart == 0) return GetCombinedStream(0, cancellationToken);
        var foundSegment = await SeekSegment(rangeStart, cancellationToken);
        var stream = GetCombinedStream(foundSegment.FoundIndex, cancellationToken);
        await stream.DiscardBytesAsync(rangeStart - foundSegment.FoundByteRange.StartInclusive);
        return stream;
    }

    private CombinedStream GetCombinedStream(int firstSegmentIndex, CancellationToken ct)
    {
        // Note: The Select creates tasks lazily, and WithConcurrency() already optimizes
        // time-to-first-byte by yielding the first task immediately (see IEnumerableTaskExtensions.cs:36-42)
        // and managing concurrent prefetching of subsequent segments
        return new CombinedStream(
            fileSegmentIds[firstSegmentIndex..]
                .Select(async x => (Stream)await client.GetSegmentStreamAsync(x, false, ct))
                .WithConcurrency(concurrentConnections)
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _innerStream?.Dispose();
        _disposed = true;
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (_innerStream != null) await _innerStream.DisposeAsync();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}