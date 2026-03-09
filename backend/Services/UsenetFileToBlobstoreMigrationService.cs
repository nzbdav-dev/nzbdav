using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.Services;

/// <summary>
/// Background service that migrates usenet file data
/// from the sqlite database to the blob-store.
/// </summary>
public class UsenetFileToBlobstoreMigrationService(WebsocketManager websocketManager) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Report("Determining number of files to migrate...");
        var initialRemaining = await GetTotalCountLeft(stoppingToken);
        var totalRemaining = initialRemaining;
        ReportProgress(totalRemaining, initialRemaining);
        totalRemaining = await MigrateNzbFiles(totalRemaining, initialRemaining, stoppingToken);
        totalRemaining = await MigrateRarFiles(totalRemaining, initialRemaining, stoppingToken);
        totalRemaining = await MigrateMultipartFiles(totalRemaining, initialRemaining, stoppingToken);
        var complete = initialRemaining - totalRemaining;
        Report(complete == 0
            ? $"Done! Nothing to migrate."
            : $"Done! Migrated {complete}/{initialRemaining} file(s) to the blob-store.");
    }

    private async Task<int> GetTotalCountLeft(CancellationToken ct)
    {
        await using var dbContext = new DavDatabaseContext();
        return await dbContext.NzbFiles.CountAsync(ct) +
               await dbContext.RarFiles.CountAsync(ct) +
               await dbContext.MultipartFiles.CountAsync(ct);
    }

    private Task<int> MigrateNzbFiles(int totalRemaining, int initialRemaining, CancellationToken ct)
    {
        return MigrateUsenetFiles<DavNzbFile>(
            (dbContext) => dbContext.NzbFiles.FirstOrDefaultAsync(ct),
            (file) => file.Id,
            (file) => file.Id = Guid.NewGuid(),
            (dbContext, id) => dbContext.NzbFiles.Remove(new DavNzbFile { Id = id }),
            totalRemaining,
            initialRemaining,
            ct
        );
    }

    private Task<int> MigrateRarFiles(int totalRemaining, int initialRemaining, CancellationToken ct)
    {
        return MigrateUsenetFiles<DavRarFile>(
            (dbContext) => dbContext.RarFiles.FirstOrDefaultAsync(ct),
            (file) => file.Id,
            (file) => file.Id = Guid.NewGuid(),
            (dbContext, id) => dbContext.RarFiles.Remove(new DavRarFile { Id = id }),
            totalRemaining,
            initialRemaining,
            ct
        );
    }

    private Task<int> MigrateMultipartFiles(int totalRemaining, int initialRemaining, CancellationToken ct)
    {
        return MigrateUsenetFiles<DavMultipartFile>(
            (dbContext) => dbContext.MultipartFiles.FirstOrDefaultAsync(ct),
            (file) => file.Id,
            (file) => file.Id = Guid.NewGuid(),
            (dbContext, id) => dbContext.MultipartFiles.Remove(new DavMultipartFile { Id = id }),
            totalRemaining,
            initialRemaining,
            ct
        );
    }

    private async Task<int> MigrateUsenetFiles<T>(
        Func<DavDatabaseContext, Task<T?>> getFileToMigrate,
        Func<T, Guid> getFileToMigrateId,
        Action<T> setFileToMigrateNewId,
        Action<DavDatabaseContext, Guid> removeFileToMigrateFromDb,
        int totalRemaining,
        int initialRemaining,
        CancellationToken ct
    )
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var dbContext = new DavDatabaseContext();
                var fileToMigrate = await getFileToMigrate(dbContext);
                if (fileToMigrate == null) return totalRemaining;
                var davItem = await GetDavItem(getFileToMigrateId(fileToMigrate), dbContext, ct);
                dbContext.Entry(fileToMigrate).State = EntityState.Detached;
                setFileToMigrateNewId(fileToMigrate);
                await BlobStore.WriteBlob(getFileToMigrateId(fileToMigrate), fileToMigrate);
                try
                {
                    // database changes
                    davItem.FileBlobId = getFileToMigrateId(fileToMigrate);
                    removeFileToMigrateFromDb(dbContext, davItem.Id);
                    await dbContext.SaveChangesAsync(ct);
                    totalRemaining--;
                    ReportProgress(totalRemaining, initialRemaining);
                }
                catch
                {
                    BlobStore.Delete(getFileToMigrateId(fileToMigrate));
                    throw;
                }
            }
            catch (Exception e)
            {
                Log.Error(e, $"Error migrating usenet-file to blob-store: {e.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            }
        }

        return totalRemaining;
    }

    private static async Task<DavItem> GetDavItem(Guid id, DavDatabaseContext dbContext, CancellationToken ct)
    {
        return (await dbContext.Items.Where(x => x.Id == id).FirstOrDefaultAsync(ct))
               ?? throw new Exception($"DavItem with id `{id}` not found");
    }

    private void Report(string message)
    {
        _ = websocketManager.SendMessage(WebsocketTopic.UsenetFileToBlobstoreMigrationProgress, message);
    }

    private void ReportProgress(int totalRemaining, int initialRemaining)
    {
        var complete = initialRemaining - totalRemaining;
        Report($"Migrated {complete}/{initialRemaining} file(s) to the blob-store.");
    }
}