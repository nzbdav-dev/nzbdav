// ReSharper disable InconsistentNaming

namespace NzbWebDAV.Extensions;

public static class IEnumerableTaskExtensions
{
    /// <summary>
    /// Executes tasks with specified concurrency and enumerates results as they come in
    /// </summary>
    /// <param name="tasks">The tasks to execute</param>
    /// <param name="concurrency">The max concurrency</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <typeparam name="T">The resulting type of each task</typeparam>
    /// <returns>An IAsyncEnumerable that enumerates task results as they come in</returns>
    public static IEnumerable<Task<T>> WithConcurrency<T>
    (
        this IEnumerable<Task<T>> tasks,
        int concurrency
    ) where T : IDisposable
    {
        if (concurrency < 1)
            throw new ArgumentException("concurrency must be greater than zero.");

        if (concurrency == 1)
        {
            foreach (var task in tasks) yield return task;
            yield break;
        }

        var isFirst = true;
        var runningTasks = new Queue<Task<T>>();
        try
        {
            foreach (var task in tasks)
            {
                if (isFirst)
                {
                    // help with time-to-first-byte
                    yield return task;
                    isFirst = false;
                    continue;
                }

                runningTasks.Enqueue(task);
                if (runningTasks.Count < concurrency) continue;
                yield return runningTasks.Dequeue();
            }

            while (runningTasks.Count > 0)
                yield return runningTasks.Dequeue();
        }
        finally
        {
            while (runningTasks.Count > 0)
            {
                runningTasks.Dequeue().ContinueWith(x =>
                {
                    if (x.Status == TaskStatus.RanToCompletion)
                        x.Result.Dispose();
                });
            }
        }
    }

    public static async IAsyncEnumerable<T> WithConcurrencyAsync<T>
    (
        this IEnumerable<Task<T>> tasks,
        int concurrency
    )
    {
        if (concurrency < 1)
            throw new ArgumentException("concurrency must be greater than zero.");

        var runningTasks = new HashSet<Task<T>>();
        var totalStarted = 0;
        var totalCompleted = 0;

        foreach (var task in tasks)
        {
            runningTasks.Add(task);
            totalStarted++;
            Serilog.Log.Debug($"[WithConcurrency] Started task {totalStarted}, Running={runningTasks.Count}, MaxConcurrency={concurrency}");

            if (runningTasks.Count < concurrency) continue;
            var completedTask = await Task.WhenAny(runningTasks).ConfigureAwait(false);
            runningTasks.Remove(completedTask);
            totalCompleted++;
            Serilog.Log.Debug($"[WithConcurrency] Task completed {totalCompleted}/{totalStarted}, Running={runningTasks.Count}");
            yield return await completedTask.ConfigureAwait(false);
        }

        Serilog.Log.Debug($"[WithConcurrency] All tasks started ({totalStarted}), draining remaining {runningTasks.Count}");
        while (runningTasks.Count > 0)
        {
            var completedTask = await Task.WhenAny(runningTasks).ConfigureAwait(false);
            runningTasks.Remove(completedTask);
            totalCompleted++;
            Serilog.Log.Debug($"[WithConcurrency] Task completed {totalCompleted}/{totalStarted}, Remaining={runningTasks.Count}");
            yield return await completedTask.ConfigureAwait(false);
        }
    }
}