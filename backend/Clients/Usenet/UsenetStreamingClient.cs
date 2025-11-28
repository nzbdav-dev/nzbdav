using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Config;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Streams;
using NzbWebDAV.Websocket;
using Usenet.Nntp.Responses;
using Usenet.Nzb;

namespace NzbWebDAV.Clients.Usenet;

public class UsenetStreamingClient
{
    private readonly CachingNntpClient _client;
    private readonly WebsocketManager _websocketManager;

    public UsenetStreamingClient(ConfigManager configManager, WebsocketManager websocketManager)
    {
        // initialize private members
        _websocketManager = websocketManager;

        // get connection settings from config-manager
        var providerConfig = configManager.GetUsenetProviderConfig();

        // initialize the nntp-client
        var multiProviderClient = CreateMultiProviderClient(providerConfig);
        var cache = new MemoryCache(new MemoryCacheOptions() { SizeLimit = 8192 });
        _client = new CachingNntpClient(multiProviderClient, cache);

        // when config changes, update the connection-pool
        configManager.OnConfigChanged += (_, configEventArgs) =>
        {
            // if unrelated config changed, do nothing
            if (!configEventArgs.ChangedConfig.TryGetValue("usenet.providers", out var rawConfig)) return;

            // update the connection-pool according to the new config
            var newProviderConfig = JsonSerializer.Deserialize<UsenetProviderConfig>(rawConfig);
            var newMultiProviderClient = CreateMultiProviderClient(newProviderConfig!);
            _client.UpdateUnderlyingClient(newMultiProviderClient);
        };
    }

    public async Task CheckAllSegmentsAsync
    (
        IEnumerable<string> segmentIds,
        int concurrency,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        using var childCt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var _1 = childCt.Token.SetScopedContext(cancellationToken.GetContext<ReservedPooledConnectionsContext>());
        using var _2 = childCt.Token.SetScopedContext(cancellationToken.GetContext<LastSuccessfulProviderContext>());
        using var _3 = childCt.Token.SetScopedContext(cancellationToken.GetContext<ConnectionUsageContext>());
        var token = childCt.Token;

        var tasks = segmentIds
            .Select(async x => (
                SegmentId: x,
                Result: await _client.StatAsync(x, token).ConfigureAwait(false)
            ))
            .WithConcurrencyAsync(concurrency);

        var processed = 0;
        await foreach (var task in tasks.ConfigureAwait(false))
        {
            progress?.Report(++processed);
            if (task.Result.ResponseType == NntpStatResponseType.ArticleExists) continue;
            await childCt.CancelAsync().ConfigureAwait(false);
            throw new UsenetArticleNotFoundException(task.SegmentId);
        }
    }

    public async Task<NzbFileStream> GetFileStream(NzbFile nzbFile, int concurrentConnections, CancellationToken ct)
    {
        var segmentIds = nzbFile.GetSegmentIds();
        var fileSize = await _client.GetFileSizeAsync(nzbFile, ct).ConfigureAwait(false);
        return new NzbFileStream(segmentIds, fileSize, _client, concurrentConnections);
    }

    public NzbFileStream GetFileStream(NzbFile nzbFile, long fileSize, int concurrentConnections)
    {
        return new NzbFileStream(nzbFile.GetSegmentIds(), fileSize, _client, concurrentConnections);
    }

    public NzbFileStream GetFileStream(string[] segmentIds, long fileSize, int concurrentConnections, ConnectionUsageContext? usageContext = null, bool useBufferedStreaming = true, int bufferSize = 10)
    {
        return new NzbFileStream(segmentIds, fileSize, _client, concurrentConnections, usageContext, useBufferedStreaming, bufferSize);
    }

    public Task<YencHeaderStream> GetSegmentStreamAsync(string segmentId, bool includeHeaders, CancellationToken ct)
    {
        return _client.GetSegmentStreamAsync(segmentId, includeHeaders, ct);
    }

    public Task<long> GetFileSizeAsync(NzbFile file, CancellationToken cancellationToken)
    {
        return _client.GetFileSizeAsync(file, cancellationToken);
    }

