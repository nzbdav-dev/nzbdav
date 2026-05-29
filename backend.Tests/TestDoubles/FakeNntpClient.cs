using System.Collections.Concurrent;
using System.Reflection;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Streams;
using UsenetSharp.Models;

namespace NzbWebDAV.Tests.TestDoubles;

public sealed class FakeNntpClient : NntpClient
{
    private sealed class TrackingMemoryStream(byte[] buffer, Action onDispose) : MemoryStream(buffer, writable: false)
    {
        private readonly Action _onDispose = onDispose;
        private bool _disposed;

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _onDispose();
                _disposed = true;
            }

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _onDispose();
                _disposed = true;
            }

            return base.DisposeAsync();
        }
    }

    private sealed record SegmentData(
        byte[] Bytes,
        long PartOffset,
        UsenetYencHeader YencHeader,
        UsenetArticleHeader ArticleHeaders
    );

    private readonly ConcurrentDictionary<string, SegmentData> _segments = new(StringComparer.Ordinal);

    private int _getYencHeadersCallCount;
    private int _decodedBodyCallCount;
    private int _decodedArticleCallCount;
    private int _headCallCount;
    private int _statCallCount;

    public int GetYencHeadersCallCount => Volatile.Read(ref _getYencHeadersCallCount);
    public int DecodedBodyCallCount => Volatile.Read(ref _decodedBodyCallCount);
    public int DecodedArticleCallCount => Volatile.Read(ref _decodedArticleCallCount);
    public int HeadCallCount => Volatile.Read(ref _headCallCount);
    public int StatCallCount => Volatile.Read(ref _statCallCount);

    public FakeNntpClient AddSegment(string segmentId, byte[] bytes, long partOffset = 0)
    {
        var header = CreateYencHeader(partOffset, bytes.Length);
        var articleHeaders = CreateArticleHeader(segmentId);
        _segments[segmentId] = new SegmentData(bytes, partOffset, header, articleHeaders);
        return this;
    }

    public override Task ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public override Task<UsenetResponse> AuthenticateAsync(string user, string pass, CancellationToken cancellationToken)
    {
        return Task.FromException<UsenetResponse>(new NotSupportedException());
    }

    public override Task<UsenetStatResponse> StatAsync(SegmentId segmentId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _statCallCount);
        _ = GetSegment(segmentId);
        return Task.FromResult(new UsenetStatResponse
        {
            ArticleExists = true,
            ResponseCode = (int)UsenetResponseType.ArticleExists,
            ResponseMessage = "223 - Article exists"
        });
    }

    public override Task<UsenetHeadResponse> HeadAsync(SegmentId segmentId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _headCallCount);
        var data = GetSegment(segmentId);
        return Task.FromResult(new UsenetHeadResponse
        {
            SegmentId = segmentId,
            ResponseCode = (int)UsenetResponseType.ArticleRetrievedHeadFollows,
            ResponseMessage = "221 - Head retrieved",
            ArticleHeaders = data.ArticleHeaders
        });
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId, CancellationToken cancellationToken)
    {
        return DecodedBodyAsync(segmentId, onConnectionReadyAgain: null, cancellationToken);
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken
    )
    {
        Interlocked.Increment(ref _decodedBodyCallCount);
        onConnectionReadyAgain?.Invoke(ArticleBodyResult.Retrieved);
        return Task.FromResult(CreateBodyResponse(segmentId));
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId,
        CancellationToken cancellationToken
    )
    {
        return DecodedArticleAsync(segmentId, onConnectionReadyAgain: null, cancellationToken);
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken
    )
    {
        Interlocked.Increment(ref _decodedArticleCallCount);
        var data = GetSegment(segmentId);
        onConnectionReadyAgain?.Invoke(ArticleBodyResult.Retrieved);
        return Task.FromResult(new UsenetDecodedArticleResponse
        {
            SegmentId = segmentId,
            ResponseCode = (int)UsenetResponseType.ArticleRetrievedHeadAndBodyFollow,
            ResponseMessage = "220 - Article retrieved",
            ArticleHeaders = data.ArticleHeaders,
            Stream = CreateBodyStream(segmentId, data)
        });
    }

    public override Task<UsenetDateResponse> DateAsync(CancellationToken cancellationToken)
    {
        return Task.FromException<UsenetDateResponse>(new NotSupportedException());
    }

    public override Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync(
        string segmentId,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult(new UsenetExclusiveConnection(onConnectionReadyAgain: null));
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken
    )
    {
        return DecodedBodyAsync(segmentId, exclusiveConnection.OnConnectionReadyAgain, cancellationToken);
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken
    )
    {
        return DecodedArticleAsync(segmentId, exclusiveConnection.OnConnectionReadyAgain, cancellationToken);
    }

    public override Task<UsenetYencHeader> GetYencHeadersAsync(string segmentId, CancellationToken ct)
    {
        Interlocked.Increment(ref _getYencHeadersCallCount);
        return Task.FromResult(GetSegment(segmentId).YencHeader);
    }

    public override void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private UsenetDecodedBodyResponse CreateBodyResponse(string segmentId)
    {
        var data = GetSegment(segmentId);
        return new UsenetDecodedBodyResponse
        {
            SegmentId = segmentId,
            ResponseCode = (int)UsenetResponseType.ArticleRetrievedBodyFollows,
            ResponseMessage = "222 - Body retrieved",
            Stream = CreateBodyStream(segmentId, data)
        };
    }

    private CachedYencStream CreateBodyStream(string segmentId, SegmentData data)
    {
        var stream = new TrackingMemoryStream(data.Bytes, () => { });
        return new CachedYencStream(data.YencHeader, stream);
    }

    private SegmentData GetSegment(string segmentId)
    {
        if (_segments.TryGetValue(segmentId, out var data))
            return data;

        throw new UsenetArticleNotFoundException(segmentId);
    }

    private static UsenetYencHeader CreateYencHeader(long partOffset, long partSize)
    {
        return new UsenetYencHeader
        {
            FileName = "segment.bin",
            FileSize = partOffset + partSize,
            LineLength = 128,
            PartNumber = 1,
            TotalParts = 1,
            PartSize = partSize,
            PartOffset = partOffset
        };
    }

    private static UsenetArticleHeader CreateArticleHeader(string segmentId)
    {
        return new UsenetArticleHeader
        {
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Subject"] = segmentId
            }
        };
    }
}
