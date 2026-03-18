using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NzbWebDAV.Clients.Usenet.Concurrency;
using NzbWebDAV.Clients.Usenet.Contexts;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Services;
using NzbWebDAV.Streams;

namespace NzbWebDAV.WebDav.Base;

public abstract class BaseStoreStreamFile(HttpContext context) : BaseStoreReadonlyItem
{
    protected abstract Task<Stream> GetStreamAsync(CancellationToken cancellationToken);

    public override async Task<Stream> GetReadableStreamAsync(CancellationToken cancellationToken)
    {
        var downloadPriorityContext = new DownloadPriorityContext() { Priority = SemaphorePriority.High };
        var scopedDownloadPriorityContext = cancellationToken.SetContext(downloadPriorityContext);

        var stream = await GetStreamAsync(cancellationToken).ConfigureAwait(false);

        var tracker = context.RequestServices.GetRequiredService<ActiveStreamTracker>();
        var davItem = context.Items["DavItem"] as DavItem;
        var fileName = davItem?.Name ?? Name;
        var fileSize = davItem?.FileSize ?? FileSize;
        var streamId = tracker.Register(fileName, fileSize);
        var trackingStream = new ProgressTrackingStream(stream, streamId, tracker);

        context.Response.OnCompleted(() =>
        {
            scopedDownloadPriorityContext.Dispose();
            tracker.Unregister(streamId);
            return Task.CompletedTask;
        });

        return trackingStream;
    }
}