    public async Task<Dictionary<NzbFile, long>> GetFileSizesBatchAsync(
        IEnumerable<NzbFile> files,
        int concurrentConnections,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<NzbFile, long>();
        var filesToFetch = files.Where(f => f.Segments.Count > 0).ToList();

        if (filesToFetch.Count == 0)
            return results;

        var tasks = filesToFetch
            .Select(async file =>
            {
                var size = await _client.GetFileSizeAsync(file, cancellationToken).ConfigureAwait(false);
                return (file, size);
            })
            .WithConcurrencyAsync(concurrentConnections);

        await foreach (var (file, size) in tasks.ConfigureAwait(false))
        {
            results[file] = size;
        }

        return results;
    }

    public Task<UsenetArticleHeaders> GetArticleHeadersAsync(string segmentId, CancellationToken cancellationToken)
    {
        return _client.GetArticleHeadersAsync(segmentId, cancellationToken);
    }

    private ConnectionPool<INntpClient> CreateNewConnectionPool
    (
        int maxConnections,
        ExtendedSemaphoreSlim pooledSemaphore,
        Func<CancellationToken, ValueTask<INntpClient>> connectionFactory,
        EventHandler<ConnectionPoolStats.ConnectionPoolChangedEventArgs> onConnectionPoolChanged,
        ConnectionPoolStats connectionPoolStats,
        int providerIndex
    )
    {
        var connectionPool = new ConnectionPool<INntpClient>(maxConnections, pooledSemaphore, connectionFactory);
        connectionPool.OnConnectionPoolChanged += onConnectionPoolChanged;
        connectionPoolStats.RegisterConnectionPool(providerIndex, connectionPool);
        var args = new ConnectionPoolStats.ConnectionPoolChangedEventArgs(0, 0, maxConnections);
        onConnectionPoolChanged(connectionPool, args);
        return connectionPool;
    }

    private MultiProviderNntpClient CreateMultiProviderClient(UsenetProviderConfig providerConfig)
    {
        var connectionPoolStats = new ConnectionPoolStats(providerConfig, _websocketManager);
        var totalPooledConnectionCount = providerConfig.TotalPooledConnections;
        var pooledSemaphore = new ExtendedSemaphoreSlim(totalPooledConnectionCount, totalPooledConnectionCount);
        var providerClients = providerConfig.Providers
            .Select((provider, index) => CreateProviderClient(
                provider,
                connectionPoolStats,
                index,
                pooledSemaphore
            ))
            .ToList();
        return new MultiProviderNntpClient(providerClients);
    }

    private MultiConnectionNntpClient CreateProviderClient
    (
        UsenetProviderConfig.ConnectionDetails connectionDetails,
        ConnectionPoolStats connectionPoolStats,
        int providerIndex,
        ExtendedSemaphoreSlim pooledSemaphore
    )
    {
        var connectionPool = CreateNewConnectionPool(
            maxConnections: connectionDetails.MaxConnections,
            pooledSemaphore: pooledSemaphore,
            connectionFactory: ct => CreateNewConnection(connectionDetails, ct),
            onConnectionPoolChanged: connectionPoolStats.GetOnConnectionPoolChanged(providerIndex),
            connectionPoolStats: connectionPoolStats,
            providerIndex: providerIndex
        );
        return new MultiConnectionNntpClient(connectionPool, connectionDetails.Type);
    }

    public static async ValueTask<INntpClient> CreateNewConnection
    (
        UsenetProviderConfig.ConnectionDetails connectionDetails,
        CancellationToken cancellationToken
    )
    {
        var connection = new ThreadSafeNntpClient();
        var host = connectionDetails.Host;
        var port = connectionDetails.Port;
        var useSsl = connectionDetails.UseSsl;
        var user = connectionDetails.User;
        var pass = connectionDetails.Pass;
        if (!await connection.ConnectAsync(host, port, useSsl, cancellationToken).ConfigureAwait(false))
            throw new CouldNotConnectToUsenetException("Could not connect to usenet host. Check connection settings.");
        if (!await connection.AuthenticateAsync(user, pass, cancellationToken).ConfigureAwait(false))
            throw new CouldNotLoginToUsenetException("Could not login to usenet host. Check username and password.");
        return connection;
    }
}