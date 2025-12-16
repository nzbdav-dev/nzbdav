using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database.Models;
using Serilog;

namespace NzbWebDAV.Database;

public sealed class DatabaseDumpService
{
    private const int ImportBatchSize = 500;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public async Task ExportAsync(DavDatabaseContext ctx, string outputDirectory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        Log.Information("Exporting database to {Directory}.", outputDirectory);

        await ExportTableAsync(ctx.ConfigItems.AsNoTracking().OrderBy(x => x.ConfigName), Path.Combine(outputDirectory, "ConfigItems.jsonl"), ct).ConfigureAwait(false);
        await ExportTableAsync(
            ctx.Accounts
                .AsNoTracking()
                .OrderBy(x => x.Type)
                .ThenBy(x => x.Username),
            Path.Combine(outputDirectory, "Accounts.jsonl"),
            ct).ConfigureAwait(false);
        await ExportTableAsync(ctx.Items.AsNoTracking().OrderBy(x => x.Id), Path.Combine(outputDirectory, "DavItems.jsonl"), ct).ConfigureAwait(false);
        await ExportTableAsync(ctx.NzbFiles.AsNoTracking().OrderBy(x => x.Id), Path.Combine(outputDirectory, "DavNzbFiles.jsonl"), ct).ConfigureAwait(false);
        await ExportTableAsync(ctx.RarFiles.AsNoTracking().OrderBy(x => x.Id), Path.Combine(outputDirectory, "DavRarFiles.jsonl"), ct).ConfigureAwait(false);
        await ExportTableAsync(ctx.MultipartFiles.AsNoTracking().OrderBy(x => x.Id), Path.Combine(outputDirectory, "DavMultipartFiles.jsonl"), ct).ConfigureAwait(false);
        await ExportTableAsync(ctx.QueueItems.AsNoTracking().OrderBy(x => x.Id), Path.Combine(outputDirectory, "QueueItems.jsonl"), ct).ConfigureAwait(false);
        await ExportTableAsync(ctx.QueueNzbContents.AsNoTracking().OrderBy(x => x.Id), Path.Combine(outputDirectory, "QueueNzbContents.jsonl"), ct).ConfigureAwait(false);
        await ExportTableAsync(ctx.HistoryItems.AsNoTracking().OrderBy(x => x.CreatedAt), Path.Combine(outputDirectory, "HistoryItems.jsonl"), ct).ConfigureAwait(false);
        await ExportTableAsync(ctx.HealthCheckResults.AsNoTracking().OrderBy(x => x.CreatedAt), Path.Combine(outputDirectory, "HealthCheckResults.jsonl"), ct).ConfigureAwait(false);
        await ExportTableAsync(ctx.HealthCheckStats.AsNoTracking().OrderBy(x => x.DateStartInclusive), Path.Combine(outputDirectory, "HealthCheckStats.jsonl"), ct).ConfigureAwait(false);

        Log.Information("Database export completed successfully.");
    }

    public async Task ImportAsync(DavDatabaseContext ctx, string inputDirectory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputDirectory);
        if (!Directory.Exists(inputDirectory)) throw new DirectoryNotFoundException(inputDirectory);

        Log.Information("Rebuilding database at {DatabasePath} from {Directory}.", DavDatabaseContext.DatabaseFilePath, inputDirectory);

        await ctx.Database.EnsureDeletedAsync(ct).ConfigureAwait(false);
        await ctx.Database.MigrateAsync(cancellationToken: ct).ConfigureAwait(false);

