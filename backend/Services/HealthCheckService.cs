using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Clients.Usenet.Connections;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.Services;

/// <summary>
/// This service monitors for health checks
/// </summary>
public class HealthCheckService
{
    private readonly ConfigManager _configManager;
    private readonly UsenetStreamingClient _usenetClient;
    private readonly WebsocketManager _websocketManager;
    private readonly CancellationToken _cancellationToken = SigtermUtil.GetCancellationToken();

    public HealthCheckService
    (
        ConfigManager configManager,
        UsenetStreamingClient usenetClient,
        WebsocketManager websocketManager
    )
    {
        _configManager = configManager;
        _usenetClient = usenetClient;
        _websocketManager = websocketManager;
        _ = StartMonitoringService();
    }

    private async Task StartMonitoringService()
    {
        while (!_cancellationToken.IsCancellationRequested)
        {
            try
            {
                // if max-repair-connections isn't configured, don't do anything
                var maxRepairConnections = _configManager.GetMaxRepairConnections();
                if (maxRepairConnections == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), _cancellationToken);
                    continue;
                }

                // set reserved-connections context
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
                var reservedConnections = _configManager.GetMaxConnections() - maxRepairConnections;
                using var _ = cts.Token.SetScopedContext(new ReservedConnectionsContext(reservedConnections));

                // get the davItem to health-check
                await using var dbContext = new DavDatabaseContext();
                var dbClient = new DavDatabaseClient(dbContext);
                var currentDateTime = DateTimeOffset.UtcNow;
                var davItem = await dbClient.Ctx.Items
                    .Where(x => x.Type == DavItem.ItemType.NzbFile || x.Type == DavItem.ItemType.RarFile)
                    .Where(x => x.NextHealthCheck == null || x.NextHealthCheck < currentDateTime)
                    .OrderBy(x => x.NextHealthCheck)
                    .ThenByDescending(x => x.ReleaseDate)
                    .FirstOrDefaultAsync(cts.Token);

                // if there is no item to health-check, don't do anything
                if (davItem == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
                    continue;
                }

                // perform the health check
                var result = await PerformHealthCheck(davItem, dbClient, maxRepairConnections, cts.Token);
                if (!result) return;
            }
            catch (Exception e)
            {
                Log.Error($"Unexpected error performing background health checks: {e.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5), _cancellationToken);
            }
        }
    }

    private async Task<bool> PerformHealthCheck
    (
        DavItem davItem,
        DavDatabaseClient dbClient,
        int concurrency,
        CancellationToken ct
    )
    {
        try
        {
            // update the release date, if null
            var segments = await GetAllSegments(davItem, dbClient, ct);
            if (davItem.ReleaseDate == null) await UpdateReleaseDate(davItem, segments, ct);


            // setup progress tracking
            var progressHook = new Progress<int>();
            var debounce = DebounceUtil.CreateDebounce(TimeSpan.FromMilliseconds(200));
            progressHook.ProgressChanged += (_, progress) =>
            {
                var message = $"{davItem.Id}|{progress}";
                debounce(() => _websocketManager.SendMessage(WebsocketTopic.HealthItemProgress, message));
            };

            // perform health check
            await _usenetClient.CheckAllSegmentsAsync(segments, concurrency, progressHook, ct);
            _ = _websocketManager.SendMessage(WebsocketTopic.HealthItemProgress, $"{davItem.Id}|100");
            _ = _websocketManager.SendMessage(WebsocketTopic.HealthItemStatus, $"{davItem.Id}|healthy");

            // update the database
            davItem.LastHealthCheck = DateTimeOffset.UtcNow;
            davItem.NextHealthCheck = davItem.ReleaseDate + 2 * (davItem.LastHealthCheck - davItem.ReleaseDate);
            await dbClient.Ctx.SaveChangesAsync(ct);
            return true;
        }
        catch (UsenetArticleNotFoundException)
        {
            _ = _websocketManager.SendMessage(WebsocketTopic.HealthItemProgress, $"{davItem.Id}|100");
            _ = _websocketManager.SendMessage(WebsocketTopic.HealthItemStatus, $"{davItem.Id}|unhealthy");
            await Repair(davItem, dbClient, ct);
            return false;
        }
    }

    private async Task UpdateReleaseDate(DavItem davItem, List<string> segments, CancellationToken ct)
    {
        var firstSegmentId = StringUtil.EmptyToNull(segments.FirstOrDefault());
        if (firstSegmentId == null) return;
        var articleHeaders = await _usenetClient.GetArticleHeadersAsync(firstSegmentId, ct);
        davItem.ReleaseDate = articleHeaders.Date;
    }

    private async Task<List<string>> GetAllSegments(DavItem davItem, DavDatabaseClient dbClient, CancellationToken ct)
    {
        if (davItem.Type == DavItem.ItemType.NzbFile)
        {
            var nzbFile = await dbClient.GetNzbFileAsync(davItem.Id, ct);
            return nzbFile?.SegmentIds?.ToList() ?? [];
        }

        if (davItem.Type == DavItem.ItemType.RarFile)
        {
            var rarFile = await dbClient.Ctx.RarFiles
                .Where(x => x.Id == davItem.Id)
                .FirstOrDefaultAsync(ct);
            return rarFile?.RarParts?.SelectMany(x => x.SegmentIds)?.ToList() ?? [];
        }

        return [];
    }

    private async Task Repair(DavItem davItem, DavDatabaseClient dbClient, CancellationToken ct)
    {
        Log.Warning($"Repairing database item: {davItem.Path}");
        var symlink = OrganizedSymlinks.GetSymlink(davItem, _configManager);

        // for unlinked files, we can simply delete the unhealthy item
        if (symlink == null)
        {
            Log.Warning($"Item is not linked to organized media library. Deleting: {davItem.Path}");
            // dbClient.Ctx.Items.Remove(davItem);
            return;
        }

        Log.Warning($"Symlink found in organized media library: {symlink}");
    }

    private static class OrganizedSymlinks
    {
        private static readonly Dictionary<Guid, string> Cache = new();

        public static string? GetSymlink(DavItem targetDavItem, ConfigManager configManager)
        {
            return !TryGetSymlinkFromCache(targetDavItem, configManager, out var symlinkFromCache)
                ? SearchForSymlink(targetDavItem, configManager)
                : symlinkFromCache;
        }

        private static bool TryGetSymlinkFromCache
        (
            DavItem targetDavItem,
            ConfigManager configManager,
            out string? symlink
        )
        {
            return Cache.TryGetValue(targetDavItem.Id, out symlink)
                   && Verify(symlink, targetDavItem, configManager);
        }

        private static bool Verify(string symlink, DavItem targetDavItem, ConfigManager configManager)
        {
            return GetSymlinkTargets([symlink], configManager)
                .Select(x => x.Target)
                .FirstOrDefault() == targetDavItem.Id;
        }

        private static string? SearchForSymlink(DavItem targetDavItem, ConfigManager configManager)
        {
            var libraryRoot = configManager.GetLibraryDir()!;
            var allSymlinkPaths = Directory.EnumerateFileSystemEntries(libraryRoot, "*", SearchOption.AllDirectories);
            var allSymlinks = GetSymlinkTargets(allSymlinkPaths, configManager);

            string? result = null;
            foreach (var symlink in allSymlinks)
            {
                Cache[targetDavItem.Id] = symlink.FileInfo.FullName;
                if (symlink.Target == targetDavItem.Id)
                    result = symlink.FileInfo.FullName;
            }

            return result;
        }

        private static IEnumerable<(FileInfo FileInfo, Guid Target)> GetSymlinkTargets
        (
            IEnumerable<string> symlinkPaths,
            ConfigManager configManager
        )
        {
            var mountDir = configManager.GetRcloneMountDir();
            return symlinkPaths
                .Select(x => new FileInfo(x))
                .Where(x => x.Attributes.HasFlag(FileAttributes.ReparsePoint))
                .Select(x => (FileInfo: x, Target: x.LinkTarget))
                .Where(x => x.Target is not null)
                .Select(x => (x.FileInfo, Target: x.Target!))
                .Where(x => x.Target.StartsWith(mountDir))
                .Select(x => (x.FileInfo, Target: x.Target.RemovePrefix(mountDir)))
                .Select(x => (x.FileInfo, Target: x.Target.StartsWith("/") ? x.Target : $"/{x.Target}"))
                .Where(x => x.Target.StartsWith("/.ids"))
                .Select(x => (x.FileInfo, Target: Path.GetFileNameWithoutExtension(x.Target)))
                .Select(x => (x.FileInfo, Target: Guid.Parse(x.Target)));
        }
    }
}