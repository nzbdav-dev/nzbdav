using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Utils;
using Serilog;

namespace NzbWebDAV.Services;

public sealed class ContentIndexRecoveryService(ConfigManager configManager) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshotReadResult = await ContentIndexSnapshotStore.ReadAsync(cancellationToken).ConfigureAwait(false);
            foreach (var warning in snapshotReadResult.Warnings)
                Log.Warning(warning);

            var snapshot = snapshotReadResult.Snapshot;
            if (snapshot == null || snapshot.Items.Count == 0) return;

            await using var dbContext = new DavDatabaseContext();
            var plan = await BuildRecoveryPlanAsync(dbContext, snapshot, configManager, cancellationToken).ConfigureAwait(false);
            if (!plan.NeedsRecovery) return;

            Log.Warning(
                "Recovering /content from snapshot '{SourcePath}'. Full restore: {RestoreAll}. Missing items: {MissingItems}. Missing metadata rows: {MissingMetadata}.",
                snapshotReadResult.SourcePath,
                plan.RestoreAllContentItems,
                plan.MissingItemIds.Count,
                plan.MissingNzbFileIds.Count + plan.MissingRarFileIds.Count + plan.MissingMultipartFileIds.Count
            );

            await RestoreAsync(dbContext, snapshot, plan, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to restore /content items from persisted snapshot.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    internal static async Task<RecoveryPlan> BuildRecoveryPlanAsync
    (
        DavDatabaseContext dbContext,
        ContentIndexSnapshotStore.ContentIndexSnapshot snapshot,
        ConfigManager configManager,
        CancellationToken cancellationToken
    )
    {
        var currentItems = await dbContext.Items
            .AsNoTracking()
            .Where(x => x.Path.StartsWith(ContentPathUtil.ForwardSlashPrefix) || x.Path.StartsWith(ContentPathUtil.BackslashPrefix))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (currentItems.Count == 0)
        {
            return new RecoveryPlan
            {
                RestoreAllContentItems = true,
            };
        }

        var snapshotItemsById = snapshot.Items.ToDictionary(x => x.Id);
        var currentItemsById = currentItems.ToDictionary(x => x.Id);
        var missingItemIds = new HashSet<Guid>();

        foreach (var item in currentItems)
        {
            if (item.ParentId == null || item.ParentId == DavItem.ContentFolder.Id) continue;
            if (currentItemsById.ContainsKey(item.ParentId.Value)) continue;
            AddItemAndAncestors(item.ParentId.Value, snapshotItemsById, missingItemIds);
        }

        foreach (var linkedItemId in GetLinkedItemIds(configManager))
        {
            if (currentItemsById.ContainsKey(linkedItemId)) continue;
            AddItemAndAncestors(linkedItemId, snapshotItemsById, missingItemIds);
        }

        var effectiveItemsById = snapshot.Items
            .Where(x => currentItemsById.ContainsKey(x.Id) || missingItemIds.Contains(x.Id))
            .ToDictionary(x => x.Id);
        var effectiveFileItems = effectiveItemsById.Values
            .Where(x => x.Type is DavItem.ItemType.NzbFile or DavItem.ItemType.RarFile or DavItem.ItemType.MultipartFile)
            .ToArray();
        var effectiveIds = effectiveFileItems.Select(x => x.Id).ToHashSet();

        var currentNzbIds = await dbContext.NzbFiles
            .AsNoTracking()
            .Where(x => effectiveIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToHashSetAsync(cancellationToken)
            .ConfigureAwait(false);
        var currentRarIds = await dbContext.RarFiles
            .AsNoTracking()
            .Where(x => effectiveIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToHashSetAsync(cancellationToken)
            .ConfigureAwait(false);
        var currentMultipartIds = await dbContext.MultipartFiles
            .AsNoTracking()
            .Where(x => effectiveIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToHashSetAsync(cancellationToken)
            .ConfigureAwait(false);

        return new RecoveryPlan
        {
            MissingItemIds = missingItemIds,
            MissingNzbFileIds = effectiveFileItems
                .Where(x => x.Type == DavItem.ItemType.NzbFile && !currentNzbIds.Contains(x.Id))
                .Select(x => x.Id)
                .ToHashSet(),
            MissingRarFileIds = effectiveFileItems
                .Where(x => x.Type == DavItem.ItemType.RarFile && !currentRarIds.Contains(x.Id))
                .Select(x => x.Id)
                .ToHashSet(),
            MissingMultipartFileIds = effectiveFileItems
                .Where(x => x.Type == DavItem.ItemType.MultipartFile && !currentMultipartIds.Contains(x.Id))
                .Select(x => x.Id)
                .ToHashSet(),
        };
    }

    internal static async Task RestoreAsync
    (
        DavDatabaseContext dbContext,
        ContentIndexSnapshotStore.ContentIndexSnapshot snapshot,
        RecoveryPlan plan,
        CancellationToken cancellationToken
    )
    {
        if (!plan.NeedsRecovery) return;

        var existingItemIds = await dbContext.Items
            .Where(x => x.Path.StartsWith(ContentPathUtil.ForwardSlashPrefix) || x.Path.StartsWith(ContentPathUtil.BackslashPrefix))
            .Select(x => x.Id)
            .ToHashSetAsync(cancellationToken)
            .ConfigureAwait(false);

        var itemIdsToRestore = plan.RestoreAllContentItems
            ? snapshot.Items.Select(x => x.Id).ToHashSet()
            : new HashSet<Guid>(plan.MissingItemIds);

        foreach (var item in snapshot.Items
                     .Where(x => itemIdsToRestore.Contains(x.Id))
                     .OrderBy(x => ContentPathUtil.NormalizeSeparators(x.Path).Count(c => c == '/'))
                     .ThenBy(x => ContentPathUtil.NormalizeSeparators(x.Path), StringComparer.Ordinal))
        {
            if (existingItemIds.Contains(item.Id)) continue;

            dbContext.Items.Add(Clone(item));
            existingItemIds.Add(item.Id);
        }

        var existingNzbIds = await dbContext.NzbFiles
            .Where(x => existingItemIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToHashSetAsync(cancellationToken)
            .ConfigureAwait(false);
        var existingRarIds = await dbContext.RarFiles
            .Where(x => existingItemIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToHashSetAsync(cancellationToken)
            .ConfigureAwait(false);
        var existingMultipartIds = await dbContext.MultipartFiles
            .Where(x => existingItemIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToHashSetAsync(cancellationToken)
            .ConfigureAwait(false);

        var nzbIdsToRestore = plan.RestoreAllContentItems
            ? snapshot.NzbFiles.Select(x => x.Id).ToHashSet()
            : plan.MissingNzbFileIds;
        foreach (var nzbFile in snapshot.NzbFiles.Where(x => nzbIdsToRestore.Contains(x.Id)))
        {
            if (!existingItemIds.Contains(nzbFile.Id) || existingNzbIds.Contains(nzbFile.Id)) continue;

            dbContext.NzbFiles.Add(new DavNzbFile
            {
                Id = nzbFile.Id,
                SegmentIds = nzbFile.SegmentIds,
            });
            existingNzbIds.Add(nzbFile.Id);
        }

        var rarIdsToRestore = plan.RestoreAllContentItems
            ? snapshot.RarFiles.Select(x => x.Id).ToHashSet()
            : plan.MissingRarFileIds;
        foreach (var rarFile in snapshot.RarFiles.Where(x => rarIdsToRestore.Contains(x.Id)))
        {
            if (!existingItemIds.Contains(rarFile.Id) || existingRarIds.Contains(rarFile.Id)) continue;

            dbContext.RarFiles.Add(new DavRarFile
            {
                Id = rarFile.Id,
                RarParts = rarFile.RarParts,
            });
            existingRarIds.Add(rarFile.Id);
        }

        var multipartIdsToRestore = plan.RestoreAllContentItems
            ? snapshot.MultipartFiles.Select(x => x.Id).ToHashSet()
            : plan.MissingMultipartFileIds;
        foreach (var multipartFile in snapshot.MultipartFiles.Where(x => multipartIdsToRestore.Contains(x.Id)))
        {
            if (!existingItemIds.Contains(multipartFile.Id) || existingMultipartIds.Contains(multipartFile.Id)) continue;

            dbContext.MultipartFiles.Add(new DavMultipartFile
            {
                Id = multipartFile.Id,
                Metadata = multipartFile.Metadata,
            });
            existingMultipartIds.Add(multipartFile.Id);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddItemAndAncestors
    (
        Guid itemId,
        IReadOnlyDictionary<Guid, DavItem> snapshotItemsById,
        ISet<Guid> missingItemIds
    )
    {
        var currentId = itemId;
        while (snapshotItemsById.TryGetValue(currentId, out var item))
        {
            if (!missingItemIds.Add(currentId)) break;
            if (item.ParentId == null || item.ParentId == DavItem.ContentFolder.Id) break;
            currentId = item.ParentId.Value;
        }
    }

    private static IEnumerable<Guid> GetLinkedItemIds(ConfigManager configManager)
    {
        var libraryDir = configManager.GetLibraryDir();
        if (string.IsNullOrWhiteSpace(libraryDir) || !Directory.Exists(libraryDir))
            return [];

        try
        {
            return OrganizedLinksUtil.GetLibraryDavItemLinks(configManager)
                .Select(x => x.DavItemId)
                .Distinct()
                .ToArray();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to inspect library links while evaluating /content recovery.");
            return [];
        }
    }

    private static DavItem Clone(DavItem item)
    {
        return new DavItem
        {
            Id = item.Id,
            IdPrefix = item.IdPrefix,
            CreatedAt = item.CreatedAt,
            ParentId = item.ParentId,
            Name = item.Name,
            FileSize = item.FileSize,
            Type = item.Type,
            Path = item.Path,
            ReleaseDate = item.ReleaseDate,
            LastHealthCheck = item.LastHealthCheck,
            NextHealthCheck = item.NextHealthCheck,
        };
    }

    internal sealed class RecoveryPlan
    {
        public bool RestoreAllContentItems { get; init; }
        public HashSet<Guid> MissingItemIds { get; init; } = [];
        public HashSet<Guid> MissingNzbFileIds { get; init; } = [];
        public HashSet<Guid> MissingRarFileIds { get; init; } = [];
        public HashSet<Guid> MissingMultipartFileIds { get; init; } = [];

        public bool NeedsRecovery =>
            RestoreAllContentItems
            || MissingItemIds.Count > 0
            || MissingNzbFileIds.Count > 0
            || MissingRarFileIds.Count > 0
            || MissingMultipartFileIds.Count > 0;
    }
}
