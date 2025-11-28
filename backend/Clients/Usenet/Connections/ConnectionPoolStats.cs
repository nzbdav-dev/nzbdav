using NzbWebDAV.Config;
using NzbWebDAV.Models;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Clients.Usenet.Connections;

public class ConnectionPoolStats
{
    private readonly int[] _live;
    private readonly int[] _idle;
    private readonly int _max;
    private int _totalLive;
    private int _totalIdle;
    private readonly UsenetProviderConfig _providerConfig;
    private readonly WebsocketManager _websocketManager;
    private readonly ConnectionPool<INntpClient>[] _connectionPools;

    public ConnectionPoolStats(UsenetProviderConfig providerConfig, WebsocketManager websocketManager)
    {
        var count = providerConfig.Providers.Count;
        _live = new int[count];
        _idle = new int[count];
        _connectionPools = new ConnectionPool<INntpClient>[count];
        _max = providerConfig.Providers
            .Where(x => x.Type == ProviderType.Pooled)
            .Select(x => x.MaxConnections)
            .Sum();

        _providerConfig = providerConfig;
        _websocketManager = websocketManager;
    }

    public void RegisterConnectionPool(int providerIndex, ConnectionPool<INntpClient> connectionPool)
    {
        _connectionPools[providerIndex] = connectionPool;
    }

    public EventHandler<ConnectionPoolChangedEventArgs> GetOnConnectionPoolChanged(int providerIndex)
    {
        return OnEvent;

        void OnEvent(object? _, ConnectionPoolChangedEventArgs args)
        {
            if (_providerConfig.Providers[providerIndex].Type == ProviderType.Pooled)
            {
                lock (this)
                {
                    _live[providerIndex] = args.Live;
                    _idle[providerIndex] = args.Idle;
                    _totalLive = _live.Sum();
                    _totalIdle = _idle.Sum();
                }
            }

            // Get usage breakdown from all connection pools
            var usageBreakdown = GetGlobalUsageBreakdown();
            var message = $"{providerIndex}|{args.Live}|{args.Idle}|{_totalLive}|{_max}|{_totalIdle}|{usageBreakdown}";
            _websocketManager.SendMessage(WebsocketTopic.UsenetConnections, message);
        }
    }

    private string GetGlobalUsageBreakdown()
    {
        var allUsageCounts = new Dictionary<ConnectionUsageType, int>();

        foreach (var pool in _connectionPools)
        {
            if (pool == null) continue;

            var breakdown = pool.GetUsageBreakdown();
            foreach (var (usageType, count) in breakdown)
            {
                allUsageCounts.TryGetValue(usageType, out var currentCount);
                allUsageCounts[usageType] = currentCount + count;
            }
        }

        var parts = allUsageCounts
            .OrderBy(x => x.Key)
            .Select(kv => $"{kv.Key}={kv.Value}")
            .ToArray();

        return parts.Length > 0 ? string.Join(",", parts) : "none";
    }

    public sealed class ConnectionPoolChangedEventArgs(int live, int idle, int max) : EventArgs
    {
        public int Live { get; } = live;
        public int Idle { get; } = idle;
        public int Max { get; } = max;
        public int Active => Live - Idle;
    }
}