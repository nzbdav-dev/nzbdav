using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.Tasks;

public class PurgeSampleFilesTask(
    ConfigManager configManager,
    DavDatabaseClient dbClient,
    WebsocketManager websocketManager,
    bool isDryRun) : BaseTask
{
    
    private static List<string> _allRemovedPaths = [];
    
    protected override async Task ExecuteInternal()
    {
        try
        {
            await PurgeSampleFiles().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Report($"Failed: {e.Message}");
            Log.Error(e, "Failed to purge sample files.");
        }
    }

    private async Task PurgeSampleFiles()
    {
        var removedItems = new HashSet<Guid>();
        var dryRun = isDryRun ? "Dry Run - " : string.Empty;
        
        // Find all dav items
        Report(dryRun + "Enumerating all webdav files...");
        var allDavItems = await dbClient.Ctx.Items.ToListAsync().ConfigureAwait(false);
        
        // Find all sample files
        Report(dryRun + "Enumerating all sample files...");
        var sampleItems = allDavItems.Where(x => x.Name.EndsWith("-sample.mkv")).ToList();
        ReportProgress(dryRun + $"Found {sampleItems.Count()} sample files.", sampleItems.Count());

        // Remove all sample files
        foreach (var sampleItem in sampleItems) 
            RemoveItem(sampleItem, removedItems);
        
        // save changes to database
        if (!isDryRun) await dbClient.Ctx.SaveChangesAsync().ConfigureAwait(false);
        
        _allRemovedPaths = allDavItems
            .Where(x => removedItems.Contains(x.Id))
            .Select(x => x.Path)
            .ToList();
        
        Report(!isDryRun 
            ? $"Done. Removed {_allRemovedPaths.Count} sample files." 
            : $"Done. The task would remove {_allRemovedPaths.Count} sample files.");

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
    
    private void Report(string message)
    {
        _ = websocketManager.SendMessage(WebsocketTopic.PurgeSampleTaskProgress, message);
    }

    private void ReportProgress(string message, int completedCount)
    {
        Report($"{message}\nConverted: {completedCount} strm file(s) to symlinks.");
    }
    
    public static string GetAuditReport()
    {
        return _allRemovedPaths.Count > 0
            ? string.Join("\n", _allRemovedPaths)
            : "This list is Empty.\nYou must first run the task.";
    }
    
}