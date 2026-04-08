using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NWebDav.Server.Stores;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Queue;
using NzbWebDAV.WebDav.Base;
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
) : BaseStoreReadonlyCollection
{
    public override string Name => davDirectory.Name;
    public override string UniqueKey => davDirectory.Id.ToString();
    public override DateTime CreatedAt => davDirectory.CreatedAt;

    protected override async Task<IStoreItem?> GetItemAsync(GetItemRequest request)
    {
        var categories = await GetCategoriesAsync(request.CancellationToken).ConfigureAwait(false);
        if (!categories.Contains(request.Name)) return null;
        return new DatabaseStoreCategoryWatchFolder(
            request.Name, dbClient, configManager, queueManager, websocketManager);
    }

    protected override async Task<IStoreItem[]> GetAllItemsAsync(CancellationToken cancellationToken)
    {
        var categories = await GetCategoriesAsync(cancellationToken).ConfigureAwait(false);
        return categories
            .Select(c => new DatabaseStoreCategoryWatchFolder(
                c, dbClient, configManager, queueManager, websocketManager))
            .Select(IStoreItem (x) => x)
            .ToArray();
    }

    private async Task<IReadOnlySet<string>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        var queueCategories = await dbClient.Ctx.QueueItems
            .Select(x => x.Category)
            .Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var configCategories = configManager.GetApiCategories();

        return queueCategories
            .Concat(configCategories)
            .Where(c => !string.IsNullOrEmpty(c))
            .ToHashSet();
    }
}
