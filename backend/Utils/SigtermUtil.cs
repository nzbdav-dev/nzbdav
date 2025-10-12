using System.Runtime.Loader;

namespace NzbWebDAV.Utils;

public static class SigtermUtil
{
    private static readonly CancellationTokenSource CancellationTokenSource = new();
    private static bool _isInitialized = false;

    /// <summary>
    /// Gets a CancellationToken that will be cancelled when the application
    /// receives a SIGTERM signal (e.g., from a shutdown command in a container).
    /// </summary>
    /// <remarks>
    /// This method is designed to be called once at application startup.
    /// It hooks into the AssemblyLoadContext.Default.Unloading event, which is the
    /// standard way to handle SIGTERM on modern .NET platforms.
    /// </remarks>
    public static CancellationToken GetCancellationToken()
    {
        // Use a lock or flag to ensure the event handler is only registered once.
        lock (CancellationTokenSource)
        {
            if (!_isInitialized)
            {
                // Subscribe to the Unloading event of the default AssemblyLoadContext.
                // This event is triggered when the host runtime initiates a shutdown,
                // which typically happens in response to a SIGTERM signal.
                AssemblyLoadContext.Default.Unloading += (_) =>
                {
                    // Signal cancellation to any tasks that are listening.
                    CancellationTokenSource.Cancel();
                };

                // Set the flag to true to prevent re-initialization.
                _isInitialized = true;
            }
        }

        return CancellationTokenSource.Token;
    }

    public static void Cancel()
    {
        CancellationTokenSource.Cancel();
    }
}