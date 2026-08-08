using Serilog;

namespace NzbWebDAV.Clients.Usenet.Connections;

/// <summary>
/// Circuit breaker for an NNTP provider.
///
/// Generic failures count as 1 point.
/// NNTP read timeouts count as 2 points.
///
/// 3 points open the circuit.
///
/// Initial cooldown is 60 seconds. A failed HALF-OPEN recovery probe
/// doubles the cooldown, capped at 5 minutes.
///
/// Failures from requests which were already in-flight when the circuit
/// opened DO NOT extend the cooldown.
/// </summary>
public class ProviderCircuitBreaker
{
    private const int FailureThreshold = 3;

    private static readonly TimeSpan InitialCooldown =
        TimeSpan.FromSeconds(60);

    private static readonly TimeSpan MaxCooldown =
        TimeSpan.FromMinutes(5);

    private readonly string _providerName;
    private readonly object _lock = new();

    private int _failurePoints;

    // 0 means circuit is closed.
    private long _trippedUntilMs;

    private TimeSpan _currentCooldown = InitialCooldown;

    // Exactly one request may probe after cooldown.
    private bool _probeInProgress;

    public ProviderCircuitBreaker(string providerName)
    {
        _providerName = providerName;
    }

    public bool IsTripped
    {
        get
        {
            lock (_lock)
            {
                if (_trippedUntilMs == 0)
                    return false;

                return Environment.TickCount64 < _trippedUntilMs;
            }
        }
    }

    /// <summary>
    /// Determines whether this caller may use the provider.
    ///
    /// CLOSED:
    ///     everyone allowed.
    ///
    /// OPEN:
    ///     nobody allowed.
    ///
    /// HALF-OPEN:
    ///     exactly one recovery probe allowed.
    /// </summary>
    public bool TryEnter()
    {
        lock (_lock)
        {
            // Healthy / closed.
            if (_trippedUntilMs == 0)
                return true;

            var now = Environment.TickCount64;

            // Still cooling down.
            if (now < _trippedUntilMs)
                return false;

            // Cooldown expired.
            // Allow exactly one half-open probe.
            if (_probeInProgress)
                return false;

            _probeInProgress = true;

            Log.Information(
                "[PROVIDER_PROBE] provider={Provider} action=half-open-probe",
                _providerName);

            return true;
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            var wasDegraded =
                _failurePoints > 0 ||
                _trippedUntilMs != 0 ||
                _probeInProgress;

            _failurePoints = 0;
            _trippedUntilMs = 0;
            _probeInProgress = false;
            _currentCooldown = InitialCooldown;

            if (wasDegraded)
            {
                Log.Information(
                    "[PROVIDER_RECOVERED] provider={Provider} action=circuit-reset",
                    _providerName);
            }
        }
    }

    public void RecordFailure()
    {
        RecordFailurePoints(1, "failure");
    }

    public void RecordTimeout()
    {
        // Streaming timeout is more serious than an ordinary command error.
        RecordFailurePoints(2, "nntp-timeout");
    }

    private void RecordFailurePoints(int points, string reason)
    {
        lock (_lock)
        {
            var now = Environment.TickCount64;

            /*
             * CIRCUIT ALREADY OPEN
             *
             * Ignore errors from requests which were already executing when
             * the circuit opened. Otherwise 10-20 simultaneous streams can
             * turn a 60-second trip into 5 minutes instantly.
             */
            if (_trippedUntilMs != 0 &&
                now < _trippedUntilMs &&
                !_probeInProgress)
            {
                Log.Debug(
                    "[PROVIDER_FAILURE_IGNORED] provider={Provider} " +
                    "reason={Reason} circuit=already-open",
                    _providerName,
                    reason);

                return;
            }

            /*
             * HALF-OPEN PROBE FAILED
             *
             * This is the ONLY situation where cooldown escalates.
             */
            if (_probeInProgress)
            {
                _probeInProgress = false;

                var cooldown = TimeSpan.FromMilliseconds(
                    Math.Min(
                        _currentCooldown.TotalMilliseconds * 2,
                        MaxCooldown.TotalMilliseconds));

                _currentCooldown = cooldown;
                _trippedUntilMs =
                    now + (long)cooldown.TotalMilliseconds;

                Log.Warning(
                    "[PROVIDER_CIRCUIT_REOPEN] provider={Provider} " +
                    "reason={Reason} cooldownSeconds={CooldownSeconds}",
                    _providerName,
                    reason,
                    cooldown.TotalSeconds);

                return;
            }

            /*
             * CLOSED CIRCUIT
             */
            _failurePoints += points;

            Log.Warning(
                "[PROVIDER_FAILURE] provider={Provider} reason={Reason} " +
                "failurePoints={FailurePoints}/{Threshold}",
                _providerName,
                reason,
                _failurePoints,
                FailureThreshold);

            if (_failurePoints < FailureThreshold)
                return;

            /*
             * FIRST TRIP
             *
             * Always start at 60 seconds.
             */
            _failurePoints = 0;
            _probeInProgress = false;
            _currentCooldown = InitialCooldown;

            _trippedUntilMs =
                now + (long)InitialCooldown.TotalMilliseconds;

            Log.Warning(
                "[PROVIDER_CIRCUIT_OPEN] provider={Provider} " +
                "reason={Reason} cooldownSeconds={CooldownSeconds}",
                _providerName,
                reason,
                InitialCooldown.TotalSeconds);
        }
    }
}
