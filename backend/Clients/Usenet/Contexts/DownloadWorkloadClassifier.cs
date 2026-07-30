using NzbWebDAV.Clients.Usenet.Concurrency;
using NzbWebDAV.Extensions;
using NzbWebDAV.Services.Metrics;

namespace NzbWebDAV.Clients.Usenet.Contexts;

internal static class DownloadWorkloadClassifier
{
    internal static DownloadWorkload Classify(CancellationToken cancellationToken)
    {
        if (cancellationToken.GetContext<QueueDownloadContext>() is not null)
            return DownloadWorkload.Queue;
        if (cancellationToken.GetContext<MaintenanceDownloadContext>() is not null)
            return DownloadWorkload.Maintenance;
        if (cancellationToken.GetContext<DownloadPriorityContext>()?.Priority == SemaphorePriority.High)
            return DownloadWorkload.Streaming;
        return DownloadWorkload.Background;
    }
}
