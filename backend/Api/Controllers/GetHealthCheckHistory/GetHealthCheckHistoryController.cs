using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;

namespace NzbWebDAV.Api.Controllers.GetHealthCheckHistory;

[ApiController]
[Route("api/get-health-check-history")]
public class GetHealthCheckHistoryController(DavDatabaseClient dbClient) : BaseApiController
{
    private async Task<GetHealthCheckHistoryResponse> GetHealthCheckHistory(GetHealthCheckHistoryRequest request)
    {
        var now = DateTime.UtcNow;
        var thirtyDaysAgo = now.AddDays(-30);
        var stats = await dbClient.GetHealthCheckStatsAsync(thirtyDaysAgo, now);
        var items = await dbClient.Ctx.HealthCheckResults
            .OrderByDescending(x => x.CreatedAt)
            .Take(request.PageSize)
            .ToListAsync();

        return new GetHealthCheckHistoryResponse()
        {
            Stats = stats,
            Items = items
        };
    }

    protected override async Task<IActionResult> HandleRequest()
    {
        var request = new GetHealthCheckHistoryRequest(HttpContext);
        var response = await GetHealthCheckHistory(request);
        return Ok(response);
    }
}