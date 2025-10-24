using Microsoft.AspNetCore.Http;
using NWebDav.Server.Stores;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.WebDav.Base;

namespace NzbWebDAV.WebDav;

public class DatabaseStoreIdFile(
    DavItem davItem,
    HttpContext httpContext,
    DavDatabaseClient dbClient,
    UsenetStreamingClient usenetClient,
    ConfigManager configManager
) : BaseStoreReadonlyItem
{
    public override string Name => davItem.Id.ToString();
    public override string UniqueKey => davItem.Id.ToString();
    public override long FileSize => davItem.FileSize!.Value;
    public override DateTime CreatedAt => davItem.CreatedAt;

    public override Task<Stream> GetReadableStreamAsync(CancellationToken cancellationToken)
    {
        return GetItem(davItem).GetReadableStreamAsync(cancellationToken);
    }

    private IStoreItem GetItem(DavItem davItem)
    {
        return davItem.Type switch
        {
            DavItem.ItemType.NzbFile =>
                new DatabaseStoreNzbFile(davItem, httpContext, dbClient, usenetClient, configManager),
            DavItem.ItemType.RarFile =>
                new DatabaseStoreRarFile(davItem, httpContext, dbClient, usenetClient, configManager),
            DavItem.ItemType.MultipartFile =>
                new DatabaseStoreMultipartFile(davItem, httpContext, dbClient, usenetClient, configManager),
            _ => throw new ArgumentException("Unrecognized id child type.")
        };
    }
}