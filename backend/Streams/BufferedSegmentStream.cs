using System.Threading.Channels;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.Streams;

/// <summary>
/// High-performance buffered stream that maintains a read-ahead buffer of segments
/// for smooth, consistent streaming performance.
/// </summary>
public class BufferedSegmentStream : Stream
{
    private readonly Channel<SegmentData> _bufferChannel;
    private readonly Task _fetchTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly CancellationTokenSource _linkedCts;
    private readonly IDisposable[] _contextScopes;

    private SegmentData? _currentSegment;
    private int _currentSegmentPosition;
    private long _position;
    private bool _disposed;

    public BufferedSegmentStream(
        string[] segmentIds,
        long fileSize,
        INntpClient client,
        int concurrentConnections,
        int bufferSegmentCount,
        CancellationToken cancellationToken)
    {
        // Create bounded channel for buffering
        var channelOptions = new BoundedChannelOptions(bufferSegmentCount)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        };
        _bufferChannel = Channel.CreateBounded<SegmentData>(channelOptions);

        // Link cancellation tokens and preserve context
        _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

        // Copy all contexts from the original token to the linked token
        // Store context scopes so they live for the duration of the stream
        _contextScopes = new[]
        {
            _linkedCts.Token.SetScopedContext(cancellationToken.GetContext<ReservedPooledConnectionsContext>()),
            _linkedCts.Token.SetScopedContext(cancellationToken.GetContext<LastSuccessfulProviderContext>()),
            _linkedCts.Token.SetScopedContext(cancellationToken.GetContext<ConnectionUsageContext>())
        };
        var contextToken = _linkedCts.Token;

        // Start background fetcher
        _fetchTask = Task.Run(async () =>
        {
            await FetchSegmentsAsync(segmentIds, client, concurrentConnections, contextToken)
                .ConfigureAwait(false);
        }, contextToken);

        Length = fileSize;
    }

    private async Task FetchSegmentsAsync(
        string[] segmentIds,
        INntpClient client,
        int concurrentConnections,
        CancellationToken ct)
    {
        try
        {
            // Use SemaphoreSlim to limit concurrent segment fetches
            using var semaphore = new SemaphoreSlim(concurrentConnections, concurrentConnections);
            var fetchTasks = new List<Task>();

            foreach (var segmentId in segmentIds)
            {
                if (ct.IsCancellationRequested) break;

                await semaphore.WaitAsync(ct).ConfigureAwait(false);

                var fetchTask = Task.Run(async () =>
                {
                    try
                    {
                        var stream = await client.GetSegmentStreamAsync(segmentId, false, ct)
                            .ConfigureAwait(false);

                        // Read entire segment into memory
                        using var ms = new MemoryStream();
                        await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
                        await stream.DisposeAsync().ConfigureAwait(false);

                        var segmentData = new SegmentData
                        {
                            SegmentId = segmentId,
                            Data = ms.ToArray()
                        };

                        // Write to channel (blocks if buffer is full)
                        await _bufferChannel.Writer.WriteAsync(segmentData, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, ct);

                fetchTasks.Add(fetchTask);

                // Clean up completed tasks periodically
                if (fetchTasks.Count >= concurrentConnections * 2)
                {
                    var completed = fetchTasks.Where(t => t.IsCompleted).ToList();
                    foreach (var task in completed)
                    {
                        await task.ConfigureAwait(false); // Propagate exceptions
                        fetchTasks.Remove(task);
                    }
                }
            }

            // Wait for all remaining fetch tasks
            await Task.WhenAll(fetchTasks).ConfigureAwait(false);

            _bufferChannel.Writer.Complete();
        }
        catch (Exception ex)
        {
            _bufferChannel.Writer.Complete(ex);
        }
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (count == 0) return 0;

        int totalRead = 0;

        while (totalRead < count && !cancellationToken.IsCancellationRequested)
        {
            // Get current segment if we don't have one
            if (_currentSegment == null)
            {
                if (!await _bufferChannel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    break; // No more segments

                if (!_bufferChannel.Reader.TryRead(out _currentSegment))
                    break;

                _currentSegmentPosition = 0;
            }

            // Read from current segment
            var bytesAvailable = _currentSegment.Data.Length - _currentSegmentPosition;
            if (bytesAvailable == 0)
            {
                _currentSegment = null;
                continue;
            }

            var bytesToRead = Math.Min(count - totalRead, bytesAvailable);
            Buffer.BlockCopy(_currentSegment.Data, _currentSegmentPosition, buffer, offset + totalRead, bytesToRead);

            _currentSegmentPosition += bytesToRead;
            totalRead += bytesToRead;
            _position += bytesToRead;

            // If segment is exhausted, move to next
            if (_currentSegmentPosition >= _currentSegment.Data.Length)
            {
                _currentSegment = null;
            }
        }

        return totalRead;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length { get; }

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException("Seeking is not supported.");
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException("Seeking is not supported.");
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _cts.Cancel();
            _cts.Dispose();
            _bufferChannel.Writer.TryComplete();
            try { _fetchTask.Wait(TimeSpan.FromSeconds(5)); } catch { }

            // Dispose context scopes
            foreach (var scope in _contextScopes)
                scope?.Dispose();

            _linkedCts.Dispose();
        }
        _disposed = true;
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _cts.Cancel();
        _bufferChannel.Writer.TryComplete();

        try
        {
            await _fetchTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch { }

        _cts.Dispose();

        // Dispose context scopes
        foreach (var scope in _contextScopes)
            scope?.Dispose();

        _linkedCts.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private class SegmentData
    {
        public required string SegmentId { get; init; }
        public required byte[] Data { get; init; }
    }
}
