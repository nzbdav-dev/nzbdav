using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using NWebDav.Server;
using NWebDav.Server.Stores;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.WebDav.Base;
using NzbWebDAV.WebDav.Requests;

namespace NzbWebDAV.WebDav;

public class DatabaseStoreSymlinkCollection(
    DavItem davDirectory,
    DavDatabaseClient dbClient,
    ConfigManager configManager
) : BaseStoreReadonlyCollection
{
    public override string Name => davDirectory.Name;
    public override string UniqueKey => davDirectory.Id.ToString();
    public override DateTime CreatedAt => davDirectory.CreatedAt;

    private Guid TargetId => davDirectory.Id == DavItem.SymlinkFolder.Id ? DavItem.ContentFolder.Id : davDirectory.Id;
    private DeletedFileManager DeletedFiles => new(davDirectory.Id);

    protected override async Task<IStoreItem?> GetItemAsync(GetItemRequest request)
    {
        if (DeletedFiles.IsDeleted(request.Name)) return null;
        var name = Regex.Replace(request.Name, @"\.rclonelink$", "");
        var child = await dbClient.GetDirectoryChildAsync(TargetId, name, request.CancellationToken);
        if (child is null) return null;
        return GetItem(child);
    }

    protected override async Task<IStoreItem[]> GetAllItemsAsync(CancellationToken cancellationToken)
    {
        // if we are a category folder within the /completed-symlinks dir,
        // then we only want to show children that correspond to Completed History items.
        var isCategoryFolder = davDirectory.ParentId == DavItem.ContentFolder.Id;
        var children = isCategoryFolder
            ? await dbClient.GetCompletedSymlinkCategoryChildren(davDirectory.Name, cancellationToken)
            : await dbClient.GetDirectoryChildrenAsync(TargetId, cancellationToken);

        return children
            .Select(GetItem)
            .Where(x => !DeletedFiles.IsDeleted(x.Name)) // must appear after Select(GetItem) for correct Name.
            .ToArray();
    }

    protected override bool SupportsFastMove(SupportsFastMoveRequest request)
    {
        return false;
    }

    protected override Task<DavStatusCode> DeleteItemAsync(DeleteItemRequest request)
    {
        // Items cannot be deleted from the '/completed-symlinks' folder.
        // This path simply mirrors the '/content' folder, except with symlinks.
        // This allows radarr/sonarr to import the lightweight symlink, instead
        // of trying to import large-sized media.
        //
        // However, when radarr attempts to import the symlink, it does so by moving
        // it to the media library. But since the symlinks lives in a separate
        // file-system (rclone-mounted webdav), the operating system will instead
        // perform a copy-and-delete operation. For the import to succeed, we must
        // trick the OS into thinking that the "delete" worked.
        //
        // The symlink doesn't actually exist anywhere. It takes zero storage and
        // just gets created in memory, as needed, for webdav requests. The only
        // thing that exists is the underlying data within the '/content' directory
        // But in this request, we only want to "delete" the symlink. We don't want
        // to delete the underlying media within the '/content' directory.
        //
        // Instead, we store the filename in a temporary cache (for 30 seconds).
        // While the filename is in the cache, we will no longer create that
        // symlink on-the-fly in subsequent webdav requests. It essentially
        // mimics a deletion even though there was nothing to delete in the first
        // place, since everything is created on the fly, mirroring the '/content'
        // directory.
        //
        // (204 No Content) is the correct status code to return for a successful
        // deletion of a file. This status code means the server has successfully
        // processed the request, and there is no additional content to send in the
        // response body. (200 OK) is also acceptable, but more appropriate for when
        // the server also returns a response body with the status of the operation.
        DeletedFiles.AddDeletedFile(request.Name, TimeSpan.FromSeconds(30));
        return Task.FromResult(DavStatusCode.NoContent);
    }

    private IStoreItem GetItem(DavItem davItem)
    {
        return davItem.Type switch
        {
            DavItem.ItemType.Directory =>
                new DatabaseStoreSymlinkCollection(davItem, dbClient, configManager),
            DavItem.ItemType.NzbFile =>
                new DatabaseStoreSymlinkFile(davItem, configManager),
            DavItem.ItemType.RarFile =>
                new DatabaseStoreSymlinkFile(davItem, configManager),
            DavItem.ItemType.MultipartFile =>
                new DatabaseStoreSymlinkFile(davItem, configManager),
            _ => throw new ArgumentException("Unrecognized directory child type.")
        };
    }

    private class DeletedFileManager(Guid directoryId)
    {
        private static readonly MemoryCache DeletedFiles = new(new MemoryCacheOptions());

        public void AddDeletedFile(string filename, TimeSpan? expiry = null)
        {
            using var entry = DeletedFiles.CreateEntry(GetKey(filename));
            entry.SlidingExpiration = expiry ?? TimeSpan.FromSeconds(30);
            entry.Value = true;
        }

        public bool IsDeleted(string filename)
        {
            return (bool)(DeletedFiles.Get(GetKey(filename)) ?? false);
        }

        private string GetKey(string filename)
        {
            return $"{directoryId}/{filename}";
        }
    }
}