using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NzbWebDAV.Database;
using Serilog;

namespace NzbWebDAV.Services;

/// <summary>
/// Background service that processes the blob cleanup queue.
/// Continuously monitors BlobCleanupQueueItems table and deletes corresponding blobs.
/// </summary>
public class BlobCleanupService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var dbContext = new DavDatabaseContext();

                // Get the first item from the queue
                var cleanupItem = await dbContext.BlobCleanupItems
                    .FirstOrDefaultAsync(stoppingToken)
                    .ConfigureAwait(false);

                // If no items in queue, wait 10 seconds before checking again
                if (cleanupItem == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // Delete the blob
                BlobStore.Delete(cleanupItem.Id);

                // Remove the queue item from database
                dbContext.BlobCleanupItems.Remove(cleanupItem);
                await dbContext.SaveChangesAsync(stoppingToken).ConfigureAwait(false);

                // Continue immediately to next iteration to process more items
            }
            catch (Exception e)
            {
                Log.Error(e, $"Error processing blob cleanup queue: {e.Message}");

                // Wait 10 seconds before continuing on exception
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
