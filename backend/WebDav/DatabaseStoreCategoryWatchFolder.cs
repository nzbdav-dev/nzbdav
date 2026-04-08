using Microsoft.EntityFrameworkCore;
using NWebDav.Server;
using NWebDav.Server.Stores;
using NzbWebDAV.Api.SabControllers.AddFile;
using NzbWebDAV.Api.SabControllers.RemoveFromQueue;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Queue;
using NzbWebDAV.WebDav.Base;
using NzbWebDAV.WebDav.Requests;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.WebDav;

public class DatabaseStoreCategoryWatchFolder(
    string category,
    DavDatabaseClient dbClient,
    ConfigManager configManager,
    QueueManager queueManager,
    WebsocketManager websocketManager
) : BaseStoreReadonlyCollection
{
    public override string Name => category;
    public override string UniqueKey => $"nzbs_category_{category}";
    public override DateTime CreatedAt => DateTime.Now;

    protected override async Task<IStoreItem?> GetItemAsync(GetItemRequest request)
    {
        var queueItem = await dbClient.Ctx.QueueItems
            .Where(x => x.FileName == request.Name && x.Category == category)
            .FirstOrDefaultAsync(request.CancellationToken).ConfigureAwait(false);
        if (queueItem is null) return null;
        return new DatabaseStoreQueueItem(queueItem, dbClient);
    }

    protected override async Task<IStoreItem[]> GetAllItemsAsync(CancellationToken cancellationToken)
    {
        return (await dbClient.GetQueueItems(category, 0, int.MaxValue, cancellationToken).ConfigureAwait(false))
            .Select(x => new DatabaseStoreQueueItem(x, dbClient))
            .Select(IStoreItem (x) => x)
            .ToArray();
    }

    protected override async Task<StoreItemResult> CreateItemAsync(CreateItemRequest request)
    {
        var controller = new AddFileController(null!, dbClient, queueManager, configManager, websocketManager);
        var addFileRequest = new AddFileRequest()
        {
            FileName = request.Name,
            ContentType = "application/x-nzb",
            Category = category,
            Priority = QueueItem.PriorityOption.Normal,
            PostProcessing = QueueItem.PostProcessingOption.RepairUnpackDelete,
            PauseUntil = DateTime.Now.AddSeconds(3),
            NzbFileStream = request.Stream,
            CancellationToken = request.CancellationToken
        };
        var response = await controller.AddFileAsync(addFileRequest).ConfigureAwait(false);
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
            .Where(x => x.FileName == request.Name && x.Category == category)
            .FirstOrDefaultAsync(request.CancellationToken).ConfigureAwait(false);

        // if the item doesn't exist, return 404
        if (item is null)
            return DavStatusCode.NotFound;

        // delete the item
        dbClient.Ctx.ClearChangeTracker();
        await controller.RemoveFromQueue(new RemoveFromQueueRequest()
        {
            NzoIds = [item.Id],
            CancellationToken = request.CancellationToken
        }).ConfigureAwait(false);
        return DavStatusCode.NoContent;
    }
}