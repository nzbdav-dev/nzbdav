using System.Collections.Concurrent;

namespace NzbWebDAV.Clients.Usenet;

/// <summary>
/// Tracks health status of Usenet servers using circuit breaker pattern
/// </summary>
public class ServerHealthTracker
{
    private readonly ConcurrentDictionary<string, ServerHealth> _serverHealth = new();
    private readonly TimeSpan _circuitBreakerTimeout = TimeSpan.FromMinutes(2);
    private readonly int _failureThreshold = 5;

    /// <summary>
    /// Check if a server is available for use
    /// </summary>
    public bool IsServerAvailable(string serverId)
    {
        if (!_serverHealth.TryGetValue(serverId, out var health))
            return true; // Unknown servers are assumed healthy

        // If circuit is open, check if timeout has elapsed
        if (health.IsCircuitOpen)
        {
            if (DateTime.UtcNow - health.LastFailureTime > _circuitBreakerTimeout)
            {
                // Reset to half-open state
                health.IsCircuitOpen = false;
                health.ConsecutiveFailures = 0;
                return true;
            }
            return false; // Circuit still open
        }

        return true;
    }

    /// <summary>
    /// Record a successful operation on a server
    /// </summary>
    public void RecordSuccess(string serverId)
    {
        var health = _serverHealth.GetOrAdd(serverId, _ => new ServerHealth());
        health.ConsecutiveFailures = 0;
        health.TotalSuccesses++;
        health.LastSuccessTime = DateTime.UtcNow;
        health.IsCircuitOpen = false;
    }

    /// <summary>
    /// Record a failed operation on a server
    /// </summary>
    public void RecordFailure(string serverId, Exception exception)
    {
        var health = _serverHealth.GetOrAdd(serverId, _ => new ServerHealth());
        health.ConsecutiveFailures++;
        health.TotalFailures++;
        health.LastFailureTime = DateTime.UtcNow;
        health.LastException = exception;

        // Open circuit if threshold exceeded
        if (health.ConsecutiveFailures >= _failureThreshold)
        {
            health.IsCircuitOpen = true;
        }
    }

    /// <summary>
    /// Get health statistics for a server
    /// </summary>
    public ServerHealthStats GetHealthStats(string serverId)
    {
        if (!_serverHealth.TryGetValue(serverId, out var health))
        {
            return new ServerHealthStats
            {
                ServerId = serverId,
                IsAvailable = true,
                ConsecutiveFailures = 0,
                TotalSuccesses = 0,
                TotalFailures = 0
            };
        }

        return new ServerHealthStats
        {
            ServerId = serverId,
            IsAvailable = !health.IsCircuitOpen,
            ConsecutiveFailures = health.ConsecutiveFailures,
            TotalSuccesses = health.TotalSuccesses,
            TotalFailures = health.TotalFailures,
            LastSuccessTime = health.LastSuccessTime,
            LastFailureTime = health.LastFailureTime,
            LastException = health.LastException?.Message
        };
    }

    /// <summary>
    /// Get health statistics for all tracked servers
    /// </summary>
    public List<ServerHealthStats> GetAllHealthStats()
    {
        return _serverHealth.Select(kvp => GetHealthStats(kvp.Key)).ToList();
    }

    /// <summary>
    /// Reset health tracking for a server
    /// </summary>
    public void ResetServerHealth(string serverId)
    {
        _serverHealth.TryRemove(serverId, out _);
    }

    /// <summary>
    /// Reset health tracking for all servers
    /// </summary>
    public void ResetAllServerHealth()
    {
        _serverHealth.Clear();
    }

    private class ServerHealth
    {
        public int ConsecutiveFailures { get; set; }
        public int TotalSuccesses { get; set; }
        public int TotalFailures { get; set; }
        public DateTime? LastSuccessTime { get; set; }
        public DateTime? LastFailureTime { get; set; }
        public Exception? LastException { get; set; }
        public bool IsCircuitOpen { get; set; }
    }
}

/// <summary>
/// Health statistics for a server
/// </summary>
public class ServerHealthStats
{
    public string ServerId { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int TotalSuccesses { get; set; }
    public int TotalFailures { get; set; }
    public DateTime? LastSuccessTime { get; set; }
    public DateTime? LastFailureTime { get; set; }
    public string? LastException { get; set; }
}
