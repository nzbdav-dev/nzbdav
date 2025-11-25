using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Models;
using NzbWebDAV.Streams;
using NzbWebDAV.Utils;
using Usenet.Exceptions;
using Usenet.Nntp.Responses;
using Usenet.Nzb;
using Usenet.Yenc;

namespace NzbWebDAV.Clients.Usenet;

public class MultiConnectionNntpClient(ConnectionPool<INntpClient> connectionPool, ProviderType type) : INntpClient
{
    public ProviderType ProviderType { get; } = type;
    public int LiveConnections => _connectionPool.LiveConnections;
    public int IdleConnections => _connectionPool.IdleConnections;
    public int ActiveConnections => _connectionPool.ActiveConnections;
    public int AvailableConnections => _connectionPool.AvailableConnections;
    public int RemainingSemaphoreSlots => _connectionPool.RemainingSemaphoreSlots;

    private ConnectionPool<INntpClient> _connectionPool = connectionPool;

    public Task<bool> ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Please connect within the connectionFactory");
    }

    public Task<bool> AuthenticateAsync(string user, string pass, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Please authenticate within the connectionFactory");
    }

    public Task<NntpStatResponse> StatAsync(string segmentId, CancellationToken cancellationToken)
    {
        return RunWithConnection(connection => connection.StatAsync(segmentId, cancellationToken), cancellationToken);
    }

    public Task<NntpDateResponse> DateAsync(CancellationToken cancellationToken)
    {
        return RunWithConnection(connection => connection.DateAsync(cancellationToken), cancellationToken);
    }

    public Task<UsenetArticleHeaders> GetArticleHeadersAsync(string segmentId, CancellationToken cancellationToken)
    {
        return RunWithConnection(connection => connection.GetArticleHeadersAsync(segmentId, cancellationToken),
            cancellationToken);
    }

    public Task<YencHeaderStream> GetSegmentStreamAsync(string segmentId, bool includeHeaders,
        CancellationToken cancellationToken)
    {
        return RunWithConnection(
            connection => connection.GetSegmentStreamAsync(segmentId, includeHeaders, cancellationToken),
            cancellationToken);
    }

    public Task<YencHeader> GetSegmentYencHeaderAsync(string segmentId, CancellationToken cancellationToken)
    {
        return RunWithConnection(connection => connection.GetSegmentYencHeaderAsync(segmentId, cancellationToken),
            cancellationToken);
    }

    public Task<long> GetFileSizeAsync(NzbFile file, CancellationToken cancellationToken)
    {
        return RunWithConnection(connection => connection.GetFileSizeAsync(file, cancellationToken), cancellationToken);
    }

    public async Task WaitForReady(CancellationToken cancellationToken)
    {
        using var connectionLock =
            await _connectionPool.GetConnectionLockAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> RunWithConnection<T>
    (
        Func<INntpClient, Task<T>> task,
        CancellationToken cancellationToken,
        int retries = 1
    )
    {
        var connectionLock = await _connectionPool.GetConnectionLockAsync(cancellationToken).ConfigureAwait(false);
        var isDisposed = false;
        try
        {
            return await task(connectionLock.Connection).ConfigureAwait(false);
        }
        catch (NntpException)
        {
            // we want to replace the underlying connection in cases of NntpExceptions.
            connectionLock.Replace();
            connectionLock.Dispose();
            isDisposed = true;

            // and try again with a new connection (max 1 retry)
            if (retries > 0)
                return await RunWithConnection<T>(task, cancellationToken, retries - 1).ConfigureAwait(false);

            throw;
        }
        finally
        {
            // we only want to release the connection-lock once the underlying connection is ready again.
            //
            // ReSharper disable once MethodSupportsCancellation
            // we intentionally do not pass the cancellation token to ContinueWith,
            // since we want the continuation to always run.
            if (!isDisposed)
                _ = connectionLock.Connection.WaitForReady(SigtermUtil.GetCancellationToken())
                    .ContinueWith(_ => connectionLock.Dispose());
        }
    }

    public void UpdateConnectionPool(ConnectionPool<INntpClient> connectionPool)
    {
        var oldConnectionPool = _connectionPool;
        _connectionPool = connectionPool;
        oldConnectionPool.Dispose();
    }

    public void Dispose()
    {
        _connectionPool.Dispose();
        GC.SuppressFinalize(this);
    }
}