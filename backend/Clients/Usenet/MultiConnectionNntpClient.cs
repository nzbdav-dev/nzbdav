using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Models;
using UsenetSharp.Models;

namespace NzbWebDAV.Clients.Usenet;

public class MultiConnectionNntpClient(ConnectionPool<INntpClient> connectionPool, ProviderType type)
    : INntpClient
{
    public ProviderType ProviderType { get; } = type;
    public int LiveConnections => connectionPool.LiveConnections;
    public int IdleConnections => connectionPool.IdleConnections;
    public int ActiveConnections => connectionPool.ActiveConnections;
    public int AvailableConnections => connectionPool.AvailableConnections;

    public Task ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Please connect within the connectionFactory");
    }

    public Task<UsenetResponse> AuthenticateAsync(string user, string pass, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Please authenticate within the connectionFactory");
    }

    public async Task<UsenetStatResponse> StatAsync(SegmentId segmentId, CancellationToken ct)
    {
        using var connectionLock = await connectionPool.GetConnectionLockAsync(ct).ConfigureAwait(false);
        return await connectionLock.Connection.StatAsync(segmentId, ct).ConfigureAwait(false);
    }

    public async Task<UsenetHeadResponse> HeadAsync(SegmentId segmentId, CancellationToken ct)
    {
        using var connectionLock = await connectionPool.GetConnectionLockAsync(ct).ConfigureAwait(false);
        return await connectionLock.Connection.HeadAsync(segmentId, ct).ConfigureAwait(false);
    }

    public async Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId, CancellationToken ct)
    {
        var connectionLock = await connectionPool.GetConnectionLockAsync(ct).ConfigureAwait(false);
        return await connectionLock.Connection.DecodedBodyAsync(segmentId, OnDone, ct).ConfigureAwait(false);

        void OnDone(ArticleBodyResult articleBodyResult)
        {
            connectionLock.Dispose();
        }
    }

    public async Task<UsenetDecodedArticleResponse> DecodedArticleAsync(SegmentId segmentId, CancellationToken ct)
    {
        var connectionLock = await connectionPool.GetConnectionLockAsync(ct).ConfigureAwait(false);
        return await connectionLock.Connection.DecodedArticleAsync(segmentId, OnDone, ct).ConfigureAwait(false);

        void OnDone(ArticleBodyResult articleBodyResult)
        {
            connectionLock.Dispose();
        }
    }

    public async Task<UsenetDateResponse> DateAsync(CancellationToken ct)
    {
        using var connectionLock = await connectionPool.GetConnectionLockAsync(ct).ConfigureAwait(false);
        return await connectionLock.Connection.DateAsync(ct).ConfigureAwait(false);
    }

    public async Task WaitForReadyAsync(CancellationToken ct)
    {
        using var connectionLock = await connectionPool.GetConnectionLockAsync(ct).ConfigureAwait(false);
    }

    public async Task<UsenetDecodedBodyResponse> DecodedBodyAsync
    (
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken ct
    )
    {
        var connectionLock = await connectionPool.GetConnectionLockAsync(ct).ConfigureAwait(false);
        return await connectionLock.Connection.DecodedBodyAsync(segmentId, OnDone, ct).ConfigureAwait(false);

        void OnDone(ArticleBodyResult articleBodyResult)
        {
            connectionLock.Dispose();
            onConnectionReadyAgain?.Invoke(articleBodyResult);
        }
    }

    public async Task<UsenetDecodedArticleResponse> DecodedArticleAsync
    (
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken ct
    )
    {
        var connectionLock = await connectionPool.GetConnectionLockAsync(ct).ConfigureAwait(false);
        return await connectionLock.Connection.DecodedArticleAsync(segmentId, OnDone, ct).ConfigureAwait(false);

        void OnDone(ArticleBodyResult articleBodyResult)
        {
            connectionLock.Dispose();
            onConnectionReadyAgain?.Invoke(articleBodyResult);
        }
    }

    public async Task<UsenetYencHeader> GetYencHeadersAsync(string segmentId, CancellationToken ct)
    {
        var decodedBodyResponse = await DecodedBodyAsync(segmentId, ct).ConfigureAwait(false);
        await using var stream = decodedBodyResponse.Stream;
        var headers = await stream.GetYencHeadersAsync(ct).ConfigureAwait(false);
        return headers!;
    }

    public void Dispose()
    {
        connectionPool.Dispose();
        GC.SuppressFinalize(this);
    }
}