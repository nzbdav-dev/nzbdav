namespace NzbWebDAV.Tasks;

public abstract class BaseTask
{
    protected abstract Task ExecuteInternal();

    private static readonly SemaphoreSlim Semaphore = new(1, 1);
    private static Task? _runningTask;

    public async Task<bool> Execute()
    {
        await Semaphore.WaitAsync().ConfigureAwait(false);
        Task? task;
        try
        {
            // if the task is already running, return immediately.
            if (_runningTask is { IsCompleted: false })
                return false;

            // otherwise, run the task.
            _runningTask = Task.Run(ExecuteInternal);
            task = _runningTask;
        }
        finally
        {
            Semaphore.Release();
        }

        // and wait for it to finish.
        await task.ConfigureAwait(false);
        return true;
    }
}