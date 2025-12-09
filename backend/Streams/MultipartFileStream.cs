using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Models;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Streams;

public class MultipartFileStream : Stream
{
    private bool _isDisposed;
    private readonly INntpClient _client;
    private readonly MultipartFile _multipartFile;
    private Stream? _currentStream;
    private long _position = 0;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _multipartFile.FileSize;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public MultipartFileStream(MultipartFile multipartFile, INntpClient client)
    {
        _multipartFile = multipartFile;
        _client = client;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (count == 0) return 0;
        while (_position < Length && !cancellationToken.IsCancellationRequested)
        {
            // If we haven't read the first stream, read it.
            _currentStream ??= GetCurrentStream();

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
            _currentStream = null;
        }

        return 0;
    }

    private Stream GetCurrentStream()
    {
        var searchResult = InterpolationSearch.Find(
            _position,
            new LongRange(0, _multipartFile.FileParts.Count),
            new LongRange(0, Length),
            guess => _multipartFile.FileParts[guess].ByteRange
        );

        var filePart = _multipartFile.FileParts[searchResult.FoundIndex];
        var stream = _client.GetFileStream(filePart.NzbFile, filePart.PartSize, concurrentConnections: 1);
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