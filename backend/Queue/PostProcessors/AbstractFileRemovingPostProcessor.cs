using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using Serilog;

namespace NzbWebDAV.Queue.PostProcessors;

// Implements the required logic to remove files 
public abstract class AbstractFileRemovingPostProcessor(DavDatabaseClient dbClient)
{
    protected abstract IEnumerable<DavItem> GetFilesToRemove();

    public void RemoveFiles()
    {
        foreach (var file in GetFilesToRemove())
            RemoveFile(file);
    }

    private void RemoveFile(DavItem davItem)
    {
        if (davItem.Type == DavItem.ItemType.NzbFile)
        {
            var file = dbClient.Ctx.ChangeTracker.Entries<DavNzbFile>()
                .Where(x => x.State == EntityState.Added)
                .Select(x => x.Entity)
                .First(x => x.Id == davItem.Id);
            dbClient.Ctx.NzbFiles.Remove(file);
        }

        else if (davItem.Type == DavItem.ItemType.RarFile)
        {
            var file = dbClient.Ctx.ChangeTracker.Entries<DavRarFile>()
                .Where(x => x.State == EntityState.Added)
                .Select(x => x.Entity)
                .First(x => x.Id == davItem.Id);
            dbClient.Ctx.RarFiles.Remove(file);
        }

        else if (davItem.Type == DavItem.ItemType.MultipartFile)
        {
            var file = dbClient.Ctx.ChangeTracker.Entries<DavMultipartFile>()
                .Where(x => x.State == EntityState.Added)
                .Select(x => x.Entity)
                .First(x => x.Id == davItem.Id);
            dbClient.Ctx.MultipartFiles.Remove(file);
        }

        else
        {
            Log.Error("Error removing file from database.");
            return;
        }

        dbClient.Ctx.Items.Remove(davItem);
    }
}
