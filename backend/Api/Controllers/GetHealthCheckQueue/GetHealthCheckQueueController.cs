using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Services;

namespace NzbWebDAV.Api.Controllers.GetHealthCheckQueue;

[ApiController]
[Route("api/get-health-check-queue")]
public class GetHealthCheckQueueController(DavDatabaseClient dbClient) : BaseApiController
{
    private async Task<GetHealthCheckQueueResponse> GetHealthCheckQueue(GetHealthCheckQueueRequest request)
    {
        var davItems = await HealthCheckService.GetHealthCheckQueueItems(dbClient)
            .Take(request.PageSize)
            .ToListAsync();

        var uncheckedCount = await dbClient.Ctx.Items
            .Where(x => x.Type == DavItem.ItemType.NzbFile || x.Type == DavItem.ItemType.RarFile)
            .Where(x => x.NextHealthCheck == null)
            .CountAsync();

        return new GetHealthCheckQueueResponse()
        {
            UncheckedCount = uncheckedCount,
            Items = davItems.Select(x => new GetHealthCheckQueueResponse.HealthCheckQueueItem()
            {
                Id = x.Id.ToString(),
                Name = x.Name,
                Path = x.Path,
                ReleaseDate = x.ReleaseDate,
                LastHealthCheck = x.LastHealthCheck,
                NextHealthCheck = x.NextHealthCheck,
            }).ToList(),
        };
    }

    protected override async Task<IActionResult> HandleRequest()
    {
        var request = new GetHealthCheckQueueRequest(HttpContext);
        var response = await GetHealthCheckQueue(request);
        return Ok(response);
    }
}