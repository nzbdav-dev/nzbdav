﻿using NzbWebDAV.Clients;
using NzbWebDAV.Extensions;
using NzbWebDAV.Utils;
using Usenet.Nzb;
using Usenet.Yenc;

namespace NzbWebDAV.Streams;

public class NzbFileStream(
    string[] fileSegmentIds,
    long fileSize,
    INntpClient client,
    int concurrentConnections
) : Stream
{
    private long _position = 0;
    private YencHeaderStream? _firstSegmentStream;
    private CombinedStream? _innerStream;
    private bool _disposed;

    public YencHeader? FirstYencHeader => _firstSegmentStream?.Header;

    public NzbFileStream
    (
        NzbFile file,
        long fileSize,
        INntpClient client,
        int concurrentConnections
    ) : this(file.GetOrderedSegmentIds(), fileSize, client, concurrentConnections)
    {
    }


    public NzbFileStream
    (
        NzbFile file,
        YencHeaderStream firstSegmentStream,
        INntpClient client,
        int concurrentConnections
    ) : this(file.GetOrderedSegmentIds(), firstSegmentStream.Header.FileSize, client, concurrentConnections)
    {
        _firstSegmentStream = firstSegmentStream;
    }

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
        _firstSegmentStream?.Dispose();
        _firstSegmentStream = null;
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
            new InterpolationSearch.Range(0, fileSegmentIds.Length),
            new InterpolationSearch.Range(0, fileSize),
            async (guess) =>
            {
                var header = await client.GetSegmentYencHeaderAsync(fileSegmentIds[guess], ct);
                return new InterpolationSearch.Range(header.PartOffset, header.PartOffset + header.PartSize);
            },
            ct
        );
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
        if (firstSegmentIndex == 0 && _firstSegmentStream != null)
        {
            return new CombinedStream(
                fileSegmentIds[1..]
                    .Select(async x => (Stream)await client.GetSegmentStreamAsync(x, ct))
                    .Prepend(Task.FromResult<Stream>(_firstSegmentStream))
                    .WithConcurrency(concurrentConnections)
            );
        }

        return new CombinedStream(
            fileSegmentIds[firstSegmentIndex..]
                .Select(async x => (Stream)await client.GetSegmentStreamAsync(x, ct))
                .WithConcurrency(concurrentConnections)
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _innerStream?.Dispose();
        _firstSegmentStream?.Dispose();
        _disposed = true;
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (_innerStream != null) await _innerStream.DisposeAsync();
        if (_firstSegmentStream != null) await _firstSegmentStream.DisposeAsync();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}