        var previousAutoDetect = ctx.ChangeTracker.AutoDetectChangesEnabled;
        ctx.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            await ImportConfigItemsAsync(ctx, Path.Combine(inputDirectory, "ConfigItems.jsonl"), ct).ConfigureAwait(false);
            await ImportTableAsync(ctx, ctx.Accounts, Path.Combine(inputDirectory, "Accounts.jsonl"), ct).ConfigureAwait(false);
            await ImportTableAsync(ctx, ctx.Items, Path.Combine(inputDirectory, "DavItems.jsonl"), ct).ConfigureAwait(false);
            await ImportTableAsync(ctx, ctx.NzbFiles, Path.Combine(inputDirectory, "DavNzbFiles.jsonl"), ct).ConfigureAwait(false);
            await ImportTableAsync(ctx, ctx.RarFiles, Path.Combine(inputDirectory, "DavRarFiles.jsonl"), ct).ConfigureAwait(false);
            await ImportTableAsync(ctx, ctx.MultipartFiles, Path.Combine(inputDirectory, "DavMultipartFiles.jsonl"), ct).ConfigureAwait(false);
            await ImportTableAsync(ctx, ctx.QueueItems, Path.Combine(inputDirectory, "QueueItems.jsonl"), ct).ConfigureAwait(false);
            await ImportTableAsync(ctx, ctx.QueueNzbContents, Path.Combine(inputDirectory, "QueueNzbContents.jsonl"), ct).ConfigureAwait(false);
            await ImportTableAsync(ctx, ctx.HistoryItems, Path.Combine(inputDirectory, "HistoryItems.jsonl"), ct).ConfigureAwait(false);
            await ImportTableAsync(ctx, ctx.HealthCheckResults, Path.Combine(inputDirectory, "HealthCheckResults.jsonl"), ct).ConfigureAwait(false);
            await ImportTableAsync(ctx, ctx.HealthCheckStats, Path.Combine(inputDirectory, "HealthCheckStats.jsonl"), ct).ConfigureAwait(false);
        }
        finally
        {
            ctx.ChangeTracker.AutoDetectChangesEnabled = previousAutoDetect;
            ctx.ChangeTracker.Clear();
        }

        Log.Information("Database import completed successfully.");
    }

    private static async Task ExportTableAsync<T>(IQueryable<T> query, string filePath, CancellationToken ct)
    {
        await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        var total = 0;
        await foreach (var entity in query.AsAsyncEnumerable().WithCancellation(ct))
        {
            var json = JsonSerializer.Serialize(entity, SerializerOptions);
            await writer.WriteLineAsync(json).ConfigureAwait(false);
            total++;
        }

        Log.Information("Exported {RowCount} rows to {FileName}.", total, Path.GetFileName(filePath));
    }

    private static async Task ImportTableAsync<T>(DavDatabaseContext ctx, DbSet<T> dbSet, string filePath, CancellationToken ct)
        where T : class
    {
        if (!File.Exists(filePath))
        {
            Log.Information("Skipping import for {FileName}; file not found.", Path.GetFileName(filePath));
            return;
        }

        var buffer = new List<T>(ImportBatchSize);
        var total = 0;

        await foreach (var line in File.ReadLinesAsync(filePath, ct))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var entity = JsonSerializer.Deserialize<T>(line, SerializerOptions);
            if (entity == null) continue;
            buffer.Add(entity);
            total++;

            if (buffer.Count >= ImportBatchSize)
            {
                await dbSet.AddRangeAsync(buffer, ct).ConfigureAwait(false);
                await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
                buffer.Clear();
                ctx.ChangeTracker.Clear();
            }
        }

        if (buffer.Count > 0)
        {
            await dbSet.AddRangeAsync(buffer, ct).ConfigureAwait(false);
            await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
            buffer.Clear();
            ctx.ChangeTracker.Clear();
        }

        Log.Information("Imported {RowCount} rows from {FileName}.", total, Path.GetFileName(filePath));
    }

    private static async Task ImportConfigItemsAsync(DavDatabaseContext ctx, string filePath, CancellationToken ct)
    {
        if (!File.Exists(filePath))
        {
            Log.Information("Skipping import for {FileName}; file not found.", Path.GetFileName(filePath));
            return;
        }

        await ctx.ConfigItems.ExecuteDeleteAsync(ct).ConfigureAwait(false);

        var items = new Dictionary<string, ConfigItem>(StringComparer.Ordinal);
        await foreach (var line in File.ReadLinesAsync(filePath, ct))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var entity = JsonSerializer.Deserialize<ConfigItem>(line, SerializerOptions);
            if (entity == null || string.IsNullOrWhiteSpace(entity.ConfigName)) continue;
            items[entity.ConfigName] = entity;
        }

        if (items.Count == 0)
        {
            Log.Information("Imported 0 rows from {FileName}.", Path.GetFileName(filePath));
            return;
        }

        await ctx.ConfigItems.AddRangeAsync(items.Values, ct).ConfigureAwait(false);
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        ctx.ChangeTracker.Clear();

        Log.Information("Imported {RowCount} rows from {FileName}.", items.Count, Path.GetFileName(filePath));
    }
}
