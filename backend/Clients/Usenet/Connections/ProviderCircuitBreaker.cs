using Serilog;

namespace NzbWebDAV.Clients.Usenet.Connections;

/// <summary>
/// Tracks consecutive connection failures for an NNTP provider and temporarily
/// disables it when a failure threshold is reached, preventing a single
/// misbehaving provider from blocking the entire download pipeline.
/// <para>
/// After tripping, the provider enters a cooldown period during which it is
/// skipped. When the cooldown expires, a single probe attempt is allowed.
/// If the probe succeeds, the breaker resets. If it fails, the cooldown
/// doubles (up to a cap) and the breaker re-trips.
/// </para>
/// </summary>
public class ProviderCircuitBreaker
{
    private const int FailureThreshold = 3;
    private static readonly TimeSpan InitialCooldown = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromMinutes(5);

    private readonly string _providerName;
    private readonly object _lock = new();

    private int _consecutiveFailures;
    private long _trippedUntilMs;
    private TimeSpan _currentCooldown = InitialCooldown;
    private int _probeInFlight; // 0 == false, 1 == true

    public ProviderCircuitBreaker(string providerName)
    {
        _providerName = providerName;
    }

    public bool IsTripped
    {
        get
        {
            var trippedUntil = Volatile.Read(ref _trippedUntilMs);
            if (trippedUntil == 0) return false;
            return Environment.TickCount64 < trippedUntil;
        }
    }

    /// <summary>
    /// While tripped, at most one caller at a time should actually attempt a
    /// connection (a "probe"); everyone else should fail fast instead of also
    /// hitting the provider. Returns true if this caller won the right to probe
    /// (the caller must eventually call RecordSuccess or RecordFailure, both of
    /// which release this flag). Returns false if another probe is already in
    /// flight - the caller should fail immediately without attempting a connection.
    /// </summary>
    public bool TryEnterProbe()
    {
        return Interlocked.CompareExchange(ref _probeInFlight, 1, 0) == 0;
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            if (_consecutiveFailures > 0 || _trippedUntilMs > 0)
                Log.Information("Provider {Provider} recovered — circuit breaker reset.", _providerName);

            _consecutiveFailures = 0;
            _trippedUntilMs = 0;
            _currentCooldown = InitialCooldown;
            Volatile.Write(ref _probeInFlight, 0);
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            _consecutiveFailures++;
            Volatile.Write(ref _probeInFlight, 0);

            if (_consecutiveFailures < FailureThreshold) return;

            // Already tripped and cooling down - this failure came from the one
            // permitted probe attempt, not a fresh trip. Don't re-log or
            // re-double the cooldown for every concurrent caller that raced in
            // before the probe gate above existed.
            if (IsTripped) return;

            _trippedUntilMs = Environment.TickCount64 + (long)_currentCooldown.TotalMilliseconds;
            Log.Warning(
                "Provider {Provider} tripped after {Failures} consecutive failures. " +
                "Skipping for {Cooldown}s.",
                _providerName, _consecutiveFailures, _currentCooldown.TotalSeconds);

            _currentCooldown = TimeSpan.FromMilliseconds(
                Math.Min(_currentCooldown.TotalMilliseconds * 2, MaxCooldown.TotalMilliseconds));
        }
    }
}
