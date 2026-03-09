using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NzbWebDAV.Database;
using Serilog;

namespace NzbWebDAV.Services;

/// <summary>
/// Background service that processes the NZB blob cleanup queue.
/// An NZB blob is only deleted once it is no longer referenced by any
/// QueueItem, HistoryItem, or DavItem.
/// </summary>
public class NzbBlobCleanupService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var dbContext = new DavDatabaseContext();

                // Get the first item from the queue
                var cleanupItem = await dbContext.NzbBlobCleanupItems
                    .FirstOrDefaultAsync(stoppingToken)
                    .ConfigureAwait(false);

                // If no items in queue, wait 10 seconds before checking again
                if (cleanupItem == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var blobId = cleanupItem.Id;

                // Use a serializable (BEGIN IMMEDIATE) transaction so that the three
                // reference checks and the removal of the cleanup item are atomic.
                // Without this, a concurrent HistoryItem/DavItem deletion could:
                //   1. occur between our reference checks (making one check stale), and
                //   2. have its trigger INSERT OR IGNORE suppressed because our cleanup
                //      item is still in the table, permanently orphaning the blob.
                // With BEGIN IMMEDIATE, concurrent writers are blocked until we commit.
                // After commit, the cleanup item is gone, so any trigger that fires
                // will successfully insert a new item for the next service pass.
                await using var tx = await dbContext.Database
                    .BeginTransactionAsync(IsolationLevel.Serializable, stoppingToken)
                    .ConfigureAwait(false);

                // Only delete the blob if it is no longer referenced anywhere.
                // QueueItem.Id IS the NZB blob ID (the blob is stored at that GUID).
                var isReferencedByQueue = await dbContext.QueueItems
                    .AnyAsync(x => x.Id == blobId, stoppingToken)
                    .ConfigureAwait(false);

                var isReferencedByHistory = await dbContext.HistoryItems
                    .AnyAsync(x => x.NzbBlobId == blobId, stoppingToken)
                    .ConfigureAwait(false);

                var isReferencedByDavItems = await dbContext.Items
                    .AnyAsync(x => x.NzbBlobId == blobId, stoppingToken)
                    .ConfigureAwait(false);

                if (!isReferencedByQueue && !isReferencedByHistory && !isReferencedByDavItems)
                {
                    // Delete the blob before SaveChangesAsync so that if SaveChangesAsync
                    // fails, the cleanup item remains in the DB and the service retries.
                    // On retry, BlobStore.Delete succeeds even if the file is already gone.
                    BlobStore.Delete(blobId);

                    var nzbName = await dbContext.NzbNames.FindAsync([blobId], stoppingToken).ConfigureAwait(false);
                    if (nzbName != null)
                        dbContext.NzbNames.Remove(nzbName);
                }

                // Remove the cleanup queue item and commit.
                dbContext.NzbBlobCleanupItems.Remove(cleanupItem);
                await dbContext.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
                await tx.CommitAsync(stoppingToken).ConfigureAwait(false);

                // Continue immediately to next iteration to process more items
            }
            catch (Exception e)
            {
                Log.Error(e, $"Error processing NZB blob cleanup queue: {e.Message}");

                // Wait 10 seconds before continuing on exception
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
