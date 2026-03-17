using System.Collections.Concurrent;

namespace NzbWebDAV.Clients.Usenet.Connections;

/// <summary>
/// Tracks consecutive connection failures per provider and temporarily
/// "opens" the circuit (skips the provider) after a threshold is reached.
/// Resets automatically after a cooldown period.
/// </summary>
public class ProviderCircuitBreaker
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _cooldown;
    private readonly ConcurrentDictionary<int, ProviderState> _states = new();

    public ProviderCircuitBreaker(int failureThreshold = 3, TimeSpan? cooldown = null)
    {
        _failureThreshold = failureThreshold;
        _cooldown = cooldown ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Returns true if the provider should be skipped (circuit is open).
    /// </summary>
    public bool IsOpen(int providerIndex)
    {
        if (!_states.TryGetValue(providerIndex, out var state))
            return false;

        if (state.ConsecutiveFailures < _failureThreshold)
            return false;

        // If cooldown has elapsed, allow a probe attempt
        if (Environment.TickCount64 - state.LastFailureTickMs >= _cooldown.TotalMilliseconds)
            return false;

        return true;
    }

    public void RecordFailure(int providerIndex)
    {
        _states.AddOrUpdate(
            providerIndex,
            _ => new ProviderState { ConsecutiveFailures = 1, LastFailureTickMs = Environment.TickCount64 },
            (_, existing) =>
            {
                existing.ConsecutiveFailures++;
                existing.LastFailureTickMs = Environment.TickCount64;
                return existing;
            });
    }

    public void RecordSuccess(int providerIndex)
    {
        _states.TryRemove(providerIndex, out _);
    }

    private class ProviderState
    {
        public int ConsecutiveFailures;
        public long LastFailureTickMs;
    }
}
