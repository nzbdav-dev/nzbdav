namespace NzbWebDAV.Clients.Usenet.Connections
{
    public sealed class CombinedSemaphoreSlim(int maxCount, ExtendedSemaphoreSlim pooledSemaphore): IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new(maxCount, maxCount);

        public int RemainingSemaphoreSlots => _semaphore.CurrentCount;

        public async Task WaitAsync(int requiredAvailable, CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await pooledSemaphore.WaitAsync(requiredAvailable, cancellationToken);
            }
            catch (Exception)
            {
                _semaphore.Release();
                throw;
            }
        }

        public void Release()
        {
            _semaphore.Release();
            pooledSemaphore.Release();
        }

        public void Dispose()
        {
            _semaphore.Dispose();
        }
    }
}