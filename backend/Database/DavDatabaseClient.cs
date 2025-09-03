using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Database;

public sealed class DavDatabaseClient(DavDatabaseContext ctx)
{
    public DavDatabaseContext Ctx => ctx;

    // file
    public Task<DavItem?> GetFileById(string id)
    {
        var guid = Guid.Parse(id);
        return ctx.Items.Where(i => i.Id == guid).FirstOrDefaultAsync();
    }

    public Task<List<DavItem>> GetFilesByIdPrefix(string prefix)
    {
        return ctx.Items
            .Where(i => i.IdPrefix == prefix)
            .Where(i => i.Type == DavItem.ItemType.NzbFile || i.Type == DavItem.ItemType.RarFile)
            .ToListAsync();
    }

    // directory
    public Task<List<DavItem>> GetDirectoryChildrenAsync(Guid dirId, CancellationToken ct = default)
    {
        return ctx.Items.Where(x => x.ParentId == dirId).ToListAsync(ct);
    }

    public Task<DavItem?> GetDirectoryChildAsync(Guid dirId, string childName, CancellationToken ct = default)
    {
        return ctx.Items.FirstOrDefaultAsync(x => x.ParentId == dirId && x.Name == childName, ct);
    }

    public async Task<long> GetRecursiveSize(Guid dirId, CancellationToken ct = default)
    {
        if (dirId == DavItem.Root.Id)
        {
            return await Ctx.Items.SumAsync(x => x.FileSize, ct) ?? 0;
        }

        const string sql = @"
            WITH RECURSIVE RecursiveChildren AS (
                SELECT Id, FileSize
                FROM DavItems
                WHERE ParentId = @parentId

                UNION ALL

                SELECT d.Id, d.FileSize
                FROM DavItems d
                INNER JOIN RecursiveChildren rc ON d.ParentId = rc.Id
            )
            SELECT IFNULL(SUM(FileSize), 0)
            FROM RecursiveChildren;
        ";
        var connection = Ctx.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@parentId";
        parameter.Value = dirId;
        command.Parameters.Add(parameter);
        var result = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    // nzbfile
    public async Task<DavNzbFile?> GetNzbFileAsync(Guid id, CancellationToken ct = default)
    {
        return await ctx.NzbFiles.FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    // queue
    public Task<QueueItem?> GetTopQueueItem(CancellationToken ct = default)
    {
        var nowTime = DateTime.Now;
        return Ctx.QueueItems
            .OrderByDescending(q => q.Priority)
            .ThenBy(q => q.CreatedAt)
            .Where(q => q.PauseUntil == null || nowTime >= q.PauseUntil)
            .Skip(0)
            .Take(1)
            .FirstOrDefaultAsync(ct);
    }

    public Task<QueueItem[]> GetQueueItems
    (
        string? category,
        int start = 0,
        int limit = int.MaxValue,
        CancellationToken ct = default
    )
    {
        return Ctx.QueueItems
            .Where(q => q.Category == category || category == null)
            .OrderByDescending(q => q.Priority)
            .ThenBy(q => q.CreatedAt)
            .Skip(start)
            .Take(limit)
            .Select(q => new QueueItem()
            {
                Id = q.Id,
                CreatedAt = q.CreatedAt,
                FileName = q.FileName,
                NzbContents = null!,
                NzbFileSize = q.NzbFileSize,
                TotalSegmentBytes = q.TotalSegmentBytes,
                Category = q.Category,
                Priority = q.Priority,
                PostProcessing = q.PostProcessing,
            })
            .ToArrayAsync(cancellationToken: ct);
    }

    public async Task RemoveQueueItemAsync(string id)
    {
        try
        {
            Ctx.QueueItems.Remove(new QueueItem() { Id = Guid.Parse(id) });
            await Ctx.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException e)
        {
            var ignoredErrorMessage = "expected to affect 1 row(s), but actually affected 0 row(s)";
            if (!e.Message.Contains(ignoredErrorMessage)) throw;
        }
    }

    // history
    public async Task<HistoryItem?> GetHistoryItemAsync(string id)
    {
        return await Ctx.HistoryItems.FirstOrDefaultAsync(x => x.Id == Guid.Parse(id));
    }

    public async Task RemoveHistoryItemsAsync(List<Guid> ids, bool deleteFiles, CancellationToken ct = default)
    {
        if (deleteFiles)
        {
            await Ctx.Items
                .Where(d => Ctx.HistoryItems
                    .Where(h => ids.Contains(h.Id) && h.DownloadDirId != null)
                    .Select(h => h.DownloadDirId!)
                    .Contains(d.Id))
                .ExecuteDeleteAsync(ct);
        }

        await Ctx.HistoryItems
            .Where(x => ids.Contains(x.Id))
            .ExecuteDeleteAsync(ct);
    }

    private class FileSizeResult
    {
        public long TotalSize { get; init; }
    }

    // completed-symlinks
    public async Task<List<DavItem>> GetCompletedSymlinkCategoryChildren(string category, CancellationToken ct = default)
    {
        var query = from historyItem in Ctx.HistoryItems
            where historyItem.Category == category
                  && historyItem.DownloadStatus == HistoryItem.DownloadStatusOption.Completed
                  && historyItem.DownloadDirId != null
            join davItem in Ctx.Items on historyItem.DownloadDirId equals davItem.Id
            where davItem.Type == DavItem.ItemType.Directory
            select davItem;
        return await query.Distinct().ToListAsync(ct);
    }
}