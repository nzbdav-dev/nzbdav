using Microsoft.Extensions.Caching.Memory;
using NzbWebDAV.Clients.Connections;
using NzbWebDAV.Config;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Streams;
using NzbWebDAV.Websocket;
using Serilog;
using Usenet.Nntp.Responses;
using Usenet.Nzb;

namespace NzbWebDAV.Clients;

public class UsenetStreamingClient
{
    private readonly INntpClient _client;
    private readonly WebsocketManager _websocketManager;
    private readonly ConfigManager _configManager;
    public UsenetStreamingClient(ConfigManager configManager, WebsocketManager websocketManager)
    {
        // initialize private members
        _websocketManager = websocketManager;
        _configManager = configManager;

        // get connection settings from config-manager
        var host = configManager.GetConfigValue("usenet.host") ?? string.Empty;
        var port = int.Parse(configManager.GetConfigValue("usenet.port") ?? "119");
        var useSsl = bool.Parse(configManager.GetConfigValue("usenet.use-ssl") ?? "false");
        var user = configManager.GetConfigValue("usenet.user") ?? string.Empty;
        var pass = configManager.GetConfigValue("usenet.pass") ?? string.Empty;
        var connections = configManager.GetMaxConnections();

        // initialize the nntp-client
        var createNewConnection = (CancellationToken ct) => CreateNewConnection(host, port, useSsl, user, pass, ct);
        var connectionPool = CreateNewConnectionPool(connections, createNewConnection);
        var multiConnectionClient = new MultiConnectionNntpClient(connectionPool);
        var cache = new MemoryCache(new MemoryCacheOptions() { SizeLimit = 8192 });
        _client = new CachingNntpClient(multiConnectionClient, cache);

        // when config changes, update the connection-pool
        configManager.OnConfigChanged += (_, configEventArgs) =>
        {
            // if unrelated config changed, do nothing
            if (!configEventArgs.ChangedConfig.ContainsKey("usenet.host") &&
                !configEventArgs.ChangedConfig.ContainsKey("usenet.port") &&
                !configEventArgs.ChangedConfig.ContainsKey("usenet.use-ssl") &&
                !configEventArgs.ChangedConfig.ContainsKey("usenet.user") &&
                !configEventArgs.ChangedConfig.ContainsKey("usenet.pass") &&
                !configEventArgs.ChangedConfig.ContainsKey("usenet.connections")) return;

            // update the connection-pool according to the new config
            var connectionCount = int.Parse(configEventArgs.NewConfig["usenet.connections"]);
            var newHost = configEventArgs.NewConfig["usenet.host"];
            var newPort = int.Parse(configEventArgs.NewConfig["usenet.port"]);
            var newUseSsl = bool.Parse(configEventArgs.NewConfig.GetValueOrDefault("usenet.use-ssl", "false"));
            var newUser = configEventArgs.NewConfig["usenet.user"];
            var newPass = configEventArgs.NewConfig["usenet.pass"];
            var newConnectionPool = CreateNewConnectionPool(connectionCount, cancellationToken =>
                CreateNewConnection(newHost, newPort, newUseSsl, newUser, newPass, cancellationToken));
            multiConnectionClient.UpdateConnectionPool(newConnectionPool);
        };
    }

    public async Task<bool> CheckNzbFileHealth(string[] segmentIds, int samplePct, int healthyThresholdPct, CancellationToken cancellationToken = default)
    {
        // ensure segmentIds has Count > 0 and thresholdPercentage is between 80 and 100
        if (segmentIds.Length == 0) throw new ArgumentException("segmentIds must have Count > 0");
        if (healthyThresholdPct < 80 || healthyThresholdPct > 100) throw new ArgumentException("healthyThresholdPct must be between 80 and 100");
        if (samplePct < 1 || samplePct > 100) throw new ArgumentException("samplePct must be between 1 and 100");

        var reservedConnections = _configManager.GetMaxConnections() - _configManager.GetMaxQueueConnections();
        using var _ = cancellationToken.SetScopedContext(new ReservedConnectionsContext(reservedConnections));

        var sampledSegmentIds = samplePct == 100 ? segmentIds :
            segmentIds
                .OrderBy(x => Random.Shared.Next())
                .Take((int)Math.Ceiling(segmentIds.Length * samplePct / 100.0d));

        using var childCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var missingSegments = 0;
        var missingSegmentsThreshold = Math.Ceiling(sampledSegmentIds.Count() * (100 - healthyThresholdPct) / 100.0d);

        try
        {
            var tasks = sampledSegmentIds
                .Select(segmentId => _client.StatAsync(segmentId, childCts.Token))
                .WithConcurrencyAsync(_configManager.GetMaxQueueConnections());

            await foreach (var response in tasks.WithCancellation(childCts.Token))
            {
                if (response.ResponseType != NntpStatResponseType.ArticleExists)
                {
                    missingSegments++;

                    if (missingSegments >= missingSegmentsThreshold)
                    {
                        childCts.Cancel();
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when we cancel due to threshold being reached or parent cancellation
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking NZB file health");
            return false;
        }

        return missingSegments < missingSegmentsThreshold;
    }

    public async Task<NzbFileStream> GetFileStream(NzbFile nzbFile, int concurrentConnections, CancellationToken ct)
    {
        var segmentIds = nzbFile.Segments.Select(x => x.MessageId.Value).ToArray();
        var fileSize = await _client.GetFileSizeAsync(nzbFile, cancellationToken: ct);
        return new NzbFileStream(segmentIds, fileSize, _client, concurrentConnections);
    }

    public NzbFileStream GetFileStream(string[] segmentIds, long fileSize, int concurrentConnections)
    {
        return new NzbFileStream(segmentIds, fileSize, _client, concurrentConnections);
    }

    public Task<YencHeaderStream> GetSegmentStreamAsync(string segmentId, CancellationToken cancellationToken)
    {
        return _client.GetSegmentStreamAsync(segmentId, cancellationToken);
    }

    public Task<long> GetFileSizeAsync(NzbFile file, CancellationToken cancellationToken)
    {
        return _client.GetFileSizeAsync(file, cancellationToken);
    }

    private ConnectionPool<INntpClient> CreateNewConnectionPool
    (
        int maxConnections,
        Func<CancellationToken, ValueTask<INntpClient>> connectionFactory
    )
    {
        var connectionPool = new ConnectionPool<INntpClient>(maxConnections, connectionFactory);
        connectionPool.OnConnectionPoolChanged += OnConnectionPoolChanged;
        var args = new ConnectionPool<INntpClient>.ConnectionPoolChangedEventArgs(0, 0, maxConnections);
        OnConnectionPoolChanged(connectionPool, args);
        return connectionPool;
    }

    private void OnConnectionPoolChanged(object? _, ConnectionPool<INntpClient>.ConnectionPoolChangedEventArgs args)
    {
        var message = $"{args.Live}|{args.Max}|{args.Idle}";
        _websocketManager.SendMessage(WebsocketTopic.UsenetConnections, message);
    }

    public static async ValueTask<INntpClient> CreateNewConnection
    (
        string host,
        int port,
        bool useSsl,
        string user,
        string pass,
        CancellationToken cancellationToken
    )
    {
        var connection = new ThreadSafeNntpClient();
        if (!await connection.ConnectAsync(host, port, useSsl, cancellationToken))
            throw new CouldNotConnectToUsenetException("Could not connect to usenet host. Check connection settings.");
        if (!await connection.AuthenticateAsync(user, pass, cancellationToken))
            throw new CouldNotLoginToUsenetException("Could not login to usenet host. Check username and password.");
        return connection;
    }
}