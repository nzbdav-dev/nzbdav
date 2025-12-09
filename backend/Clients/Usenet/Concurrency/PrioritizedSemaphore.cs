using UsenetSharp.Concurrency;

namespace NzbWebDAV.Clients.Usenet.Concurrency;

public class PrioritizedSemaphore : IDisposable
{
    private readonly LinkedList<TaskCompletionSource<bool>> _highPriorityWaiters = [];
    private readonly LinkedList<TaskCompletionSource<bool>> _lowPriorityWaiters = [];
    private readonly SemaphorePriorityOdds _priorityOdds;
    private int _currentCount;
    private bool _disposed = false;
    private readonly Lock _lock = new();
    private readonly Random _random = new();

    public PrioritizedSemaphore(int initialCount, SemaphorePriorityOdds? priorityOdds = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCount);
        _priorityOdds = priorityOdds ?? new SemaphorePriorityOdds { HighPriorityOdds = 1 };
        _currentCount = initialCount;
    }

    public Task WaitAsync(SemaphorePriority priority, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AsyncSemaphore));

            if (_currentCount > 0)
            {
                _currentCount--;
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var queue = priority == SemaphorePriority.High ? _highPriorityWaiters : _lowPriorityWaiters;
            var node = queue.AddLast(tcs);

            if (cancellationToken.CanBeCanceled)
            {
                var registration = cancellationToken.Register(() =>
                {
                    var removed = false;
                    lock (_lock)
                    {
                        try
                        {
                            queue.Remove(node);
                            removed = true;
                        }
                        catch (InvalidOperationException)
                        {
                            // intentionally left blank
                        }
                    }

                    if (removed)
                        tcs.TrySetCanceled(cancellationToken);
                });

                tcs.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
            }

            return tcs.Task;
        }
    }

    public void Release()
    {
        TaskCompletionSource<bool>? toRelease = null;
        lock (_lock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AsyncSemaphore));

            if (_highPriorityWaiters.Count == 0)
            {
                // if there are no high-priority waiters,
                // then release a low-priority waiter.
                toRelease = Release(_lowPriorityWaiters);
            }
            else if (_lowPriorityWaiters.Count == 0)
            {
                // if there are no low-priority waiters,
                // then release a high-priority waiter.
                toRelease = Release(_highPriorityWaiters);
            }
            else
            {
                // if there are both high-priority waiters and low-priority waiters,
                // then roll the dice to determine which to release, based on the given odds.
                var result = _random.NextDouble();
                var (one, two) = (_highPriorityWaiters, _lowPriorityWaiters);
                if (result > _priorityOdds.HighPriorityOdds)
                    (one, two) = (two, one);
                toRelease = Release(one);
                if (toRelease == null)
                    Release(two);
            }

            if (toRelease == null)
            {
                // if no waiters were ultimately released,
                // then increase the current count.
                _currentCount++;
                return;
            }
        }

        toRelease.TrySetResult(true);
    }

    private static TaskCompletionSource<bool>? Release(LinkedList<TaskCompletionSource<bool>> queue)
    {
        while (queue.Count > 0)
        {
            var node = queue.First!;
            queue.RemoveFirst();

            // Skip canceled tasks
            if (!node.Value.Task.IsCanceled)
            {
                return node.Value;
            }
        }

        return null;
    }

    public void Dispose()
    {
        List<TaskCompletionSource<bool>> waitersToCancel;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            waitersToCancel = _highPriorityWaiters.Concat(_lowPriorityWaiters).ToList();
            _highPriorityWaiters.Clear();
            _lowPriorityWaiters.Clear();
        }

        foreach (var tcs in waitersToCancel)
            tcs.TrySetException(new ObjectDisposedException(nameof(AsyncSemaphore)));
    }
}