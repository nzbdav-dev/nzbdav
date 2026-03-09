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
    WebsocketManager websocketManager,
    bool isDryRun
) : BaseTask
{
    private static List<string> _allRemovedPaths = [];

    private record UnlinkedItemInfo(string Id, int Type, string Path);

    protected override async Task ExecuteInternal()
    {
        try
        {
            await RemoveUnlinkedFiles().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Report($"Failed: {e.Message}");
            Log.Error(e, "Failed to remove unlinked files.");
        }
    }

    private async Task RemoveUnlinkedFiles()
    {
        // get linked file paths
        Report("Scanning all linked files...");
        var startTime = DateTime.Now;
        var linkedIdCount = await WriteLinkedIdsToTable();
        if (linkedIdCount < 5)
        {
            Report($"Aborted: " +
                   $"There are less than five linked files found in your library. " +
                   $"Cancelling operation to prevent accidental bulk deletion.");
            return;
        }

        Report("Searching for unlinked webdav items...");
        var unlinkedItems = await CountUnlinkedItems(startTime);
        Report($"Found {unlinkedItems} webdav items to remove.");

        if (isDryRun)
        {
            await DryRunIdentifyUnlinkedFiles(startTime);
            Report($"Done. Identified {_allRemovedPaths.Count} unlinked files.");
        }
        else
        {
            await RemoveUnlinkedItems(startTime, unlinkedItems);
            await RemoveEmptyDirectories(startTime);
            Report($"Done. Removed {_allRemovedPaths.Count} unlinked files.");
        }
    }

    private async Task<int> WriteLinkedIdsToTable()
    {
        var dbContext = new DavDatabaseContext();

        // Create a new table "TMP_LINKED_FILES", dropping old one if it already exists.
        // No index initially for fast writes.
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DROP TABLE IF EXISTS TMP_LINKED_FILES;
            CREATE TABLE TMP_LINKED_FILES (Id TEXT NOT NULL);
            """);

        var count = 0;
        var batches = GetLinkedIds().ToBatches(100);
        foreach (var batch in batches)
        {
            var values = string.Join(",", batch.Select(id => $"('{id.ToString().ToUpper()}')"));
            await dbContext.Database.ExecuteSqlRawAsync(
                $"INSERT INTO TMP_LINKED_FILES (Id) VALUES {values}");
            count += batch.Count;
        }

        // Remove duplicates and add primary key index.
        // Create a new table with unique constraint, copy distinct values, then swap.
        Report($"Indexing {count} linked files...");
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE TMP_LINKED_FILES_UNIQUE (Id TEXT NOT NULL PRIMARY KEY);
            INSERT OR IGNORE INTO TMP_LINKED_FILES_UNIQUE (Id) SELECT Id FROM TMP_LINKED_FILES;
            DROP TABLE TMP_LINKED_FILES;
            ALTER TABLE TMP_LINKED_FILES_UNIQUE RENAME TO TMP_LINKED_FILES;
            """);

        return count;
    }

    private IEnumerable<Guid> GetLinkedIds()
    {
        var debounce = DebounceUtil.CreateDebounce(TimeSpan.FromMilliseconds(500));
        var linkedIds = OrganizedLinksUtil
            .GetLibraryDavItemLinks(configManager)
            .Select(x => x.DavItemId);

        var count = 0;
        foreach (var linkedId in linkedIds)
        {
            count++;
            debounce(() => Report($"Scanning all linked files...\nFound {count}..."));
            yield return linkedId;
        }

        Report($"Scanning all linked files...\nFound {count}...");
    }

    private async Task<int> CountUnlinkedItems(DateTime createdBefore)
    {
        var dbContext = new DavDatabaseContext();
        var createdBeforeStr = createdBefore.ToString("yyyy-MM-dd HH:mm:ss");
        var usenetFileType = (int)DavItem.ItemType.UsenetFile;

        var count = await dbContext.Database
            .SqlQueryRaw<int>(
                $"""
                 SELECT COUNT(i.Id) AS Value FROM DavItems i
                 LEFT JOIN TMP_LINKED_FILES t ON i.Id = t.Id
                 WHERE i.Type = {usenetFileType}
                   AND i.HistoryItemId IS NULL
                   AND i.CreatedAt < '{createdBeforeStr}'
                   AND t.Id IS NULL
                 """)
            .FirstAsync();

        return count;
    }

    private async Task RemoveUnlinkedItems(DateTime createdBefore, int totalCount)
    {
        Report("Removing unlinked items...");
        _allRemovedPaths.Clear();
        var dbContext = new DavDatabaseContext();
        var removed = 0;

        while (true)
        {
            // Select items to delete (batch of 100)
            var itemsToDelete = await dbContext.Database
                .SqlQueryRaw<UnlinkedItemInfo>(
                    $"""
                     SELECT Id, Type, Path FROM DavItems
                     WHERE Type = {(int)DavItem.ItemType.UsenetFile}
                       AND HistoryItemId IS NULL
                       AND CreatedAt < '{createdBefore:yyyy-MM-dd HH:mm:ss}'
                       AND Id NOT IN (SELECT Id FROM TMP_LINKED_FILES)
                     LIMIT 100
                     """)
                .ToListAsync();

            // If there are no more items to delete, we're done.
            if (itemsToDelete.Count == 0)
                break;

            // Delete the items.
            var idsToDelete = string.Join(",", itemsToDelete.Select(x => $"'{x.Id}'"));
            await dbContext.Database.ExecuteSqlRawAsync($"DELETE FROM DavItems WHERE Id IN ({idsToDelete})");

            // Trigger rclone vfs/forget for deleted items
            _ = DavDatabaseContext.RcloneVfsForget(itemsToDelete.Select(x => new DavItem
            {
                Id = Guid.Parse(x.Id),
                Type = (DavItem.ItemType)x.Type,
                Path = x.Path
            }).ToList());

            // Track removed paths
            _allRemovedPaths.AddRange(itemsToDelete.Select(x => x.Path));
            removed += itemsToDelete.Count;

            Report($"Removing unlinked items...\nRemoved {removed}/{totalCount}...");
        }

        Report($"Removing unlinked items...\nRemoved {removed} of {removed}...");
    }

    private async Task RemoveEmptyDirectories(DateTime createdBefore)
    {
        Report($"Removing empty directories...");
        var dbContext = new DavDatabaseContext();
        var removed = 0;

        while (true)
        {
            // Find empty directories (no children).
            // Only target regular directories (SubType = Directory), not root folders.
            var emptyDirs = await dbContext.Database
                .SqlQueryRaw<UnlinkedItemInfo>(
                    $"""
                     SELECT d.Id, d.Type, d.Path FROM DavItems d
                     LEFT JOIN DavItems c ON c.ParentId = d.Id
                     WHERE d.SubType = {(int)DavItem.ItemSubType.Directory}
                       AND d.CreatedAt < '{createdBefore:yyyy-MM-dd HH:mm:ss}'
                       AND c.Id IS NULL
                     LIMIT 100
                     """)
                .ToListAsync();

            if (emptyDirs.Count == 0)
                break;

            // Delete the empty directories.
            var idsToDelete = string.Join(",", emptyDirs.Select(x => $"'{x.Id}'"));
            await dbContext.Database.ExecuteSqlRawAsync($"DELETE FROM DavItems WHERE Id IN ({idsToDelete})");

            // Trigger rclone vfs/forget for deleted directories
            _ = DavDatabaseContext.RcloneVfsForget(emptyDirs.Select(x => new DavItem
            {
                Id = Guid.Parse(x.Id),
                Type = (DavItem.ItemType)x.Type,
                Path = x.Path
            }).ToList());

            removed += emptyDirs.Count;
            Report($"Removing empty directories...\nRemoved {removed}...");
        }
    }

    private async Task DryRunIdentifyUnlinkedFiles(DateTime createdBefore)
    {
        var dbContext = new DavDatabaseContext();
        var unlinkedFiles = await dbContext.Database
            .SqlQueryRaw<UnlinkedItemInfo>(
                $"""
                 SELECT Id, Type, Path FROM DavItems
                 WHERE Type = {(int)DavItem.ItemType.UsenetFile}
                   AND HistoryItemId IS NULL
                   AND CreatedAt < '{createdBefore:yyyy-MM-dd HH:mm:ss}'
                   AND Id NOT IN (SELECT Id FROM TMP_LINKED_FILES)
                 """)
            .ToListAsync();

        _allRemovedPaths = unlinkedFiles.Select(x => x.Path).ToList();
    }

    private void Report(string message)
    {
        var dryRun = isDryRun ? "Dry Run - " : string.Empty;
        _ = websocketManager.SendMessage(WebsocketTopic.CleanupTaskProgress, $"{dryRun}{message}");
    }

    public static string GetAuditReport()
    {
        return _allRemovedPaths.Count > 0
            ? string.Join("\n", _allRemovedPaths)
            : "This list is Empty.\nYou must first run the task.";
    }
}