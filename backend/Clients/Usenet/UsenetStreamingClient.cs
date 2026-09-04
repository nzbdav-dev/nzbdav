using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Config;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Websocket;
using UsenetSharp.Models;

namespace NzbWebDAV.Clients.Usenet;

public class UsenetStreamingClient : WrappingNntpClient
{
    private readonly ConfigManager _configManager;

    public UsenetStreamingClient(ConfigManager configManager, WebsocketManager websocketManager)
        : base(CreateDownloadingNntpClient(configManager, websocketManager))
    {
        _configManager = configManager;

        // when config changes, create a new MultiProviderClient to use instead.
        configManager.OnConfigChanged += (_, configEventArgs) =>
        {
            // if unrelated config changed, do nothing
            if (!configEventArgs.ChangedConfig.ContainsKey("usenet.providers")) return;

            // update the connection-pool according to the new config
            var newUsenetClient = CreateDownloadingNntpClient(configManager, websocketManager);
            ReplaceUnderlyingClient(newUsenetClient);
        };
    }

    /// <summary>
    /// STAT-checks every segment, failing fast on the first one that is missing on all providers.
    /// When the NNTP pipelining master switch is on, segments are checked in pipelined batches
    /// (per-provider pipelining is gated further down the chain); otherwise this defers to the
    /// base one-command-per-round-trip implementation.
    /// </summary>
    public override async Task CheckAllSegmentsAsync
    (
        IEnumerable<string> segmentIds,
        int concurrency,
        IProgress<int>? progress,
        CancellationToken cancellationToken
    )
    {
        // Stay on the original one-at-a-time path unless pipelining is switched on AND at least one
        // provider has actually opted in -- so enabling the master switch alone changes nothing until
        // a provider is tested and enabled.
        var depth = _configManager.GetNntpPipeliningDepth();
        var anyProviderPipelined = _configManager.GetUsenetProviderConfig()
            .Providers.Any(p => p.StatPipeliningEnabled);
        if (!_configManager.GetNntpPipeliningEnabled() || depth <= 1 || !anyProviderPipelined)
        {
            await base.CheckAllSegmentsAsync(segmentIds, concurrency, progress, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        using var childCt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = childCt.Token;

        // Check the segments in pipelined batches of `depth`, running up to `concurrency` batches at
        // once so every pooled connection stays busy. Depth is the user-facing lever: a deeper
        // pipeline hides more round-trip latency; if a provider handles deep batches poorly, lower it.
        var batches = segmentIds.Chunk(depth);
        var tasks = batches
            .Select(async batch => (
                Batch: batch,
                Results: await StatPipelinedAsync(batch, token).ConfigureAwait(false)
            ))
            .WithConcurrencyAsync(concurrency);

        var processed = 0;
        await foreach (var task in tasks.ConfigureAwait(false))
        {
            for (var i = 0; i < task.Batch.Length; i++)
            {
                progress?.Report(++processed);
                if (task.Results[i].ResponseType == UsenetResponseType.ArticleExists) continue;
                await childCt.CancelAsync().ConfigureAwait(false);
                throw new UsenetArticleNotFoundException(task.Batch[i]);
            }
        }
    }

    private static DownloadingNntpClient CreateDownloadingNntpClient
    (
        ConfigManager configManager,
        WebsocketManager websocketManager
    )
    {
        var multiProviderClient = CreateMultiProviderClient(configManager, websocketManager);
        return new DownloadingNntpClient(multiProviderClient, configManager);
    }

    private static MultiProviderNntpClient CreateMultiProviderClient
    (
        ConfigManager configManager,
        WebsocketManager websocketManager
    )
    {
        var providerConfig = configManager.GetUsenetProviderConfig();
        var connectionPoolStats = new ConnectionPoolStats(providerConfig, websocketManager);
        var providerClients = providerConfig.Providers
            .Select((provider, index) => CreateProviderClient(
                provider,
                connectionPoolStats.GetOnConnectionPoolChanged(index)
            ))
            .ToList();
        return new MultiProviderNntpClient(providerClients);
    }

    private static MultiConnectionNntpClient CreateProviderClient
    (
        UsenetProviderConfig.ConnectionDetails connectionDetails,
        EventHandler<ConnectionPoolStats.ConnectionPoolChangedEventArgs> onConnectionPoolChanged
    )
    {
        var connectionPool = CreateNewConnectionPool(
            maxConnections: connectionDetails.MaxConnections,
            connectionFactory: ct => CreateNewConnection(connectionDetails, ct),
            onConnectionPoolChanged
        );
        var circuitBreaker = new ProviderCircuitBreaker(connectionDetails.Host);
        return new MultiConnectionNntpClient(
            connectionPool,
            connectionDetails.Type,
            circuitBreaker,
            connectionDetails.Host,
            connectionDetails.StatPipeliningEnabled);
    }

    private static ConnectionPool<INntpClient> CreateNewConnectionPool
    (
        int maxConnections,
        Func<CancellationToken, ValueTask<INntpClient>> connectionFactory,
        EventHandler<ConnectionPoolStats.ConnectionPoolChangedEventArgs> onConnectionPoolChanged
    )
    {
        var connectionPool = new ConnectionPool<INntpClient>(maxConnections, connectionFactory);
        connectionPool.OnConnectionPoolChanged += onConnectionPoolChanged;
        var args = new ConnectionPoolStats.ConnectionPoolChangedEventArgs(0, 0, maxConnections);
        onConnectionPoolChanged(connectionPool, args);
        return connectionPool;
    }

    public static async ValueTask<INntpClient> CreateNewConnection
    (
        UsenetProviderConfig.ConnectionDetails connectionDetails,
        CancellationToken ct
    )
    {
        var connection = new BaseNntpClient();
        var host = connectionDetails.Host;
        var port = connectionDetails.Port;
        var useSsl = connectionDetails.UseSsl;
        var user = connectionDetails.User;
        var pass = connectionDetails.Pass;
        await connection.ConnectAsync(host, port, useSsl, ct).ConfigureAwait(false);
        await connection.AuthenticateAsync(user, pass, ct).ConfigureAwait(false);
        return connection;
    }
}