using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using Serilog;

namespace NzbWebDAV.Services;

public class DavCleanupService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var dbContext = new DavDatabaseContext();

                // Get the first item from the queue
                var cleanupItem = await dbContext.DavCleanupItems
                    .FirstOrDefaultAsync(stoppingToken)
                    .ConfigureAwait(false);

                // If no items in queue, wait 10 seconds before checking again
                if (cleanupItem == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // Collect children to delete for vfs/forget
                var deletedItems = await dbContext.Items
                    .Where(x => x.ParentId == cleanupItem.Id)
                    .Select(x => new DavItem { Id = x.Id, Type = x.Type, Path = x.Path })
                    .ToListAsync(stoppingToken);

                // Delete any children
                await dbContext.Items
                    .Where(x => x.ParentId == cleanupItem.Id)
                    .ExecuteDeleteAsync(stoppingToken);

                // Trigger rclone vfs/forget for deleted children
                _ = DavDatabaseContext.RcloneVfsForget(deletedItems);

                // Remove the queue item from database
                dbContext.DavCleanupItems.Remove(cleanupItem);
                await dbContext.SaveChangesAsync(stoppingToken).ConfigureAwait(false);

                // Continue immediately to next iteration to process more items
            }
            catch (Exception e)
            {
                Log.Error(e, $"Error processing dav cleanup queue: {e.Message}");

                // Wait 10 seconds before continuing on exception
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
