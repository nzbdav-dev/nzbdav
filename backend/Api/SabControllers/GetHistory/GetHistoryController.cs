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

        // get history items with joined DavItem data
        var historyItemsWithDavItems = await dbClient.Ctx.HistoryItems
            .Where(q => q.Category == request.Category || request.Category == null)
            .GroupJoin(
                dbClient.Ctx.Items,
                h => h.DownloadDirId,
                d => d.Id,
                (h, d) => new { HistoryItem = h, DavItem = d.FirstOrDefault() }
            )
            .OrderByDescending(q => q.HistoryItem.CreatedAt)
            .Skip(request.Start)
            .Take(request.Limit)
            .ToArrayAsync(request.CancellationToken);

        // get slots
        var slots = historyItemsWithDavItems
            .Select(x => GetHistoryResponse.HistorySlot.FromHistoryItem(
                x.HistoryItem, x.DavItem, configManager.GetRcloneMountDir()))
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
        var request = new GetHistoryRequest(httpContext, configManager);
        return Ok(await GetHistoryAsync(request));
    }
}