namespace NzbWebDAV.Clients.Connections
{
    /// <summary>
    /// A semaphore-like primitive where each waiter specifies the number of
    /// free slots that must remain available after acquisition.
    ///
    /// WaitAsync(requiredAvailable): acquires only if (CurrentCount >= requiredAvailable + 1).
    /// Release(): releases one slot.
    ///
    /// Higher requiredAvailable implies higher priority when multiple waiters exist.
    /// </summary>
    public sealed class ExtendedSemaphoreSlim
    {
        private readonly object _lock = new object();

        // Available slots (like SemaphoreSlim's CurrentCount)
        private int _currentCount;
        private readonly int _maxCount;

        // Waiters bucketed by requiredAvailable; keys sorted ascending, we serve descending.
        private readonly SortedDictionary<int, LinkedList<Waiter>> _queues = new();
        private int _waiterCount; // tracked atomically for fast-path guard

        public ExtendedSemaphoreSlim(int initialCount, int maxCount)
        {
            if (maxCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxCount));
            if (initialCount < 0 || initialCount > maxCount)
                throw new ArgumentOutOfRangeException(nameof(initialCount));

            _currentCount = initialCount;
            _maxCount = maxCount;
        }

        public ExtendedSemaphoreSlim(int initialCount) : this(initialCount, int.MaxValue)
        {
        }

        /// <summary>
        /// Current number of available slots.
        /// </summary>
        public int CurrentCount => Volatile.Read(ref _currentCount);

        /// <summary>
        /// Wait until there are at least (requiredAvailable + 1) slots free, then acquire 1 slot.
        /// After this completes successfully, at least requiredAvailable slots remain free.
        /// </summary>
        public Task WaitAsync(int requiredAvailable, CancellationToken cancellationToken = default)
        {
            if (requiredAvailable < 0) throw new ArgumentOutOfRangeException(nameof(requiredAvailable));
            if (requiredAvailable >= _maxCount)
                throw new ArgumentOutOfRangeException(nameof(requiredAvailable),
                    "requiredAvailable must be less than the semaphore's maximum count.");

            // Fast path: no waiters and enough capacity -> try to take one with a single CAS.
            if (Volatile.Read(ref _waiterCount) == 0 && TryAcquireFast(requiredAvailable))
                return Task.CompletedTask;

            // Slow path: enqueue and let the scheduler (by priority) grant slots.
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var waiter = new Waiter(requiredAvailable, tcs);

            List<Waiter>? toGrant;
            CancellationTokenRegistration ctr = default;

            lock (_lock)
            {
                EnqueueWaiter_NoLock(waiter);

                // Try to grant immediately in case capacity is there (or arrived just before we enqueued).
                toGrant = TryGrantWaiters_NoLock();
            }

            // Fire completions outside the lock.
            CompleteGranted(toGrant);

            if (tcs.Task.IsCompleted)
                return tcs.Task;

            if (cancellationToken.CanBeCanceled)
            {
                ctr = cancellationToken.Register(static state =>
                {
                    var (sem, w, tok) = ((ExtendedSemaphoreSlim, Waiter, CancellationToken))state!;
                    bool removed = sem.TryRemoveWaiter(w);
                    if (removed)
                    {
                        w.Tcs.TrySetCanceled(tok);
                    }
                }, (this, waiter, cancellationToken));

                // Dispose registration when the wait completes in any way.
                _ = tcs.Task.ContinueWith(static (_, s) => { ((CancellationTokenRegistration)s!).Dispose(); }, ctr,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            return tcs.Task;

            // ---------------- local helpers ----------------

            bool TryAcquireFast(int reqAvail)
            {
                while (true)
                {
                    int observed = Volatile.Read(ref _currentCount);
                    if (observed <= reqAvail) return false; // need at least reqAvail+1 to take one
                    int newVal = observed - 1;
                    if (Interlocked.CompareExchange(ref _currentCount, newVal, observed) == observed)
                        return true;
                }
            }
        }

        /// <summary>
        /// Release one slot back to the semaphore.
        /// </summary>
        public void Release()
        {
            int newCount = Interlocked.Increment(ref _currentCount);
            if (newCount > _maxCount)
            {
                Interlocked.Decrement(ref _currentCount);
                throw new SemaphoreFullException();
            }

            // If there are no waiters, we're done.
            if (Volatile.Read(ref _waiterCount) == 0)
                return;

            List<Waiter>? toGrant;
            lock (_lock)
            {
                toGrant = TryGrantWaiters_NoLock();
            }

            // Complete outside the lock to avoid running continuations under the lock.
            CompleteGranted(toGrant);
        }

        // ---------------- core scheduling ----------------

        private void EnqueueWaiter_NoLock(Waiter w)
        {
            if (!_queues.TryGetValue(w.Required, out var list))
            {
                list = new LinkedList<Waiter>();
                _queues.Add(w.Required, list);
            }

            w.Node = list.AddLast(w);
            Interlocked.Increment(ref _waiterCount);
        }

        private bool TryRemoveWaiter(Waiter w)
        {
            lock (_lock)
            {
                if (w.Signaled) return false; // already granted
                if (w.Node == null) return false; // not in a queue (already removed)
                if (!_queues.TryGetValue(w.Required, out var list)) return false;

                list.Remove(w.Node);
                w.Node = null;
                Interlocked.Decrement(ref _waiterCount);
                if (list.Count == 0) _queues.Remove(w.Required);
                return true;
            }
        }

        /// <summary>
        /// Grants as many queued waiters as possible by descending requiredAvailable.
        /// Must be called under _lock. Returns the waiters to complete outside the lock.
        /// </summary>
        private List<Waiter> TryGrantWaiters_NoLock()
        {
            if (Volatile.Read(ref _waiterCount) == 0)
                return s_empty;

            var granted = new List<Waiter>();
            List<int>? emptyKeys = null;

            // Iterate priorities descending; serve FIFO within each priority.
            foreach (var kvp in _queues.Reverse())
            {
                int req = kvp.Key;
                var list = kvp.Value;

                while (list.First is not null)
                {
                    int observed = Volatile.Read(ref _currentCount);
                    if (observed <= req)
                        break;

                    int newCount = Interlocked.Decrement(ref _currentCount);
                    if (newCount < 0)
                    {
                        Interlocked.Increment(ref _currentCount);
                        throw new InvalidOperationException("Semaphore count went negative.");
                    }

                    var node = list.First!;
                    list.RemoveFirst();

                    var w = node.Value;
                    w.Node = null;
                    w.Signaled = true;

                    Interlocked.Decrement(ref _waiterCount);
                    granted.Add(w);
                }

                if (list.Count == 0)
                    (emptyKeys ??= new List<int>()).Add(req);
            }

            if (emptyKeys is not null)
            {
                foreach (var k in emptyKeys)
                    _queues.Remove(k);
            }

            return granted;
        }

        private static void CompleteGranted(List<Waiter>? granted)
        {
            if (granted is null || granted.Count == 0) return;
            foreach (var w in granted)
                w.Tcs.TrySetResult(true);
        }

        // ---------------- inner types ----------------

        private sealed class Waiter
        {
            public readonly int Required;
            public readonly TaskCompletionSource<bool> Tcs;
            public LinkedListNode<Waiter>? Node;
            public bool Signaled;

            public Waiter(int required, TaskCompletionSource<bool> tcs)
            {
                Required = required;
                Tcs = tcs;
            }
        }

        private static readonly List<Waiter> s_empty = new List<Waiter>(0);
    }
}