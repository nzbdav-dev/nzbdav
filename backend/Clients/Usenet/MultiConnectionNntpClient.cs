using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Streams;
using Usenet.Exceptions;
using Usenet.Nntp.Responses;
using Usenet.Nzb;
using Usenet.Yenc;

namespace NzbWebDAV.Clients.Usenet;

public class MultiConnectionNntpClient(ConnectionPool<INntpClient> connectionPool) : INntpClient
{
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

    public Task<YencHeaderStream> GetSegmentStreamAsync(string segmentId, bool includeHeaders, CancellationToken cancellationToken)
    {
        return RunWithConnection(connection => connection.GetSegmentStreamAsync(segmentId, includeHeaders, cancellationToken), cancellationToken);
    }

    public Task<YencHeader> GetSegmentYencHeaderAsync(string segmentId, CancellationToken cancellationToken)
    {
        return RunWithConnection(connection => connection.GetSegmentYencHeaderAsync(segmentId, cancellationToken), cancellationToken);
    }

    public Task<long> GetFileSizeAsync(NzbFile file, CancellationToken cancellationToken)
    {
        return RunWithConnection(connection => connection.GetFileSizeAsync(file, cancellationToken), cancellationToken);
    }

    public async Task WaitForReady(CancellationToken cancellationToken)
    {
        await using var connectionLock = await _connectionPool.GetConnectionLockAsync(cancellationToken);
    }

    private async Task<T> RunWithConnection<T>
    (
        Func<INntpClient, Task<T>> task,
        CancellationToken cancellationToken,
        int retries = 1
    )
    {
        var connectionLock = await _connectionPool.GetConnectionLockAsync(cancellationToken);
        try
        {
            var result = await task(connectionLock.Connection);

            // we only want to release the connection-lock once the underlying connection is ready again.
            // ReSharper disable once MethodSupportsCancellation
            // we intentionally do not pass the cancellation token to ContinueWith,
            // since we want the continuation to always run.
            _ = connectionLock.Connection.WaitForReady(CancellationToken.None)
                .ContinueWith(_ => connectionLock.Dispose());
            return result;
        }
        catch (NntpException e)
        {
            // we want to replace the underlying connection in cases of NntpExceptions.
            connectionLock.Replace();
            connectionLock.Dispose();

            // and try again with a new connection (max 1 retry)
            if (retries > 0)
                return await RunWithConnection<T>(task, cancellationToken, retries - 1);

            throw;
        }
        catch (Exception e)
        {
            // we also want to release the connection-lock if there was any error getting the result.
            connectionLock.Dispose();
            throw;
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