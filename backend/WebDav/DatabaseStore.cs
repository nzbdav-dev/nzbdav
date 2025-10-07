using Microsoft.AspNetCore.Http;
using NWebDav.Server.Stores;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Queue;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.WebDav;

public class DatabaseStore(
    IHttpContextAccessor httpContextAccessor,
    DavDatabaseClient dbClient,
    ConfigManager configManager,
    UsenetStreamingClient usenetClient,
    QueueManager queueManager,
    WebsocketManager websocketManager
) : IStore
{
    private readonly DatabaseStoreCollection _root = new(
        DavItem.Root,
        httpContextAccessor.HttpContext!,
        dbClient,
        configManager,
        usenetClient,
        queueManager,
        websocketManager
    );

    public async Task<IStoreItem?> GetItemAsync(string path, CancellationToken cancellationToken)
    {
        path = path.Trim('/');
        return path == "" ? _root : await _root.ResolvePath(path, cancellationToken);
    }

    public Task<IStoreItem?> GetItemAsync(Uri uri, CancellationToken cancellationToken)
    {
        return GetItemAsync(Uri.UnescapeDataString(uri.AbsolutePath), cancellationToken);
    }

    public async Task<IStoreCollection?> GetCollectionAsync(Uri uri, CancellationToken cancellationToken)
    {
        return await GetItemAsync(uri, cancellationToken) as IStoreCollection;
    }
}