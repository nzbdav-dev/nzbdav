namespace NzbWebDAV.Database.Models;

public class HealthCheckStats
{
    public HealthCheckResult.HealthResult Result { get; set; }
    public HealthCheckResult.RepairAction RepairStatus { get; set; }
    public int Count { get; set; }
}