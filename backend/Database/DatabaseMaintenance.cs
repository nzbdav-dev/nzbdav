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
    private const int CompressionVersion = 1;
    private const int BatchSize = 250;

    private static readonly object AutoVacuumLock = new();
    private static bool _autoVacuumConfigured;

    public static void EnsureDataDirectory()
    {
        var directory = Path.GetDirectoryName(DavDatabaseContext.DatabaseFilePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }

    public static void EnsureAutoVacuumConfigured()
    {
        if (_autoVacuumConfigured) return;
        lock (AutoVacuumLock)
        {
            if (_autoVacuumConfigured) return;
            EnsureDataDirectory();
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
            await VacuumAsync(ct).ConfigureAwait(false);
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
            await VacuumAsync(ct).ConfigureAwait(false);
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

    public static async Task VacuumAsync(CancellationToken ct)
    {
        EnsureDataDirectory();
        await using var connection = new SqliteConnection($"Data Source={DavDatabaseContext.DatabaseFilePath}");
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "VACUUM;";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<int> RewriteNzbFilesAsync(DavDatabaseContext ctx, CancellationToken ct)
    {
        var rewritten = 0;
        var lastRowId = 0L;
        while (true)
        {
            var batch = await ctx.NzbFiles.AsNoTracking()
                .Where(x => EF.Property<long>(x, "rowid") > lastRowId)
                .OrderBy(x => EF.Property<long>(x, "rowid"))
                .Take(BatchSize)
                .Select(x => new
                {
                    RowId = EF.Property<long>(x, "rowid"),
                    x.Id,
                    x.SegmentIds
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);
            if (batch.Count == 0) break;
            lastRowId = batch.Last().RowId;
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
        }

        return rewritten;
    }

    private static async Task<int> RewriteRarFilesAsync(DavDatabaseContext ctx, CancellationToken ct)
    {
        var rewritten = 0;
        var lastRowId = 0L;
        while (true)
        {
            var batch = await ctx.RarFiles.AsNoTracking()
                .Where(x => EF.Property<long>(x, "rowid") > lastRowId)
                .OrderBy(x => EF.Property<long>(x, "rowid"))
                .Take(BatchSize)
                .Select(x => new
                {
                    RowId = EF.Property<long>(x, "rowid"),
                    x.Id,
                    x.RarParts
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);
            if (batch.Count == 0) break;
            lastRowId = batch.Last().RowId;
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
        }

        return rewritten;
    }

    private static async Task<int> RewriteMultipartFilesAsync(DavDatabaseContext ctx, CancellationToken ct)
    {
        var rewritten = 0;
        var lastRowId = 0L;
        while (true)
        {
            var batch = await ctx.MultipartFiles.AsNoTracking()
                .Where(x => EF.Property<long>(x, "rowid") > lastRowId)
                .OrderBy(x => EF.Property<long>(x, "rowid"))
                .Take(BatchSize)
                .Select(x => new
                {
                    RowId = EF.Property<long>(x, "rowid"),
                    x.Id,
                    x.Metadata
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);
            if (batch.Count == 0) break;
            lastRowId = batch.Last().RowId;
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
        }

        return rewritten;
    }
}
