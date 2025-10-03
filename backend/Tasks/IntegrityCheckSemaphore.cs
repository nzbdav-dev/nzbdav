namespace NzbWebDAV.Tasks;

/// <summary>
/// Shared coordination to ensure only one integrity check (manual or scheduled) runs at a time
/// </summary>
public static class IntegrityCheckSemaphore
{
    private static readonly SemaphoreSlim _semaphore = new(1, 1);
    private static Task? _runningTask = null;
    private static string? _runningTaskType = null;
    private static CancellationTokenSource? _taskCancellationTokenSource = null;

    /// <summary>
    /// Attempts to start an integrity check by setting the global running task
    /// </summary>
    /// <param name="task">The task to set as the running task (must be created but not started)</param>
    /// <param name="taskType">Description of the task type (e.g., "Manual", "Scheduled")</param>
    /// <param name="taskCancellationTokenSource">The cancellation token source for this task</param>
    /// <param name="cancellationToken">Cancellation token for this operation</param>
    /// <returns>True if the task was started successfully, false if another task is already running</returns>
    public static async Task<bool> TryStartTaskAsync(Task task, string taskType, CancellationTokenSource taskCancellationTokenSource, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            // Check if there's already a running task
            if (_runningTask is { IsCompleted: false })
            {
                return false; // Another task is already running
            }

            // Set the new running task and start it
            _runningTask = task;
            _runningTaskType = taskType;
            _taskCancellationTokenSource = taskCancellationTokenSource;

            // Start the task
            task.Start();

            // Set up continuation to clear the running task when done
            _ = task.ContinueWith(async _ =>
            {
                await ClearRunningTaskAsync();
            }, TaskContinuationOptions.ExecuteSynchronously);

            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Clears the running task (should be called when task completes)
    /// </summary>
    public static async Task ClearRunningTaskAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            _runningTask = null;
            _runningTaskType = null;
            _taskCancellationTokenSource = null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Cancels the currently running task if any
    /// </summary>
    /// <returns>True if a task was cancelled, false if no task was running</returns>
    public static async Task<bool> CancelRunningTaskAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (_runningTask is { IsCompleted: false } && _taskCancellationTokenSource != null)
            {
                _taskCancellationTokenSource.Cancel();
                return true;
            }
            return false;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Gets the current running task status
    /// </summary>
    /// <returns>Tuple indicating if a task is running and its type</returns>
    public static async Task<(bool IsRunning, string? TaskType)> GetRunningTaskStatusAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            var isRunning = _runningTask is { IsCompleted: false };
            return (isRunning, isRunning ? _runningTaskType : null);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Gets the current running task (if any)
    /// </summary>
    /// <returns>The running task or null if no task is running</returns>
    public static async Task<Task?> GetRunningTaskAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            return _runningTask is { IsCompleted: false } ? _runningTask : null;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
