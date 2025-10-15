using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Api.Controllers.GetHealthCheckHistory;

public class GetHealthCheckHistoryResponse : BaseApiResponse
{
    public required List<HealthCheckStats> Stats { get; init; }
    public required List<HealthCheckResult> Items { get; init; }
}