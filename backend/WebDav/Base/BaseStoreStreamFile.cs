using Microsoft.AspNetCore.Http;
using NzbWebDAV.Clients.Usenet.Concurrency;
using NzbWebDAV.Clients.Usenet.Contexts;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.WebDav.Base;

public abstract class BaseStoreStreamFile(HttpContext context) : BaseStoreReadonlyItem
{
    protected abstract Task<Stream> GetStreamAsync(CancellationToken cancellationToken);

    public override Task<Stream> GetReadableStreamAsync(CancellationToken cancellationToken)
    {
        var downloadPriorityContext = new DownloadPriorityContext() { Priority = SemaphorePriority.High };
        var scopedDownloadPriorityContext = cancellationToken.SetContext(downloadPriorityContext);
        context.Response.OnCompleted(() =>
        {
            scopedDownloadPriorityContext.Dispose();
            return Task.CompletedTask;
        });

        return GetStreamAsync(cancellationToken);
    }
}