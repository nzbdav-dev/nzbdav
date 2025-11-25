using Microsoft.AspNetCore.Http;
using NWebDav.Server;
using NWebDav.Server.Stores;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Queue;
using NzbWebDAV.WebDav.Base;
using NzbWebDAV.WebDav.Requests;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.WebDav;

public class DatabaseStoreCollection(
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
        var child = await dbClient.GetDirectoryChildAsync(davDirectory.Id, request.Name, request.CancellationToken).ConfigureAwait(false);
        if (child is null) return null;
        return GetItem(child);
    }

    protected override async Task<IStoreItem[]> GetAllItemsAsync(CancellationToken cancellationToken)
    {
        return (await dbClient.GetDirectoryChildrenAsync(davDirectory.Id, cancellationToken).ConfigureAwait(false))
            .Select(GetItem)
            .ToArray();
    }

    protected override bool SupportsFastMove(SupportsFastMoveRequest request)
    {
        return false;
    }

    protected override async Task<DavStatusCode> DeleteItemAsync(DeleteItemRequest request)
    {
        // Cannot delete items if readonly-webdav is enabled
        if (configManager.IsEnforceReadonlyWebdavEnabled())
            return DavStatusCode.Forbidden;

        // Cannot delete items from dav root.
        if (davDirectory.Id == DavItem.Root.Id)
            return DavStatusCode.Forbidden;

        // Get the item being deleted
        var davItem = await dbClient.GetDirectoryChildAsync(davDirectory.Id, request.Name, request.CancellationToken).ConfigureAwait(false);
        if (davItem is null) return DavStatusCode.NotFound;

        // If the item is a file, simply delete it and we're done.
        if (davItem.Type is DavItem.ItemType.NzbFile or DavItem.ItemType.RarFile or DavItem.ItemType.MultipartFile)
        {
            dbClient.Ctx.Items.Remove(davItem);
            await dbClient.Ctx.SaveChangesAsync().ConfigureAwait(false);
            return DavStatusCode.Ok;
        }

        // If the item is a directory and it not a protected directory, simply delete it.
        if (davItem.Type == DavItem.ItemType.Directory && !davItem.IsProtected())
        {
            dbClient.Ctx.Items.Remove(davItem);
            await dbClient.Ctx.SaveChangesAsync().ConfigureAwait(false);
            return DavStatusCode.Ok;
        }

        // forbid deletion of any other items
        return DavStatusCode.Forbidden;
    }

    private IStoreItem GetItem(DavItem davItem)
    {
        return davItem.Type switch
        {
            DavItem.ItemType.IdsRoot =>
                new DatabaseStoreIdsCollection(
                    davItem.Name, "", httpContext, dbClient, usenetClient, configManager),
            DavItem.ItemType.Directory when davItem.Id == DavItem.NzbFolder.Id =>
                new DatabaseStoreWatchFolder(
                    davItem, httpContext, dbClient, configManager, usenetClient, queueManager, websocketManager),
            DavItem.ItemType.Directory =>
                new DatabaseStoreCollection(
                    davItem, httpContext, dbClient, configManager, usenetClient, queueManager, websocketManager),
            DavItem.ItemType.SymlinkRoot =>
                new DatabaseStoreSymlinkCollection(
                    davItem, dbClient, configManager),
            DavItem.ItemType.NzbFile =>
                new DatabaseStoreNzbFile(
                    davItem, httpContext, dbClient, usenetClient, configManager),
            DavItem.ItemType.RarFile =>
                new DatabaseStoreRarFile(
                    davItem, httpContext, dbClient, usenetClient, configManager),
            DavItem.ItemType.MultipartFile =>
                new DatabaseStoreMultipartFile(
                    davItem, httpContext, dbClient, usenetClient, configManager),
            _ => throw new ArgumentException("Unrecognized directory child type.")
        };
    }
}