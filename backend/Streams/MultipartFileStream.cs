using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Models;
using NzbWebDAV.Utils;
using UsenetSharp.Streams;

namespace NzbWebDAV.Streams;

public class MultipartFileStream(MultipartFile multipartFile, INntpClient usenetClient) : FastReadOnlyStream
{
    private long _position;
    private bool _isDisposed;
    private Stream? _currentStream;

    public override bool CanSeek => true;
    public override long Length => multipartFile.FileSize;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0) return 0;
        while (_position < Length && !cancellationToken.IsCancellationRequested)
        {
            // If we haven't read the first stream, read it.
            _currentStream ??= GetCurrentStream();

            // read from our current stream
            var readCount = await _currentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            _position += readCount;
            if (readCount > 0) return readCount;

            // If we couldn't read anything from our current stream,
            // it's time to advance to the next stream.
            await _currentStream.DisposeAsync().ConfigureAwait(false);
            _currentStream = null;
        }

        return 0;
    }

    private NzbFileStream GetCurrentStream()
    {
        var searchResult = InterpolationSearch.Find(
            _position,
            new LongRange(0, multipartFile.FileParts.Count),
            new LongRange(0, Length),
            guess => multipartFile.FileParts[guess].ByteRange
        );

        var filePart = multipartFile.FileParts[searchResult.FoundIndex];
        var stream = usenetClient.GetFileStream(filePart.NzbFile, filePart.PartSize, articleBufferSize: 0);
        stream.Seek(_position - searchResult.FoundByteRange.StartInclusive, SeekOrigin.Begin);
        return stream;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var absoluteOffset = origin == SeekOrigin.Begin ? offset
            : origin == SeekOrigin.Current ? _position + offset
            : throw new InvalidOperationException("SeekOrigin must be Begin or Current.");
        if (_position == absoluteOffset) return _position;
        _position = absoluteOffset;
        _currentStream?.Dispose();
        _currentStream = null;
        return _position;
    }

    public override void Flush()
    {
        _currentStream?.Flush();
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed) return;
        if (!disposing) return;
        _currentStream?.Dispose();
        _isDisposed = true;
    }

    public override async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        if (_currentStream != null) await _currentStream.DisposeAsync().ConfigureAwait(false);
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}