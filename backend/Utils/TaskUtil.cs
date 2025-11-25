namespace NzbWebDAV.Utils;

public static class TaskUtil
{
    /// <summary>
    /// Waits for all the given tasks to complete.
    /// if any task throws an exception, the method short-circuit and stops early.
    /// In the cases of no errors, the returned results are in order of completion.
    /// </summary>
    /// <param name="tasks">Tha tasks to wait on.</param>
    /// <param name="progress">Callback handler for progress report.</param>
    /// <typeparam name="T">The result type of each task.</typeparam>
    /// <returns>The results of each task.</returns>
    /// <exception cref="Exception">The first task error thrown.</exception>
    public static async Task<List<T>> WhenAllOrError<T>(IEnumerable<Task<T>> tasks, IProgress<int>? progress = null)
    {
        var results = new List<T>();
        var taskList = tasks.ToList();
        var totalTasks = taskList.Count();
        var completedTasks = 0;
        while (taskList.Count > 0)
        {
            var completedTask = await Task.WhenAny(taskList).ConfigureAwait(false);
            taskList.Remove(completedTask);

            if (completedTask.IsFaulted)
            {
                throw completedTask?.Exception?.InnerException ??
                    new Exception("An unknown error occurred.");
            }

            completedTasks++;
            results.Add(completedTask.Result);
            progress?.Report((completedTasks) * 100 / totalTasks);
        }

        return results;
    }
}