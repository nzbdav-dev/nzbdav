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
        var tomorrow = now.AddDays(1);
        var thirtyDaysAgo = now.AddDays(-30);
        var statsPromise = dbClient.GetHealthCheckStatsAsync(thirtyDaysAgo, tomorrow);
        var itemsPromise = dbClient.Ctx.HealthCheckResults
            .OrderByDescending(x => x.CreatedAt)
            .Take(request.PageSize)
            .ToListAsync();

        return new GetHealthCheckHistoryResponse()
        {
            Stats = await statsPromise.ConfigureAwait(false),
            Items = await itemsPromise.ConfigureAwait(false)
        };
    }

    protected override async Task<IActionResult> HandleRequest()
    {
        var request = new GetHealthCheckHistoryRequest(HttpContext);
        var response = await GetHealthCheckHistory(request).ConfigureAwait(false);
        return Ok(response);
    }
}