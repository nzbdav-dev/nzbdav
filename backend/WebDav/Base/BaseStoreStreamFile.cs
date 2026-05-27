using Microsoft.AspNetCore.Http;
using NzbWebDAV.Clients.Usenet.Concurrency;
using NzbWebDAV.Clients.Usenet.Contexts;
using NzbWebDAV.Config;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.WebDav.Base;

public abstract class BaseStoreStreamFile(HttpContext context, ConfigManager configManager) : BaseStoreReadonlyItem
{
    protected abstract Task<Stream> GetStreamAsync(CancellationToken cancellationToken);

    public override Task<Stream> GetReadableStreamAsync(CancellationToken cancellationToken)
    {
        var downloadPriorityContext = new DownloadPriorityContext() { Priority = SemaphorePriority.High };
        var scopedDownloadPriorityContext = cancellationToken.SetContext(downloadPriorityContext);

        var streamingTimeoutContext = new StreamingTimeoutContext
        {
            PerAttemptTimeout = configManager.GetStreamingSegmentTimeout(),
            MaxRetries = configManager.GetStreamingSegmentRetries()
        };
        var scopedStreamingTimeoutContext = cancellationToken.SetContext(streamingTimeoutContext);

        context.Response.OnCompleted(() =>
        {
            scopedDownloadPriorityContext.Dispose();
            scopedStreamingTimeoutContext.Dispose();
            return Task.CompletedTask;
        });

        return GetStreamAsync(cancellationToken);
    }
}