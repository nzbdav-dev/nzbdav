namespace NzbWebDAV.Queue.FileProcessors;

/// <summary>
/// The base processor for all child processors.
/// Children must override either of the two `ProcessAsync` method overloads, but not both.
/// </summary>
public abstract class BaseProcessor
{
    public class Result;

    public virtual Task<Result?> ProcessAsync()
    {
        return Task.FromResult<Result?>(null);
    }

    public virtual async Task<Result?> ProcessAsync(IProgress<int> progress)
    {
        try
        {
            return await ProcessAsync();
        }
        finally
        {
            progress.Report(100);
        }
    }
}