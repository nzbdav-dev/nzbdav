using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Api.Controllers.GetHealthCheckHistory;

public class GetHealthCheckHistoryResponse : BaseApiResponse
{
    public required List<HealthCheckStat> Stats { get; init; }
    public required List<HealthCheckResult> Items { get; init; }
    public int Page { get; init; }
    public bool HasMore { get; init; }
}
