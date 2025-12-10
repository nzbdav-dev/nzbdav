using NzbWebDAV.Clients.Usenet.Concurrency;

namespace NzbWebDAV.Clients.Usenet.Contexts;

public record DownloadPriorityContext
{
    public required SemaphorePriority Priority { get; init; }
}