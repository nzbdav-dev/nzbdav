using Microsoft.Extensions.Caching.Memory;
using NzbWebDAV.Clients.Connections;
using NzbWebDAV.Config;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Streams;
using NzbWebDAV.Websocket;
using Usenet.Nntp.Responses;
using Usenet.Nzb;

namespace NzbWebDAV.Clients;

public class UsenetStreamingClient
{
    private readonly INntpClient _client;
    private readonly WebsocketManager _websocketManager;

    public UsenetStreamingClient(ConfigManager configManager, WebsocketManager websocketManager)
    {
        // initialize private members
        _websocketManager = websocketManager;

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

    public async Task<bool> CheckNzbFileHealth(NzbFile nzbFile, CancellationToken cancellationToken = default)
    {
        var childCt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = nzbFile.Segments
            .Select(x => x.MessageId.Value)
            .Select(x => _client.StatAsync(x, childCt.Token))
            .ToHashSet();

        while (tasks.Count > 0)
        {
            var completedTask = await Task.WhenAny(tasks);
            tasks.Remove(completedTask);

            var completedResult = await completedTask;
            if (completedResult.ResponseType != NntpStatResponseType.ArticleExists)
            {
                await childCt.CancelAsync();
                return false;
            }
        }

        return true;
    }

    public async Task<NzbFileStream> GetFileStream(NzbFile nzbFile, int concurrentConnections, CancellationToken ct)
    {
        var segmentIds = nzbFile.GetSegmentIds();
        var fileSize = await _client.GetFileSizeAsync(nzbFile, cancellationToken: ct);
        return new NzbFileStream(segmentIds, fileSize, _client, concurrentConnections);
    }

    public NzbFileStream GetFileStream(NzbFile nzbFile, long fileSize, int concurrentConnections)
    {
        return new NzbFileStream(nzbFile.GetSegmentIds(), fileSize, _client, concurrentConnections);
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