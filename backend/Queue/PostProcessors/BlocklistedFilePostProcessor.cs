using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using Serilog;

namespace NzbWebDAV.Queue.PostProcessors;

public class BlocklistedFilePostProcessor(ConfigManager configManager, DavDatabaseClient dbClient)
{
    public void RemoveBlocklistedFiles()
    {
        var blocklistPatterns = configManager.GetBlocklistedFiles();
        var blocklistedFiles = dbClient.Ctx.ChangeTracker.Entries<DavItem>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .Where(x => x.Type != DavItem.ItemType.Directory)
            .Where(x => MatchesAnyPattern(x.Name, blocklistPatterns));

        foreach (var blocklistedFile in blocklistedFiles)
            RemoveBlocklistedFile(blocklistedFile);
    }

    public static bool MatchesAnyPattern(string fileName, HashSet<string> patterns)
    {
        var lowerFileName = fileName.ToLower();
        return patterns.Any(pattern => MatchesPattern(lowerFileName, pattern));
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        // Convert pattern to regex:
        // 1. Escape all regex special characters (this escapes * to \*)
        // 2. Replace \* with .* to support greedy wildcard matching
        var regexPattern = Regex.Escape(pattern).Replace("\\*", ".*");
        return Regex.IsMatch(fileName, $"^{regexPattern}$");
    }

    private void RemoveBlocklistedFile(DavItem davItem)
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
            Log.Error("Error filtering blocklisted files.");
            return;
        }

        dbClient.Ctx.Items.Remove(davItem);
    }
}
