using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Queue;

namespace NzbWebDAV.Api.SabControllers.GetQueue;

public class GetQueueController(
    HttpContext httpContext,
    DavDatabaseClient dbClient,
    QueueManager queueManager,
    ConfigManager configManager
) : SabApiController.BaseController(httpContext, configManager)
{
    private async Task<GetQueueResponse> GetQueueAsync(GetQueueRequest request)
    {
        // get in progress items
        var inProgressQueueItems = queueManager.GetInProgressQueueItems()
            .Where(x => request.Category is null || x.queueItem.Category == request.Category)
            .ToArray();
        var inProgressQueueItemIds = inProgressQueueItems
            .Select(x => x.queueItem.Id)
            .ToHashSet();
        var inProgressProgressById = inProgressQueueItems
            .ToDictionary(x => x.queueItem.Id, x => x.progress);

        // get total count
        var ct = request.CancellationToken;
        var totalCount = await dbClient.GetQueueItemsCount(request.Category, ct).ConfigureAwait(false);

        // get active and queued page windows
        var activeItems = inProgressQueueItems.Select(x => x.queueItem).ToArray();
        var activePageItems = activeItems
            .Skip(request.Start)
            .Take(request.Limit)
            .ToArray();
        var queuedStart = Math.Max(0, request.Start - activeItems.Length);
        var queuedLimit = Math.Max(0, request.Limit - activePageItems.Length);

        // get queued items
        var queueItems = queuedLimit > 0
            ? (await dbClient.GetQueueItems(request.Category, queuedStart, queuedLimit + activeItems.Length, ct).ConfigureAwait(false))
                .Where(x => !inProgressQueueItemIds.Contains(x.Id))
                .Take(queuedLimit)
                .ToArray()
            : [];

        // get slots
        var slots = activePageItems
            .Concat(queueItems)
            .Select((queueItem, index) =>
            {
                var isInProgress = inProgressProgressById.TryGetValue(queueItem.Id, out var progressPercentage);
                var percentage = isInProgress ? progressPercentage : 0;
                var status = isInProgress ? "Downloading" : "Queued";
                return GetQueueResponse.QueueSlot.FromQueueItem(queueItem, request.Start + index, percentage, status);
            })
            .ToList();

        // return response
        return new GetQueueResponse()
        {
            Queue = new GetQueueResponse.QueueObject()
            {
                Paused = false,
                Slots = slots,
                TotalCount = totalCount,
            }
        };
    }

    protected override async Task<IActionResult> Handle()
    {
        var request = new GetQueueRequest(httpContext);
        return Ok(await GetQueueAsync(request).ConfigureAwait(false));
    }
}