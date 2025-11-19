using Microsoft.Extensions.Logging;
using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Streams;
using Usenet.Exceptions;
using Usenet.Nntp.Responses;
using Usenet.Nzb;
using Usenet.Yenc;

namespace NzbWebDAV.Clients.Usenet;

/// <summary>
/// NNTP client that supports multiple servers with automatic failover
/// </summary>
public class MultiServerNntpClient : INntpClient
{
    private readonly List<ServerInstance> _servers = new();
    private readonly ServerHealthTracker _healthTracker;
    private readonly ILogger<MultiServerNntpClient>? _logger;
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    public MultiServerNntpClient(
        IEnumerable<UsenetServerConfig> serverConfigs,
        ServerHealthTracker healthTracker,
        ILogger<MultiServerNntpClient>? logger = null)
    {
        _healthTracker = healthTracker;
        _logger = logger;
        InitializeServers(serverConfigs);
    }

    private void InitializeServers(IEnumerable<UsenetServerConfig> serverConfigs)
    {
        var configs = serverConfigs
            .Where(s => s.Enabled)
            .OrderBy(s => s.Priority)
            .ThenBy(s => s.Name)
            .ToList();

        if (configs.Count == 0)
            throw new InvalidOperationException("At least one enabled Usenet server must be configured");

        foreach (var config in configs)
        {
            var serverInstance = CreateServerInstance(config);
            _servers.Add(serverInstance);
            _logger?.LogInformation(
                "Initialized server: {Name} ({Host}:{Port}) with {Connections} connections at priority {Priority}",
                config.Name, config.Host, config.Port, config.MaxConnections, config.Priority);
        }
    }

    private ServerInstance CreateServerInstance(UsenetServerConfig config)
    {
        var createConnection = (CancellationToken ct) =>
            UsenetStreamingClient.CreateNewConnection(
                config.Host, config.Port, config.UseSsl,
                config.Username, config.Password, ct);

        var connectionPool = new ConnectionPool<INntpClient>(config.MaxConnections, createConnection);
        var multiConnectionClient = new MultiConnectionNntpClient(connectionPool);

        return new ServerInstance
        {
            Config = config,
            Client = multiConnectionClient,
            ConnectionPool = connectionPool
        };
    }

