using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using Serilog;

namespace NzbWebDAV.Queue.PostProcessors;

public class BlacklistedExtensionPostProcessor(ConfigManager configManager, DavDatabaseClient dbClient)
{
    public void RemoveBlacklistedExtensions()
    {
        var blacklistedExtensions = configManager.GetBlacklistedExtensions();
        var blacklistedFiles = dbClient.Ctx.ChangeTracker.Entries<DavItem>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .Where(x => x.Type != DavItem.ItemType.Directory)
            .Where(x => blacklistedExtensions.Contains(Path.GetExtension(x.Name).ToLower()));

        foreach (var blacklistedFile in blacklistedFiles)
            RemoveBlacklistedFile(blacklistedFile);
    }

    private void RemoveBlacklistedFile(DavItem davItem)
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
            Log.Error("Error filtering blacklisted extensions from downloading.");
            return;
        }

        dbClient.Ctx.Items.Remove(davItem);
    }
}