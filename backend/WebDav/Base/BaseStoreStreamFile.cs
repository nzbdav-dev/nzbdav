using Microsoft.AspNetCore.Http;
using NzbWebDAV.Clients.Usenet.Contexts;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.WebDav.Base;

public abstract class BaseStoreStreamFile(HttpContext context) : BaseStoreReadonlyItem
{
    protected abstract Task<Stream> GetStreamAsync(CancellationToken cancellationToken);

    public override Task<Stream> GetReadableStreamAsync(CancellationToken cancellationToken)
    {
        var downloadPriorityContext = cancellationToken.SetScopedContext(DownloadPriorityContext.High);
        context.Response.OnCompleted(() =>
        {
            downloadPriorityContext.Dispose();
            return Task.CompletedTask;
        });

        return GetStreamAsync(cancellationToken);
    }
}