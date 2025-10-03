﻿using Microsoft.AspNetCore.Http;
using NWebDav.Server;
using NWebDav.Server.Stores;
using NzbWebDAV.Clients;
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
        var child = await dbClient.GetDirectoryChildAsync(davDirectory.Id, request.Name, request.CancellationToken);
        if (child is null) return null;
        return GetItem(child);
    }

    protected override async Task<IStoreItem[]> GetAllItemsAsync(CancellationToken cancellationToken)
    {
        return (await dbClient.GetDirectoryChildrenAsync(davDirectory.Id, cancellationToken))
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
        var davItem = await dbClient.GetDirectoryChildAsync(davDirectory.Id, request.Name, request.CancellationToken);
        if (davItem is null) return DavStatusCode.NotFound;

        var success = await dbClient.DeleteItemAsync(davItem, request.CancellationToken);
        return success ? DavStatusCode.Ok : DavStatusCode.Forbidden;
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
            _ => throw new ArgumentException("Unrecognized directory child type.")
        };
    }
}