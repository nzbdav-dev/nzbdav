using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Queue.PostProcessors;

public class SampleFilesPostProcessor(ConfigManager configManager, DavDatabaseClient dbClient)
    : AbstractFileRemovingPostProcessor(dbClient)
{
    protected override IEnumerable<DavItem> GetFilesToRemove()
    {
        if (!configManager.IsIgnoreSampleFilesEnabled())
            return [];

        return dbClient.Ctx.ChangeTracker.Entries<DavItem>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .Where(x => x.Type != DavItem.ItemType.Directory)
            .Where(x => x.Name.EndsWith("-sample.mkv", StringComparison.OrdinalIgnoreCase));
    }
}