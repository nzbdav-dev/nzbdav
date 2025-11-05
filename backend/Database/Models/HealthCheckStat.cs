namespace NzbWebDAV.Database.Models;

public class HealthCheckStat
{
    public DateTimeOffset DateStartInclusive { get; set; }
    public DateTimeOffset DateEndExclusive { get; set; }
    public HealthCheckResult.HealthResult Result { get; set; }
    public HealthCheckResult.RepairAction RepairStatus { get; set; }
    public int Count { get; set; }
}