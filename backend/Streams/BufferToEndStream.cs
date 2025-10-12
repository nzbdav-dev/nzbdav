using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.ExceptionServices;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Streams;

/// <summary>
/// Wraps a readable <see cref="Stream"/>.
/// • As soon as it is constructed it begins copying every byte
///   from <paramref name="sourceStream"/> into memory.
/// • Callers can read while the copy is in progress, but even if
///   they never read (or dispose early) the copy continues in the
///   background
/// • Drains the source to EOF, then disposes it.
/// • Dispose / DisposeAsync are non-blocking; they simply mark the public
///   wrapper dead and release any waiting readers.
/// • Note: This class was entirely authored by ChatGPT o3
/// </summary>
public sealed class BufferToEndStream : Stream
{
    // ───────────────────────────────────  configuration
    private readonly int  _segmentSize;

    // ───────────────────────────────────  pipeline & pump
    private readonly Pipe _pipe;
    private readonly Task _pumpTask;

    // ───────────────────────────────────  coordination primitives
    private readonly CancellationTokenSource _localCts = new();
    private readonly SemaphoreSlim           _readLock = new(1, 1);

    private volatile Exception? _backgroundError;
    private volatile bool       _publiclyDisposed;
    private int                 _coreDisposed;   // 0 ⇒ alive, 1 ⇒ core cleaned up

    // ───────────────────────────────────  constructor
    public BufferToEndStream(
        Stream sourceStream,
        int    minimumSegmentSize    = 1024,
        long   pauseWriterThreshold  = long.MaxValue,
        long   resumeWriterThreshold = long.MaxValue - 1)
    {
        if (sourceStream is null)  throw new ArgumentNullException(nameof(sourceStream));
        if (!sourceStream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(sourceStream));

        _segmentSize = Math.Max(256, minimumSegmentSize);

        _pipe = new Pipe(new PipeOptions(
            pool:                      null,                    // default pool
            readerScheduler:           PipeScheduler.ThreadPool,
            writerScheduler:           PipeScheduler.ThreadPool,
            pauseWriterThreshold:      pauseWriterThreshold,
            resumeWriterThreshold:     resumeWriterThreshold,
            minimumSegmentSize:        _segmentSize,
            useSynchronizationContext: false));

        _pumpTask = Task.Run(() => PumpAsync(sourceStream), SigtermUtil.GetCancellationToken());
    }

    // ───────────────────────────────────  background producer
    private async Task PumpAsync(Stream source)
    {
        byte[] scratch = ArrayPool<byte>.Shared.Rent(_segmentSize);

        try
        {
            while (true)
            {
                int read = await source.ReadAsync(scratch, 0, scratch.Length).ConfigureAwait(false);
                if (read == 0) break;                        // EOF

                if (!_publiclyDisposed)                      // normal mode: write to Pipe
                {
                    await _pipe.Writer.WriteAsync(
                            scratch.AsMemory(0, read),
                            SigtermUtil.GetCancellationToken()).ConfigureAwait(false);
                }
                // else: wrapper already disposed → just discard into 'scratch'
            }

            _pipe.Writer.Complete();                         // clean completion
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _backgroundError = ex;
            _pipe.Writer.Complete(ex);                       // propagate to readers
        }
        finally
        {
            source.Dispose();                                // ALWAYS reached
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    // ───────────────────────────────────  core read helper
    private async ValueTask<int> ReadCoreAsync(
        Memory<byte>      destination,
        CancellationToken cancellationToken)
    {
        ThrowIfCoreDisposed();
        if (destination.Length == 0) return 0;

        using var linked = CancellationTokenSource
                           .CreateLinkedTokenSource(_localCts.Token, cancellationToken);

        await _readLock.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            while (true)
            {
                ReadResult result = await _pipe.Reader
                                               .ReadAsync(linked.Token)
                                               .ConfigureAwait(false);
                var buffer = result.Buffer;

                if (!buffer.IsEmpty)
                {
                    int n = (int)Math.Min(buffer.Length, destination.Length);
                    buffer.Slice(0, n).CopyTo(destination.Span);
                    _pipe.Reader.AdvanceTo(buffer.GetPosition(n));
                    return n;
                }

                if (result.IsCompleted)
                {
                    _pipe.Reader.AdvanceTo(buffer.End);

                    if (_backgroundError is not null)
                        ExceptionDispatchInfo.Capture(_backgroundError).Throw();

                    return 0;                    // true EOF
                }

                _pipe.Reader.AdvanceTo(buffer.Start, buffer.End);   // nothing yet → loop
            }
        }
        finally
        {
            _readLock.Release();
        }
    }

    // ───────────────────────────────────  Stream overrides
    public override int Read(Span<byte> buffer)
    {
        // simple sync path – allocate once then copy
        byte[] tmp = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            int read = ReadCoreAsync(tmp.AsMemory(0, buffer.Length), SigtermUtil.GetCancellationToken())
                       .AsTask().GetAwaiter().GetResult();
            tmp.AsSpan(0, read).CopyTo(buffer);
            return read;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(tmp);
        }
    }

    public override int Read(byte[] array, int offset, int count) =>
        Read(array.AsSpan(offset, count));

    public override ValueTask<int> ReadAsync(
        Memory<byte>      buffer,
        CancellationToken cancellationToken = default) =>
        ReadCoreAsync(buffer, cancellationToken);

    public override Task<int> ReadAsync(
        byte[] array, int offset, int count, CancellationToken cancellationToken) =>
        ReadCoreAsync(array.AsMemory(offset, count), cancellationToken).AsTask();

    // ───────────────────────────────────  Dispose pattern
    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _coreDisposed, 1) == 1) return;

        if (disposing)
        {
            _publiclyDisposed = true;             // pump switches to discard mode
            _localCts.Cancel();                   // cancel any waiting readers
            _readLock.Dispose();                  // forbid further reads

            try { _pipe.Reader.Complete(); }      // maybe already done – ignore
            catch (InvalidOperationException) { }
        }

        base.Dispose(disposing);                  // returns immediately
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _coreDisposed, 1) == 1) return;

        _publiclyDisposed = true;
        _localCts.Cancel();
        _readLock.Dispose();

        try { _pipe.Reader.Complete(); }
        catch (InvalidOperationException) { }

        await base.DisposeAsync().ConfigureAwait(false); // still non-blocking
    }

    // ───────────────────────────────────  plumbing
    public override bool CanRead  => _coreDisposed == 0;
    public override bool CanSeek  => false;
    public override bool CanWrite => false;

    public override long Length   => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() =>
        throw new NotSupportedException();
    public override Task FlushAsync(CancellationToken _) =>
        throw new NotSupportedException();

    public override long Seek(long _, SeekOrigin __) =>
        throw new NotSupportedException();
    public override void SetLength(long _) =>
        throw new NotSupportedException();

    public override void Write(byte[] _, int __, int ___) =>
        throw new NotSupportedException();
    public override void Write(ReadOnlySpan<byte> _) =>
        throw new NotSupportedException();
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> _, CancellationToken __ = default) =>
        throw new NotSupportedException();

    // ───────────────────────────────────  helpers
    private void ThrowIfCoreDisposed()
    {
        if (_coreDisposed != 0)
            throw new ObjectDisposedException(nameof(BufferToEndStream));
    }
}
