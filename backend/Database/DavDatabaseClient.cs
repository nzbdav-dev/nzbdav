﻿using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Database;

public sealed class DavDatabaseClient(DavDatabaseContext ctx)
{
    public DavDatabaseContext Ctx => ctx;

    // directory
    public Task<List<DavItem>> GetDirectoryChildrenAsync(Guid dirId, CancellationToken ct = default)
    {
        return ctx.Items.Where(x => x.ParentId == dirId).ToListAsync(ct);
    }
    
    public async Task<Dictionary<Guid, DavNzbFile>> GetNzbFilesBatchAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        return await ctx.NzbFiles
            .Where(f => ids.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct);
    }
    
    public async Task<Dictionary<Guid, DavRarFile>> GetRarFilesBatchAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        return await ctx.RarFiles
            .Where(f => ids.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct);
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
            .Where(q => q.PauseUntil == null || DateTime.Now >= q.PauseUntil)
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
            var queueId = Guid.Parse(id);
            var existingItem = await Ctx.QueueItems.FirstOrDefaultAsync(q => q.Id == queueId);
            if (existingItem != null)
            {
                Ctx.QueueItems.Remove(existingItem);
                await Ctx.SaveChangesAsync();
            }
        }
        catch (DbUpdateConcurrencyException e)
        {
            var ignoredErrorMessage = "expected to affect 1 row(s), but actually affected 0 row(s)";
            if (!e.Message.Contains(ignoredErrorMessage)) throw;
        }
    }

    // history
    public async Task RemoveHistoryItemAsync(string id)
    {
        try
        {
            var historyId = Guid.Parse(id);
            var existingItem = await Ctx.HistoryItems.FirstOrDefaultAsync(h => h.Id == historyId);
            if (existingItem != null)
            {
                Ctx.HistoryItems.Remove(existingItem);
                await Ctx.SaveChangesAsync();
            }
        }
        catch (DbUpdateConcurrencyException e)
        {
            var ignoredErrorMessage = "expected to affect 1 row(s), but actually affected 0 row(s)";
            if (!e.Message.Contains(ignoredErrorMessage)) throw;
        }
    }

    private class FileSizeResult
    {
        public long TotalSize { get; init; }
    }
}