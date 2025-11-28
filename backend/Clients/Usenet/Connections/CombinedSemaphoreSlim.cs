namespace NzbWebDAV.Clients.Usenet.Connections
{
    public sealed class CombinedSemaphoreSlim(int maxCount, ExtendedSemaphoreSlim pooledSemaphore): IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new(maxCount, maxCount);

        public int RemainingSemaphoreSlots => _semaphore.CurrentCount;

        public async Task WaitAsync(int requiredAvailable, CancellationToken cancellationToken)
        {
            // IMPORTANT: Acquire the global pooled semaphore FIRST, then the local semaphore.
            // This prevents deadlock scenarios where the queue holds local semaphores while
            // waiting for the global semaphore with reserved connection requirements.
            Serilog.Log.Debug($"[CombinedSemaphore] Waiting: LocalRemaining={_semaphore.CurrentCount}, RequiredAvailable={requiredAvailable}");
            await pooledSemaphore.WaitAsync(requiredAvailable, cancellationToken);
            try
            {
                await _semaphore.WaitAsync(cancellationToken);
                Serilog.Log.Debug($"[CombinedSemaphore] Acquired: LocalRemaining={_semaphore.CurrentCount}");
            }
            catch (Exception)
            {
                pooledSemaphore.Release();
                throw;
            }
        }

        public void Release()
        {
            // Release in reverse order of acquisition: local first, then global
            _semaphore.Release();
            pooledSemaphore.Release();
            Serilog.Log.Debug($"[CombinedSemaphore] Released: LocalRemaining={_semaphore.CurrentCount}");
        }

        public void Dispose()
        {
            _semaphore.Dispose();
        }
    }
}