    public Task<bool> ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Connection is handled per-server in MultiServerNntpClient");
    }

    public Task<bool> AuthenticateAsync(string user, string pass, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Authentication is handled per-server in MultiServerNntpClient");
    }

    public Task<NntpStatResponse> StatAsync(string segmentId, CancellationToken cancellationToken)
    {
        return ExecuteWithFailover(
            server => server.Client.StatAsync(segmentId, cancellationToken),
            segmentId,
            cancellationToken,
            isArticleNotFoundRetryable: false);
    }

    public Task<YencHeaderStream> GetSegmentStreamAsync(string segmentId, bool includeHeaders, CancellationToken cancellationToken)
    {
        return ExecuteWithFailover(
            server => server.Client.GetSegmentStreamAsync(segmentId, includeHeaders, cancellationToken),
            segmentId,
            cancellationToken,
            isArticleNotFoundRetryable: true);
    }

    public Task<YencHeader> GetSegmentYencHeaderAsync(string segmentId, CancellationToken cancellationToken)
    {
        return ExecuteWithFailover(
            server => server.Client.GetSegmentYencHeaderAsync(segmentId, cancellationToken),
            segmentId,
            cancellationToken,
            isArticleNotFoundRetryable: true);
    }

    public Task<long> GetFileSizeAsync(NzbFile file, CancellationToken cancellationToken)
    {
        return ExecuteWithFailover(
            server => server.Client.GetFileSizeAsync(file, cancellationToken),
            $"file:{file.FileName}",
            cancellationToken,
            isArticleNotFoundRetryable: true);
    }

    public Task<UsenetArticleHeaders> GetArticleHeadersAsync(string segmentId, CancellationToken cancellationToken)
    {
        return ExecuteWithFailover(
            server => server.Client.GetArticleHeadersAsync(segmentId, cancellationToken),
            segmentId,
            cancellationToken,
            isArticleNotFoundRetryable: true);
    }

    public Task<NntpDateResponse> DateAsync(CancellationToken cancellationToken)
    {
        // Use primary server for date queries
        return _servers.First().Client.DateAsync(cancellationToken);
    }

    public async Task WaitForReady(CancellationToken cancellationToken)
    {
        // Wait for all servers to be ready
        var tasks = _servers.Select(s => s.Client.WaitForReady(cancellationToken));
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Execute an operation with automatic failover to backup servers
    /// </summary>
    private async Task<T> ExecuteWithFailover<T>(
        Func<ServerInstance, Task<T>> operation,
        string resourceId,
        CancellationToken cancellationToken,
        bool isArticleNotFoundRetryable)
    {
        var exceptions = new List<Exception>();
        var availableServers = _servers.Where(s => _healthTracker.IsServerAvailable(s.Config.Id)).ToList();

        if (availableServers.Count == 0)
        {
            _logger?.LogWarning("All servers are unavailable due to circuit breaker. Attempting all servers anyway.");
            availableServers = _servers.ToList();
        }

        foreach (var server in availableServers)
        {
            try
            {
                _logger?.LogDebug("Attempting operation on server: {ServerName}", server.Config.Name);
                var result = await operation(server);
                _healthTracker.RecordSuccess(server.Config.Id);

                if (exceptions.Count > 0)
                {
                    _logger?.LogInformation(
                        "Successfully retrieved {ResourceId} from fallback server {ServerName} after {FailureCount} failures",
                        resourceId, server.Config.Name, exceptions.Count);
                }

                return result;
            }
            catch (UsenetArticleNotFoundException ex)
            {
                _logger?.LogDebug(
                    "Article {ResourceId} not found on server {ServerName}",
                    resourceId, server.Config.Name);

                exceptions.Add(ex);

                // If this is a STAT operation or article explicitly not found, don't retry on other servers
                if (!isArticleNotFoundRetryable)
                {
                    _logger?.LogDebug("Article not found is not retryable for this operation");
                    throw;
                }

                // Try next server
                continue;
            }
            catch (CouldNotConnectToUsenetException ex)
            {
                _logger?.LogWarning(
                    "Could not connect to server {ServerName}: {Message}",
                    server.Config.Name, ex.Message);

                _healthTracker.RecordFailure(server.Config.Id, ex);
                exceptions.Add(ex);
                // Try next server
                continue;
            }
            catch (CouldNotLoginToUsenetException ex)
            {
                _logger?.LogWarning(
                    "Could not authenticate to server {ServerName}: {Message}",
                    server.Config.Name, ex.Message);

                _healthTracker.RecordFailure(server.Config.Id, ex);
                exceptions.Add(ex);
                // Try next server
                continue;
            }
            catch (NntpException ex)
            {
                _logger?.LogWarning(
                    "NNTP error on server {ServerName}: {Message}",
                    server.Config.Name, ex.Message);

                _healthTracker.RecordFailure(server.Config.Id, ex);
                exceptions.Add(ex);
                // Try next server
                continue;
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    ex,
                    "Unexpected error on server {ServerName}",
                    server.Config.Name);

                exceptions.Add(ex);
                // Try next server
                continue;
            }
        }

        // All servers failed
        if (exceptions.Count > 0)
        {
            var firstException = exceptions[0];

            _logger?.LogError(
                "All {ServerCount} servers failed for resource {ResourceId}. First error: {Error}",
                availableServers.Count, resourceId, firstException.Message);

            // Throw the first exception we encountered
            throw firstException;
        }

        throw new InvalidOperationException("No servers available to execute operation");
    }

    /// <summary>
    /// Update the server configurations and connection pools
    /// </summary>
    public async Task UpdateServersAsync(IEnumerable<UsenetServerConfig> newServerConfigs)
    {
        await _updateLock.WaitAsync();
        try
        {
            var oldServers = _servers.ToList();
            _servers.Clear();

            InitializeServers(newServerConfigs);

            // Dispose old connection pools
            foreach (var oldServer in oldServers)
            {
                oldServer.Client.Dispose();
            }

            _logger?.LogInformation("Updated server configurations. Now have {Count} servers", _servers.Count);
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <summary>
    /// Get current server configurations
    /// </summary>
    public IReadOnlyList<UsenetServerConfig> GetServerConfigs()
    {
        return _servers.Select(s => s.Config).ToList();
    }

    /// <summary>
    /// Get health statistics for all servers
    /// </summary>
    public List<ServerHealthStats> GetServerHealthStats()
    {
        return _servers.Select(s => _healthTracker.GetHealthStats(s.Config.Id)).ToList();
    }

    public void Dispose()
    {
        foreach (var server in _servers)
        {
            server.Client.Dispose();
        }
        _servers.Clear();
        _updateLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private class ServerInstance
    {
        public required UsenetServerConfig Config { get; init; }
        public required MultiConnectionNntpClient Client { get; init; }
        public required ConnectionPool<INntpClient> ConnectionPool { get; init; }
    }
}
