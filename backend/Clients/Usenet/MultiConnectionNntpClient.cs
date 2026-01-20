using System.Diagnostics.CodeAnalysis;
using NzbWebDAV.Clients.Usenet.Concurrency;
using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models;
using Serilog;
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
[SuppressMessage("ReSharper", "AccessToDisposedClosure")]
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

    public override Task<UsenetResponse> AuthenticateAsync(string user, string pass,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Please authenticate within the connectionFactory");
    }

    public override Task<UsenetStatResponse> StatAsync(SegmentId segmentId, CancellationToken ct)
    {
        return RunWithConnection(
            SemaphorePriority.Low,
            connection => connection.StatAsync(segmentId, ct),
            ct
        );
    }

    public override Task<UsenetHeadResponse> HeadAsync(SegmentId segmentId, CancellationToken ct)
    {
        return RunWithConnection(
            SemaphorePriority.Low,
            connection => connection.HeadAsync(segmentId, ct),
            ct
        );
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(SegmentId segmentId, CancellationToken ct)
    {
        return RunWithConnection(
            SemaphorePriority.High,
            (connection, onDone) => connection.DecodedBodyAsync(segmentId, onDone, ct),
            onConnectionReadyAgain: null,
            ct
        );
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync
    (
        SegmentId segmentId,
        CancellationToken ct
    )
    {
        return RunWithConnection(
            SemaphorePriority.High,
            (connection, onDone) => connection.DecodedArticleAsync(segmentId, onDone, ct),
            onConnectionReadyAgain: null,
            ct
        );
    }

    public override Task<UsenetDateResponse> DateAsync(CancellationToken ct)
    {
        return RunWithConnection(
            SemaphorePriority.Low,
            connection => connection.DateAsync(ct),
            ct
        );
    }

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync
    (
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken ct
    )
    {
        return RunWithConnection(
            SemaphorePriority.High,
            (connection, onDone) => connection.DecodedBodyAsync(segmentId, onDone, ct),
            onConnectionReadyAgain,
            ct
        );
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync
    (
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken ct
    )
    {
        return RunWithConnection(
            SemaphorePriority.High,
            (connection, onDone) => connection.DecodedArticleAsync(segmentId, onDone, ct),
            onConnectionReadyAgain,
            ct
        );
    }

    private async Task<T> RunWithConnection<T>
    (
        SemaphorePriority priority,
        Func<INntpClient, Task<T>> command,
        CancellationToken ct,
        int retryCount = 1
    )
    {
        while (retryCount >= 0)
        {
            ConnectionLock<INntpClient>? connectionLock = null;
            try
            {
                connectionLock = await connectionPool.GetConnectionLockAsync(priority, ct).ConfigureAwait(false);
                var result = await command(connectionLock.Connection).ConfigureAwait(false);
                return result;
            }
            catch (Exception e)
            {
                connectionLock?.Replace();
                var message = "Error executing nntp command.";
                if (retryCount > 0) message += " Retrying with a new connection.";
                Log.Warning(e, message);
                if (retryCount <= 0) throw;
                else retryCount--;
            }
            finally
            {
                connectionLock?.Dispose();
            }
        }

        Log.Error("Unreachable code reached");
        throw new InvalidOperationException("Unreachable code ");
    }

    private async Task<T> RunWithConnection<T>
    (
        SemaphorePriority priority,
        Func<INntpClient, Action<ArticleBodyResult>, Task<T>> command,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken ct,
        int retryCount = 1
    ) where T : UsenetResponse
    {
        while (retryCount >= 0)
        {
            ConnectionLock<INntpClient>? connectionLock = null;
            try
            {
                connectionLock = await connectionPool.GetConnectionLockAsync(priority, ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                LogException(() => connectionLock?.Replace());
                LogException(() => connectionLock?.Dispose());
                if (retryCount > 0)
                {
                    Log.Warning(e, "Error getting connection-lock. Retrying with a new connection.");
                    retryCount--;
                    continue;
                }

                Log.Warning(e, "Error getting connection-lock.");
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }

            T? result;
            try
            {
                result = await command(connectionLock.Connection, OnConnectionReadyAgain).ConfigureAwait(false);
            }
            catch (Exception e) when (e.TryGetCausingException(out UsenetArticleNotFoundException _))
            {
                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }
            catch (Exception e) when (!e.TryGetCausingException(out UsenetArticleNotFoundException _))
            {
                LogException(() => connectionLock?.Replace());
                LogException(() => connectionLock?.Dispose());
                if (retryCount > 0)
                {
                    Log.Warning(e, "Error executing nntp command. Retrying with a new connection.");
                    retryCount--;
                    continue;
                }

                Log.Warning(e, "Error executing nntp command.");
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
                throw;
            }

            if ((result?.Success ?? false) == false)
            {
                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(ArticleBodyResult.NotRetrieved));
            }

            return result!;

            void OnConnectionReadyAgain(ArticleBodyResult articleBodyResult)
            {
                if (articleBodyResult != ArticleBodyResult.Retrieved) return;

                LogException(() => connectionLock?.Dispose());
                LogException(() => onConnectionReadyAgain?.Invoke(articleBodyResult));
            }
        }

        Log.Error("Unreachable code reached");
        throw new InvalidOperationException("Unreachable code ");
    }

    private static void LogException(Action? action)
    {
        try
        {
            action?.Invoke();
        }
        catch (Exception e)
        {
            Log.Warning(e, "Unhandled exception");
        }
    }

    public override void Dispose()
    {
        connectionPool.Dispose();
        GC.SuppressFinalize(this);
    }
}