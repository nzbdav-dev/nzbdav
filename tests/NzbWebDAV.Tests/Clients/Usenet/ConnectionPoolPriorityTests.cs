using NzbWebDAV.Clients.Usenet.Concurrency;
using NzbWebDAV.Clients.Usenet.Connections;

namespace NzbWebDAV.Tests.Clients.Usenet;

/// <summary>
/// Streaming Priority is applied at each provider's connection gate. These tests drive the
/// gate directly with controlled connections: admission is observed by disposing the lock that
/// currently holds the single permit, so no test depends on timing.
/// </summary>
public class ConnectionPoolPriorityTests
{
    private static readonly TimeSpan WaitBudget = TimeSpan.FromSeconds(10);

    private static ConnectionPool<object> CreatePool(int maxConnections, int? highPriorityOdds = null) =>
        new(
            maxConnections,
            _ => ValueTask.FromResult(new object()),
            TimeSpan.FromMinutes(5),
            highPriorityOdds is null
                ? null
                : new SemaphorePriorityOdds { HighPriorityOdds = highPriorityOdds.Value });

    [Fact]
    public async Task IdleConnections_AreNotReservedForHighPriority()
    {
        // Priority must not hold capacity back: with no High waiter, Low work (background
        // health STAT) is free to use every connection the provider allows.
        await using var pool = CreatePool(maxConnections: 4, highPriorityOdds: 95);

        var locks = new List<ConnectionLock<object>>();
        for (var i = 0; i < 4; i++)
        {
            var borrow = pool.GetConnectionLockAsync(SemaphorePriority.Low, CancellationToken.None);
            locks.Add(await borrow.WaitAsync(WaitBudget));
        }

        Assert.Equal(4, pool.ActiveConnections);
        Assert.Equal(0, pool.AvailableConnections);

        foreach (var borrowed in locks) borrowed.Dispose();
        Assert.Equal(4, pool.AvailableConnections);
    }

    [Fact]
    public async Task ConfiguredOdds_ArbitrateContendedAdmissions()
    {
        // 95/5 gives Low one admission in twenty, deterministically.
        var admissions = await AdmissionSequenceAsync(highPriorityOdds: 95, releases: 40);

        Assert.Equal(38, admissions.Count(p => p == SemaphorePriority.High));
        Assert.Equal(2, admissions.Count(p => p == SemaphorePriority.Low));
        Assert.Equal(SemaphorePriority.Low, admissions[19]);
        Assert.Equal(SemaphorePriority.Low, admissions[39]);
    }

    [Fact]
    public async Task HighPriorityOdds100_MakesLowWaitForHighToDrain()
    {
        var admissions = await AdmissionSequenceAsync(highPriorityOdds: 100, releases: 25);

        Assert.All(admissions, p => Assert.Equal(SemaphorePriority.High, p));
    }

    [Fact]
    public async Task HighPriorityOdds0_AdmitsLowWhileHighWaits()
    {
        var admissions = await AdmissionSequenceAsync(highPriorityOdds: 0, releases: 25);

        Assert.All(admissions, p => Assert.Equal(SemaphorePriority.Low, p));
    }

    [Fact]
    public async Task UpdatePriorityOdds_ChangesArbitration_WithoutReplacingConnections()
    {
        await using var pool = CreatePool(maxConnections: 1, highPriorityOdds: 100);
        using var waiters = new CancellationTokenSource();

        var held = await pool.GetConnectionLockAsync(SemaphorePriority.High, CancellationToken.None)
            .WaitAsync(WaitBudget);
        var high = pool.GetConnectionLockAsync(SemaphorePriority.High, waiters.Token);
        var low = pool.GetConnectionLockAsync(SemaphorePriority.Low, waiters.Token);

        held.Dispose();
        var firstAdmitted = await Task.WhenAny(high, low).WaitAsync(WaitBudget);
        Assert.Same(high, firstAdmitted);
        var highLock = await high;

        pool.UpdatePriorityOdds(new SemaphorePriorityOdds { HighPriorityOdds = 0 });
        var secondHigh = pool.GetConnectionLockAsync(SemaphorePriority.High, waiters.Token);

        highLock.Dispose();
        var secondAdmitted = await Task.WhenAny(secondHigh, low).WaitAsync(WaitBudget);
        Assert.Same(low, secondAdmitted);
        (await low).Dispose();

        // Re-arming the odds must not churn the pool: the same connection served every
        // admission above.
        var churn = pool.GetChurn();
        Assert.Equal(1, churn.ConnectionsOpened);
        Assert.Equal(0, churn.ConnectionsDestroyed);

        await waiters.CancelAsync();
        await DrainAsync([secondHigh]);
    }

    /// <summary>
    /// Saturates a one-connection pool, queues waiters in both lanes, then releases
    /// <paramref name="releases"/> times and reports which lane won each admission.
    /// </summary>
    private static async Task<List<SemaphorePriority>> AdmissionSequenceAsync(
        int highPriorityOdds,
        int releases,
        int waitersPerLane = 64)
    {
        await using var pool = CreatePool(maxConnections: 1, highPriorityOdds);
        using var waiters = new CancellationTokenSource();

        var current = await pool.GetConnectionLockAsync(SemaphorePriority.High, CancellationToken.None)
            .WaitAsync(WaitBudget);
        var highWaiters = new Queue<Task<ConnectionLock<object>>>();
        var lowWaiters = new Queue<Task<ConnectionLock<object>>>();
        for (var i = 0; i < waitersPerLane; i++)
        {
            // GetConnectionLockAsync enqueues on the gate synchronously, so both lanes are
            // populated in a known order before the first release.
            highWaiters.Enqueue(pool.GetConnectionLockAsync(SemaphorePriority.High, waiters.Token));
            lowWaiters.Enqueue(pool.GetConnectionLockAsync(SemaphorePriority.Low, waiters.Token));
        }

        var admissions = new List<SemaphorePriority>(releases);
        for (var i = 0; i < releases; i++)
        {
            // Only one permit exists, so exactly one queued waiter is admitted per release
            // and each lane hands out its head first.
            current.Dispose();
            var nextHigh = highWaiters.Peek();
            var nextLow = lowWaiters.Peek();
            var admitted = await Task.WhenAny(nextHigh, nextLow).WaitAsync(WaitBudget);
            if (admitted == nextHigh)
            {
                _ = highWaiters.Dequeue();
                admissions.Add(SemaphorePriority.High);
            }
            else
            {
                _ = lowWaiters.Dequeue();
                admissions.Add(SemaphorePriority.Low);
            }

            current = await admitted;
        }

        current.Dispose();
        await waiters.CancelAsync();
        await DrainAsync(highWaiters.Concat(lowWaiters));
        return admissions;
    }

    private static async Task DrainAsync(IEnumerable<Task<ConnectionLock<object>>> pending)
    {
        foreach (var waiter in pending)
        {
            try
            {
                (await waiter).Dispose();
            }
            catch (OperationCanceledException)
            {
                // expected: the test cancelled the waiters it no longer needs.
            }
            catch (ObjectDisposedException)
            {
                // expected: the pool may retire waiters when it is disposed.
            }
        }
    }
}
