using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Utils;
using Serilog;

namespace NzbWebDAV.Database;

public static class DatabaseMaintenance
{
    private const string CompressionVersionKey = "database.payload-format-version";
    private const int CompressionVersion = 2;
    private const int BatchSize = 250;
    private const int ProgressLogInterval = 1000;

    private static readonly object AutoVacuumLock = new();
    private static bool _autoVacuumConfigured;

    public static bool EnsureDataDirectory()
    {
        var directory = Path.GetDirectoryName(DavDatabaseContext.DatabaseFilePath);
        if (string.IsNullOrWhiteSpace(directory)) return true;

        if (Directory.Exists(directory)) return true;

        try
        {
            Directory.CreateDirectory(directory);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not create database directory '{Directory}'. Database path may be on a read-only filesystem.", directory);
            return false;
        }
    }

    public static void EnsureAutoVacuumConfigured()
    {
        if (_autoVacuumConfigured) return;
        lock (AutoVacuumLock)
        {
            if (_autoVacuumConfigured) return;
            if (!EnsureDataDirectory())
            {
                Log.Warning("Database directory is not writable; skipping auto_vacuum configuration.");
                _autoVacuumConfigured = true;
                return;
            }

            try
            {
                using var connection = new SqliteConnection($"Data Source={DavDatabaseContext.DatabaseFilePath}");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA auto_vacuum;";
                var current = Convert.ToInt32(command.ExecuteScalar());
                if (current != 2)
                {
                    command.CommandText = "PRAGMA auto_vacuum = FULL;";
                    command.ExecuteNonQuery();
                    command.CommandText = "VACUUM;";
                    command.ExecuteNonQuery();
                    Log.Information("Enabled SQLite auto_vacuum=FULL and ran VACUUM to rebuild the database file.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not configure SQLite auto_vacuum; database file may be inaccessible or read-only.");
            }
            _autoVacuumConfigured = true;
        }
    }

    public static async Task RunRetentionAsync(DavDatabaseContext ctx, ConfigManager configManager, CancellationToken ct)
    {
        var historyDays = configManager.GetHistoryRetentionDays();
        var healthDays = configManager.GetHealthResultRetentionDays();
        var deletedRows = 0;

        if (historyDays > 0)
        {
            var historyCutoff = DateTime.UtcNow.AddDays(-historyDays);
            deletedRows += await ctx.HistoryItems
                .Where(x => x.CreatedAt < historyCutoff)
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);
        }

        if (healthDays > 0)
        {
            var healthCutoff = DateTimeOffset.UtcNow.AddDays(-healthDays);
            deletedRows += await ctx.HealthCheckResults
                .Where(x => x.CreatedAt < healthCutoff)
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);
        }

