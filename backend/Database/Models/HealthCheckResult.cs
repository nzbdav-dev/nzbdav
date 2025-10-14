namespace NzbWebDAV.Database.Models;

public class HealthCheckResult
{
    public Guid Id { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public Guid DavItemId { get; init; }
    public string Path { get; init; }
    public HealthResult Result { get; init; }
    public RepairAction RepairStatus { get; set; }
    public string? Message { get; set; }

    public enum HealthResult
    {
        Healthy = 0,
        Unhealthy = 1,
    }

    public enum RepairAction
    {
        None = 0,
        Repaired = 1,
        Deleted = 2,
        ActionNeeded = 3,
    }
}