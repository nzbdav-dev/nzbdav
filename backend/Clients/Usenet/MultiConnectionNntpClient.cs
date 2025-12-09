using NzbWebDAV.Clients.Usenet.Concurrency;
using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Models;
using UsenetSharp.Models;

namespace NzbWebDAV.Clients.Usenet;

/// <summary>
/// This client is responsible for delegating NNTP commands to a connection pool.
///   * The connection pool enforces a maximum number of allowed connections
///   * When a connection is available, the NNTP command executes immediately
///   * When a connection is not available, the NNTP command waits until a connection becomes available.
///   * When multiple commands are awaiting a connection,
///     then BODY/ARTICLE commands have higher priority than STAT/HEAD/DATE commands.
/// </summary>
/// <param name="connectionPool"></param>
/// <param name="type"></param>
public class MultiConnectionNntpClient(ConnectionPool<INntpClient> connectionPool, ProviderType type) : NntpClient
{
    public ProviderType ProviderType { get; } = type;
    public int LiveConnections => connectionPool.LiveConnections;
    public int IdleConnections => connectionPool.IdleConnections;
    public int ActiveConnections => connectionPool.ActiveConnections;
    public int AvailableConnections => connectionPool.AvailableConnections;

    public override Task ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Please connect within the connectionFactory");
    }

    public override Task<UsenetResponse> AuthenticateAsync(string user, string pass, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Please authenticate within the connectionFactory");
    }

    public override async Task<UsenetStatResponse> StatAsync(SegmentId segmentId, CancellationToken ct)
    {
        const SemaphorePriority priority = SemaphorePriority.Low;
        using var connectionLock = await connectionPool.GetConnectionLockAsync(priority, ct).ConfigureAwait(false);
        return await connectionLock.Connection.StatAsync(segmentId, ct).ConfigureAwait(false);
    }

    public override async Task<UsenetHeadResponse> HeadAsync(SegmentId segmentId, CancellationToken ct)
    {
        const SemaphorePriority priority = SemaphorePriority.Low;
        using var connectionLock = await connectionPool.GetConnectionLockAsync(priority, ct).ConfigureAwait(false);
        return await connectionLock.Connection.HeadAsync(segmentId, ct).ConfigureAwait(false);
    }

    public override async Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId, CancellationToken ct)
    {
        const SemaphorePriority priority = SemaphorePriority.High;
        var connectionLock = await connectionPool.GetConnectionLockAsync(priority, ct).ConfigureAwait(false);
        return await connectionLock.Connection.DecodedBodyAsync(segmentId, OnDone, ct).ConfigureAwait(false);

        void OnDone(ArticleBodyResult articleBodyResult)
        {
            connectionLock.Dispose();
        }
    }

    public override async Task<UsenetDecodedArticleResponse> DecodedArticleAsync
    (
        SegmentId segmentId, 
        CancellationToken ct
    )
    {
        const SemaphorePriority priority = SemaphorePriority.High;
        var connectionLock = await connectionPool.GetConnectionLockAsync(priority, ct).ConfigureAwait(false);
        return await connectionLock.Connection.DecodedArticleAsync(segmentId, OnDone, ct).ConfigureAwait(false);

        void OnDone(ArticleBodyResult articleBodyResult)
        {
            connectionLock.Dispose();
        }
    }

    public override async Task<UsenetDateResponse> DateAsync(CancellationToken ct)
    {
        const SemaphorePriority priority = SemaphorePriority.Low;
        using var connectionLock = await connectionPool.GetConnectionLockAsync(priority, ct).ConfigureAwait(false);
        return await connectionLock.Connection.DateAsync(ct).ConfigureAwait(false);
    }

    public override async Task<UsenetDecodedBodyResponse> DecodedBodyAsync
    (
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken ct
    )
    {
        const SemaphorePriority priority = SemaphorePriority.High;
        var connectionLock = await connectionPool.GetConnectionLockAsync(priority, ct).ConfigureAwait(false);
        return await connectionLock.Connection.DecodedBodyAsync(segmentId, OnDone, ct).ConfigureAwait(false);

        void OnDone(ArticleBodyResult articleBodyResult)
        {
            connectionLock.Dispose();
            onConnectionReadyAgain?.Invoke(articleBodyResult);
        }
    }

    public override async Task<UsenetDecodedArticleResponse> DecodedArticleAsync
    (
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken ct
    )
    {
        const SemaphorePriority priority = SemaphorePriority.High;
        var connectionLock = await connectionPool.GetConnectionLockAsync(priority, ct).ConfigureAwait(false);
        return await connectionLock.Connection.DecodedArticleAsync(segmentId, OnDone, ct).ConfigureAwait(false);

        void OnDone(ArticleBodyResult articleBodyResult)
        {
            connectionLock.Dispose();
            onConnectionReadyAgain?.Invoke(articleBodyResult);
        }
    }

    public override void Dispose()
    {
        connectionPool.Dispose();
        GC.SuppressFinalize(this);
    }
}