        if (deletedRows > 0)
        {
            Log.Information("Database retention removed {DeletedRows} rows; running VACUUM to reclaim space.", deletedRows);
            await VacuumAsync(null, ct).ConfigureAwait(false);
        }
    }

    public static async Task EnsureCompressedPayloadsAsync(DavDatabaseContext ctx, CancellationToken ct)
    {
        var currentVersion = await ctx.ConfigItems
            .Where(x => x.ConfigName == CompressionVersionKey)
            .Select(x => x.ConfigValue)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (currentVersion == CompressionVersion.ToString()) return;

        Log.Information("Normalizing legacy payloads to compressed storage format (this runs once).");
        var totalRewritten = 0;
        totalRewritten += await RewriteNzbFilesAsync(ctx, ct).ConfigureAwait(false);
        totalRewritten += await RewriteRarFilesAsync(ctx, ct).ConfigureAwait(false);
        totalRewritten += await RewriteMultipartFilesAsync(ctx, ct).ConfigureAwait(false);

        if (totalRewritten > 0)
        {
            await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
            await VacuumAsync(null, ct).ConfigureAwait(false);
            Log.Information("Rewrote {TotalRewritten} payload rows using compressed storage.", totalRewritten);
        }

        var versionItem = await ctx.ConfigItems
            .FirstOrDefaultAsync(x => x.ConfigName == CompressionVersionKey, ct)
            .ConfigureAwait(false);
        if (versionItem == null)
        {
            versionItem = new ConfigItem
            {
                ConfigName = CompressionVersionKey,
                ConfigValue = CompressionVersion.ToString()
            };
            ctx.ConfigItems.Add(versionItem);
        }
        else
        {
            versionItem.ConfigValue = CompressionVersion.ToString();
        }
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public static async Task VacuumAsync(string? vacuumIntoPath, CancellationToken ct)
    {
        if (!EnsureDataDirectory())
        {
            Log.Warning("Skipping VACUUM because the database directory is not writable.");
            return;
        }

        try
        {
            await using var connection = new SqliteConnection($"Data Source={DavDatabaseContext.DatabaseFilePath}");
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            if (!string.IsNullOrWhiteSpace(vacuumIntoPath))
            {
                var vacuumDir = Path.GetDirectoryName(vacuumIntoPath);
                if (!string.IsNullOrWhiteSpace(vacuumDir)) Directory.CreateDirectory(vacuumDir);
                command.CommandText = "VACUUM INTO $vacuumPath;";
                var param = command.CreateParameter();
                param.ParameterName = "$vacuumPath";
                param.Value = vacuumIntoPath!;
                command.Parameters.Add(param);
            }
            else
            {
                command.CommandText = "VACUUM;";
            }
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(vacuumIntoPath))
            {
                Log.Information("VACUUM completed into {VacuumPath}. You may replace the original database with this file manually.", vacuumIntoPath);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "VACUUM skipped because the database file could not be opened.");
        }
    }

    private static async Task<int> RewriteNzbFilesAsync(DavDatabaseContext ctx, CancellationToken ct)
    {
        var total = await ctx.NzbFiles.CountAsync(ct).ConfigureAwait(false);
        if (total == 0)
        {
            Log.Information("DavNzbFiles payloads already normalized; no rows to rewrite.");
            return 0;
        }

        Log.Information("Rewriting DavNzbFiles payloads ({Total} rows).", total);

        var rewritten = 0;
        var lastLoggedProgress = 0;
        Guid? lastId = null;
        while (true)
        {
            var query = ctx.NzbFiles.AsNoTracking();
            if (lastId.HasValue)
            {
                query = query.Where(x => x.Id.CompareTo(lastId.Value) > 0);
            }

            var batch = await query
                .OrderBy(x => x.Id)
                .Take(BatchSize)
                .Select(x => new
                {
                    x.Id,
                    x.SegmentIds
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);
            if (batch.Count == 0) break;
            lastId = batch.Last().Id;
            foreach (var item in batch)
            {
                var entity = new DavNzbFile
                {
                    Id = item.Id,
                    SegmentIds = item.SegmentIds
                };
                ctx.NzbFiles.Attach(entity);
                ctx.Entry(entity).Property(x => x.SegmentIds).IsModified = true;
            }

            rewritten += batch.Count;
            await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
            ctx.ChangeTracker.Clear();

            if (ShouldLogProgress(rewritten, total, lastLoggedProgress))
            {
                LogRewriteProgress("DavNzbFiles", rewritten, total);
                lastLoggedProgress = rewritten;
            }
        }

        if (rewritten != lastLoggedProgress)
        {
            LogRewriteProgress("DavNzbFiles", rewritten, total);
        }

        return rewritten;
    }
    private static async Task<int> RewriteRarFilesAsync(DavDatabaseContext ctx, CancellationToken ct)
    {
        var total = await ctx.RarFiles.CountAsync(ct).ConfigureAwait(false);
        if (total == 0)
        {
            Log.Information("DavRarFiles payloads already normalized; no rows to rewrite.");
            return 0;
        }

        Log.Information("Rewriting DavRarFiles payloads ({Total} rows).", total);

        var rewritten = 0;
        var lastLoggedProgress = 0;
        Guid? lastId = null;
        while (true)
        {
            var query = ctx.RarFiles.AsNoTracking();
            if (lastId.HasValue)
            {
                query = query.Where(x => x.Id.CompareTo(lastId.Value) > 0);
            }

            var batch = await query
                .OrderBy(x => x.Id)
                .Take(BatchSize)
                .Select(x => new
                {
                    x.Id,
                    x.RarParts
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);
            if (batch.Count == 0) break;
            lastId = batch.Last().Id;
            foreach (var item in batch)
            {
                var entity = new DavRarFile
                {
                    Id = item.Id,
                    RarParts = item.RarParts
                };
                ctx.RarFiles.Attach(entity);
                ctx.Entry(entity).Property(x => x.RarParts).IsModified = true;
            }

            rewritten += batch.Count;
            await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
            ctx.ChangeTracker.Clear();

            if (ShouldLogProgress(rewritten, total, lastLoggedProgress))
            {
                LogRewriteProgress("DavRarFiles", rewritten, total);
                lastLoggedProgress = rewritten;
            }
        }

        if (rewritten != lastLoggedProgress)
        {
            LogRewriteProgress("DavRarFiles", rewritten, total);
        }

        return rewritten;
    }

    private static async Task<int> RewriteMultipartFilesAsync(DavDatabaseContext ctx, CancellationToken ct)
    {
        var total = await ctx.MultipartFiles.CountAsync(ct).ConfigureAwait(false);
        if (total == 0)
        {
            Log.Information("DavMultipartFiles payloads already normalized; no rows to rewrite.");
            return 0;
        }

        Log.Information("Rewriting DavMultipartFiles payloads ({Total} rows).", total);

        var rewritten = 0;
        var lastLoggedProgress = 0;
        Guid? lastId = null;
        while (true)
        {
            var query = ctx.MultipartFiles.AsNoTracking();
            if (lastId.HasValue)
            {
                query = query.Where(x => x.Id.CompareTo(lastId.Value) > 0);
            }

            var batch = await query
                .OrderBy(x => x.Id)
                .Take(BatchSize)
                .Select(x => new
                {
                    x.Id,
                    x.Metadata
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);
            if (batch.Count == 0) break;
            lastId = batch.Last().Id;
            foreach (var item in batch)
            {
                var entity = new DavMultipartFile
                {
                    Id = item.Id,
                    Metadata = item.Metadata
                };
                ctx.MultipartFiles.Attach(entity);
                ctx.Entry(entity).Property(x => x.Metadata).IsModified = true;
            }

            rewritten += batch.Count;
            await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
            ctx.ChangeTracker.Clear();

            if (ShouldLogProgress(rewritten, total, lastLoggedProgress))
            {
                LogRewriteProgress("DavMultipartFiles", rewritten, total);
                lastLoggedProgress = rewritten;
            }
        }

        if (rewritten != lastLoggedProgress)
        {
            LogRewriteProgress("DavMultipartFiles", rewritten, total);
        }

        return rewritten;
    }

    private static bool ShouldLogProgress(int processed, int total, int lastLogged)
    {
        if (processed == 0) return false;
        if (processed == total) return true;
        return processed - lastLogged >= ProgressLogInterval;
    }

    private static void LogRewriteProgress(string entityName, int processed, int total)
    {
        var percent = total == 0 ? 100 : (int)((double)processed / total * 100);
        Log.Information("{Entity} rewrite progress: {Processed}/{Total} rows ({Percent}% complete).", entityName, processed, total, percent);
    }
}
