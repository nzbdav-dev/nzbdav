using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NzbWebDAV.Clients.Usenet.Concurrency;
using NzbWebDAV.Clients.Usenet.Contexts;
using NzbWebDAV.Extensions;
using NzbWebDAV.Services;

namespace NzbWebDAV.WebDav.Base;

public abstract class BaseStoreStreamFile(HttpContext context) : BaseStoreReadonlyItem
{
    protected abstract Task<Stream> GetStreamAsync(CancellationToken cancellationToken);

    public override Task<Stream> GetReadableStreamAsync(CancellationToken cancellationToken)
    {
        var downloadPriorityContext = new DownloadPriorityContext() { Priority = SemaphorePriority.High };
        var scopedDownloadPriorityContext = cancellationToken.SetContext(downloadPriorityContext);

        // track usenet download speed for this file-stream
        var speedTracker = context.RequestServices.GetService<DownloadSpeedTracker>();
        var speedSession = speedTracker?.CreateSession(Name, DownloadSpeedSession.SessionCategory.Stream);
        CancellationTokenContext? scopedSpeedTrackingContext = null;
        try
        {
            scopedSpeedTrackingContext = speedSession is not null
                ? cancellationToken.SetContext(new SpeedTrackingContext() { Session = speedSession })
                : null;

            context.Response.OnCompleted(() =>
            {
                scopedDownloadPriorityContext.Dispose();
                scopedSpeedTrackingContext?.Dispose();
                speedSession?.Dispose();
                return Task.CompletedTask;
            });
        }
        catch
        {
            // the OnCompleted callback will never run; clean up here instead.
            scopedDownloadPriorityContext.Dispose();
            scopedSpeedTrackingContext?.Dispose();
            speedSession?.Dispose();
            throw;
        }

        return GetStreamAsync(cancellationToken);
    }
}