using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Config;
using NzbWebDAV.Database;

namespace NzbWebDAV.Api.SabControllers.GetHistory;

public class GetHistoryController(
    HttpContext httpContext,
    DavDatabaseClient dbClient,
    ConfigManager configManager
) : SabApiController.BaseController(httpContext, configManager)
{
    private async Task<GetHistoryResponse> GetHistoryAsync(GetHistoryRequest request)
    {
        // get total count
        var totalCount = await dbClient.Ctx.HistoryItems
            .Where(q => q.Category == request.Category || request.Category == null)
            .CountAsync(request.CancellationToken);

        // get history items
        var historyItems = await dbClient.Ctx.HistoryItems
            .Where(q => q.Category == request.Category || request.Category == null)
            .OrderByDescending(q => q.CreatedAt)
            .Skip(request.Start)
            .Take(request.Limit)
            .ToArrayAsync(request.CancellationToken);

        // get slots
        var slots = historyItems
            .Select(x => GetHistoryResponse.HistorySlot.FromHistoryItem(x, configManager.GetRcloneMountDir()))
            .ToList();

        // return response
        return new GetHistoryResponse()
        {
            History = new GetHistoryResponse.HistoryObject()
            {
                Slots = slots,
                TotalCount = totalCount,
            }
        };
    }

    protected override async Task<IActionResult> Handle()
    {
        var request = new GetHistoryRequest(httpContext);
        return Ok(await GetHistoryAsync(request));
    }
}