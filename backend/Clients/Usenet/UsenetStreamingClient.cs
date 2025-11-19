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
    private readonly INntpClient _client;
    private readonly WebsocketManager _websocketManager;
    private readonly ServerHealthTracker _healthTracker;
    private readonly ConfigManager _configManager;
    private MultiServerNntpClient? _multiServerClient;

    public UsenetStreamingClient(
        ConfigManager configManager,
        WebsocketManager websocketManager,
        ServerHealthTracker healthTracker)
    {
        // initialize private members
        _websocketManager = websocketManager;
        _healthTracker = healthTracker;
        _configManager = configManager;

        // get server configurations
        var serverConfigs = configManager.GetUsenetServers();

        // initialize the multi-server client
        _multiServerClient = new MultiServerNntpClient(serverConfigs, _healthTracker);

        // wrap with caching
        var cache = new MemoryCache(new MemoryCacheOptions() { SizeLimit = 8192 });
        _client = new CachingNntpClient(_multiServerClient, cache);

        // setup connection pool monitoring for all servers
        SetupConnectionPoolMonitoring(serverConfigs);

        // when config changes, update the servers
        configManager.OnConfigChanged += async (_, configEventArgs) =>
        {
            // if unrelated config changed, do nothing
            if (!configManager.HasUsenetConfigChanged(configEventArgs.ChangedConfig))
                return;

            // update server configurations
            var newServerConfigs = configManager.GetUsenetServers();
            if (_multiServerClient != null)
            {
                await _multiServerClient.UpdateServersAsync(newServerConfigs);
                SetupConnectionPoolMonitoring(newServerConfigs);
            }
        };
    }

    private void SetupConnectionPoolMonitoring(List<UsenetServerConfig> serverConfigs)
    {
        // Calculate total connections across all servers
        var totalConnections = serverConfigs.Sum(s => s.MaxConnections);

        // Send initial websocket update
        var message = $"0|{totalConnections}|0";
        _websocketManager.SendMessage(WebsocketTopic.UsenetConnections, message);

        // Note: Individual connection pool monitoring is handled by MultiServerNntpClient
        // This provides an aggregate view across all servers
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
        using var _ = childCt.Token.SetScopedContext(cancellationToken.GetContext<object>());
        var token = childCt.Token;

        var tasks = segmentIds
            .Select(async x => await CheckSegmentWithRetryAsync(x, token))
            .WithConcurrencyAsync(concurrency);

        var processed = 0;
        await foreach (var task in tasks)
        {
            progress?.Report(++processed);
            if (task.IsSuccess) continue;
            await childCt.CancelAsync();
            throw new UsenetArticleNotFoundException(task.SegmentId);
        }
    }

    private async Task<(string SegmentId, bool IsSuccess)> CheckSegmentWithRetryAsync(
        string segmentId,
        CancellationToken cancellationToken)
    {
        const int maxRetries = 3;
        var retryDelays = new[] { TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500) };

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var result = await _client.StatAsync(segmentId, cancellationToken);

                // Only these response codes indicate the article is actually missing
                if (result.ResponseType == NntpStatResponseType.NoArticleWithThatNumber ||
                    result.ResponseType == NntpStatResponseType.NoArticleWithThatMessageId)
                {
                    return (segmentId, false);
                }

                // Article exists
                if (result.ResponseType == NntpStatResponseType.ArticleExists)
                {
                    return (segmentId, true);
                }

                // Protocol/state errors (412, 420) - retry if we have attempts left
                if (result.ResponseType == NntpStatResponseType.NoGroupSelected ||
                    result.ResponseType == NntpStatResponseType.CurrentArticleInvalid)
                {
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(retryDelays[attempt], cancellationToken);
                        continue;
                    }
                    // Treat as success after retries to avoid false positives
                    return (segmentId, true);
                }

                // Unknown response - log and retry
                if (attempt < maxRetries)
                {
                    await Task.Delay(retryDelays[attempt], cancellationToken);
                    continue;
                }

                // After all retries, assume success to avoid false positives
                return (segmentId, true);
            }
            catch (Exception) when (attempt < maxRetries)
            {
                // Retry on transient network errors
                await Task.Delay(retryDelays[attempt], cancellationToken);
            }
        }

        // After all retries exhausted with exceptions, assume success to avoid false positives
        return (segmentId, true);
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

    public Task<YencHeaderStream> GetSegmentStreamAsync(string segmentId, bool includeHeaders, CancellationToken ct)
    {
        return _client.GetSegmentStreamAsync(segmentId, includeHeaders, ct);
    }

    public Task<long> GetFileSizeAsync(NzbFile file, CancellationToken cancellationToken)
    {
        return _client.GetFileSizeAsync(file, cancellationToken);
    }

    public Task<UsenetArticleHeaders> GetArticleHeadersAsync(string segmentId, CancellationToken cancellationToken)
    {
        return _client.GetArticleHeadersAsync(segmentId, cancellationToken);
    }

    /// <summary>
    /// Get health statistics for all configured servers
    /// </summary>
    public List<ServerHealthStats> GetServerHealthStats()
    {
        return _multiServerClient?.GetServerHealthStats() ?? new List<ServerHealthStats>();
    }

    /// <summary>
    /// Get current server configurations
    /// </summary>
    public IReadOnlyList<UsenetServerConfig> GetServerConfigs()
    {
        return _multiServerClient?.GetServerConfigs() ?? Array.Empty<UsenetServerConfig>();
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