using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;

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

        var query = dbClient.Ctx.HealthCheckResults
            .OrderByDescending(x => x.CreatedAt)
            .AsQueryable();

        if (request.RepairStatus.HasValue)
        {
            var status = (HealthCheckResult.RepairAction)request.RepairStatus.Value;
            query = query.Where(x => x.RepairStatus == status);
        }

        var skip = (request.Page - 1) * request.PageSize;
        var items = await query
            .Skip(skip)
            .Take(request.PageSize + 1) // fetch one extra to detect hasMore
            .ToListAsync();
        var hasMore = items.Count > request.PageSize;
        if (hasMore) items = items.Take(request.PageSize).ToList();

        return new GetHealthCheckHistoryResponse()
        {
            Stats = await statsPromise.ConfigureAwait(false),
            Items = items,
            Page = request.Page,
            HasMore = hasMore
        };
    }

    protected override async Task<IActionResult> HandleRequest()
    {
        var request = new GetHealthCheckHistoryRequest(HttpContext);
        var response = await GetHealthCheckHistory(request).ConfigureAwait(false);
        return Ok(response);
    }
}
