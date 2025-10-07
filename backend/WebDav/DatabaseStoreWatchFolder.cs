using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NWebDav.Server;
using NWebDav.Server.Stores;
using NzbWebDAV.Api.SabControllers.AddFile;
using NzbWebDAV.Api.SabControllers.RemoveFromQueue;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Queue;
using NzbWebDAV.WebDav.Requests;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.WebDav;

public class DatabaseStoreWatchFolder(
    DavItem davDirectory,
    HttpContext httpContext,
    DavDatabaseClient dbClient,
    ConfigManager configManager,
    UsenetStreamingClient usenetClient,
    QueueManager queueManager,
    WebsocketManager websocketManager
) : DatabaseStoreCollection(
    davDirectory,
    httpContext,
    dbClient,
    configManager,
    usenetClient,
    queueManager,
    websocketManager
)
{
    protected override async Task<IStoreItem?> GetItemAsync(GetItemRequest request)
    {
        var queueItem = await dbClient.Ctx.QueueItems
            .Where(x => x.FileName == request.Name)
            .FirstOrDefaultAsync(request.CancellationToken);
        if (queueItem is null) return null;
        return new DatabaseStoreQueueItem(queueItem, dbClient);
    }

    protected override async Task<IStoreItem[]> GetAllItemsAsync(CancellationToken cancellationToken)
    {
        return (await dbClient.GetQueueItems(null, 0, int.MaxValue, cancellationToken))
            .Select(x => new DatabaseStoreQueueItem(x, dbClient))
            .Select(IStoreItem (x) => x)
            .ToArray();
    }

    protected override async Task<StoreItemResult> CreateItemAsync(CreateItemRequest request)
    {
        var controller = new AddFileController(null!, dbClient, queueManager, configManager, websocketManager);
        using var streamReader = new StreamReader(request.Stream);
        var nzbFileContents = await streamReader.ReadToEndAsync(request.CancellationToken);
        var addFileRequest = new AddFileRequest()
        {
            FileName = request.Name,
            MimeType = "application/x-nzb",
            Category = "uncategorized",
            Priority = QueueItem.PriorityOption.Normal,
            PostProcessing = QueueItem.PostProcessingOption.RepairUnpackDelete,
            PauseUntil = DateTime.Now.AddSeconds(3),
            NzbFileContents = nzbFileContents,
            CancellationToken = request.CancellationToken
        };
        var response = await controller.AddFileAsync(addFileRequest);
        var queueItem = dbClient.Ctx.ChangeTracker
            .Entries<QueueItem>()
            .Select(x => x.Entity)
            .First(x => x.Id.ToString() == response.NzoIds[0]);
        return new StoreItemResult(DavStatusCode.Created, new DatabaseStoreQueueItem(queueItem, dbClient));
    }

    protected override async Task<DavStatusCode> DeleteItemAsync(DeleteItemRequest request)
    {
        var controller = new RemoveFromQueueController(null!, dbClient, queueManager, configManager, websocketManager);

        // get the item to delete
        var item = await dbClient.Ctx.QueueItems
            .Where(x => x.FileName == request.Name)
            .FirstOrDefaultAsync(request.CancellationToken);

        // if the item doesn't exist, return 404
        if (item is null)
            return DavStatusCode.NotFound;

        // delete the item
        dbClient.Ctx.ChangeTracker.Clear();
        await controller.RemoveFromQueue(new RemoveFromQueueRequest()
        {
            NzoIds = [item.Id],
            CancellationToken = request.CancellationToken
        });
        return DavStatusCode.NoContent;
    }
}