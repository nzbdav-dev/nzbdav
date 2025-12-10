using UsenetSharp.Models;
using UsenetSharp.Streams;

namespace NzbWebDAV.Streams;

/// <summary>
/// A YencStream that reads from already-decoded cached data instead of decoding yenc-encoded data.
/// This stream bypasses the yenc decoding process by directly serving cached decoded bytes
/// and returning pre-parsed yenc headers.
/// </summary>
public class CachedYencStream : YencStream
{
    private readonly UsenetYencHeader _cachedHeaders;
    private readonly Stream _cachedDecodedStream;

    public CachedYencStream(UsenetYencHeader cachedHeaders, Stream cachedDecodedStream) : base(Null)
    {
        _cachedHeaders = cachedHeaders ?? throw new ArgumentNullException(nameof(cachedHeaders));
        _cachedDecodedStream = cachedDecodedStream ?? throw new ArgumentNullException(nameof(cachedDecodedStream));
    }

    public override ValueTask<UsenetYencHeader?> GetYencHeadersAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<UsenetYencHeader?>(_cachedHeaders);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return _cachedDecodedStream.ReadAsync(buffer, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cachedDecodedStream?.Dispose();
        }

        base.Dispose(disposing);
    }
}