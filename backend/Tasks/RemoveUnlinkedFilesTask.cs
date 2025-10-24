using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.Tasks;

public class RemoveUnlinkedFilesTask(
    ConfigManager configManager,
    DavDatabaseClient dbClient,
    WebsocketManager websocketManager,
    bool isDryRun
) : BaseTask
{
    private static List<string> _allRemovedPaths = [];

    protected override async Task ExecuteInternal()
    {
        try
        {
            await RemoveUnlinkedFiles();
        }
        catch (Exception e)
        {
            Report($"Failed: {e.Message}");
            Log.Error(e, "Failed to remove unlinked files.");
        }
    }

    private async Task RemoveUnlinkedFiles()
    {
        var removedItems = new HashSet<Guid>();
        var dryRun = isDryRun ? "Dry Run - " : string.Empty;

        Report(dryRun + "Enumerating all webdav files...");
        var allDavItems = await dbClient.Ctx.Items.ToListAsync();

        // get linked file paths
        Report(dryRun + $"Found {allDavItems.Count} webdav files.\nEnumerating all symlinked files...");
        var linkedIds = GetLinkedIds();
        if (linkedIds.Count < 100)
        {
            Report($"Aborted: " +
                   $"There are less than one hundred symlinked files found in your library. " +
                   $"Cancelling operation to prevent accidental bulk deletion.");
            return;
        }

        // determine paths to delete
        // only delete paths that have existed longer than a day
        var minExistance = TimeSpan.FromDays(1);
        var dateThreshold = DateTime.Now.Subtract(minExistance);
        var allEmptyDirectories = allDavItems
            .Where(x => x.Type == DavItem.ItemType.Directory)
            .Where(x => x.CreatedAt < dateThreshold)
            .Where(x => x.Children.All(y => removedItems.Contains(y.Id)));
        var allUnlinkedFiles = allDavItems
            .Where(x => x.Type
                is DavItem.ItemType.NzbFile
                or DavItem.ItemType.RarFile
                or DavItem.ItemType.MultipartFile)
            .Where(x => x.CreatedAt < dateThreshold)
            .Where(x => !linkedIds.Contains(x.Id));

        // remove all empty directories
        Report(dryRun + "Removing all empty directories...");
        foreach (var emptyDirectory in allEmptyDirectories)
            RemoveItem(emptyDirectory, removedItems);

        // remove all unlinked files
        Report(dryRun + "Removing all unlinked files...");
        foreach (var unlinkedFile in allUnlinkedFiles)
            RemoveItem(unlinkedFile, removedItems);

        // save changes to database
        if (!isDryRun) await dbClient.Ctx.SaveChangesAsync();

        // return all removed paths
        _allRemovedPaths = allDavItems
            .Where(x => removedItems.Contains(x.Id))
            .Select(x => x.Path)
            .ToList();

        Report(!isDryRun
            ? $"Done. Removed {_allRemovedPaths.Count} orphaned items."
            : $"Done. The task would remove {_allRemovedPaths.Count} orphaned items.");
    }

    private void RemoveItem(DavItem item, HashSet<Guid> removedItems)
    {
        // ignore protected folders
        if (item.IsProtected()) return;

        // ignore already removed items
        if (removedItems.Contains(item.Id)) return;

        // remove the item
        if (!isDryRun) dbClient.Ctx.Items.Remove(item);
        removedItems.Add(item.Id);

        // remove the parent directory, if it is empty.
        if (item.Parent!.Children.All(x => removedItems.Contains(x.Id)))
            RemoveItem(item.Parent!, removedItems);
    }

    private HashSet<Guid> GetLinkedIds()
    {
        return OrganizedSymlinksUtil.GetLibrarySymlinkTargets(configManager)
            .Select(x => x.Target)
            .ToHashSet();
    }

    private void Report(string message)
    {
        _ = websocketManager.SendMessage(WebsocketTopic.CleanupTaskProgress, message);
    }

    public static string GetAuditReport()
    {
        return _allRemovedPaths.Count > 0
            ? string.Join("\n", _allRemovedPaths)
            : "This list is Empty.\nYou must first run the task.";
    }
}