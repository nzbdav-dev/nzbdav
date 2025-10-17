namespace NzbWebDAV.Api.Controllers.GetHealthCheckQueue;

public class GetHealthCheckQueueResponse : BaseApiResponse
{
    public List<HealthCheckQueueItem> Items { get; init; } = [];
    public int UncheckedCount { get; init; }

    public class HealthCheckQueueItem
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Path { get; init; }
        public required DateTimeOffset? ReleaseDate { get; init; }
        public required DateTimeOffset? LastHealthCheck { get; init; }
        public required DateTimeOffset? NextHealthCheck { get; init; }
    }
}