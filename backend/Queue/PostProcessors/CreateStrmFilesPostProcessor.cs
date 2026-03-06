using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Queue.PostProcessors;

public class CreateStrmFilesPostProcessor(ConfigManager configManager, DavDatabaseClient dbClient)
{
    public async Task CreateStrmFilesAsync()
    {
        // Add strm files to the download dir
        var videoItems = dbClient.Ctx.ChangeTracker.Entries<DavItem>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .Where(x => x.Type != DavItem.ItemType.Directory)
            .Where(x => FilenameUtil.IsVideoFile(x.Name));
        foreach (var videoItem in videoItems)
            await StrmFileUtil.CreateStrmFileAsync(configManager, videoItem).ConfigureAwait(false);
    }
}