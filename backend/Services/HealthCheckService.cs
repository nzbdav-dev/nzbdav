using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Clients.RadarrSonarr;
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

    private const string NoCorrespondingArrInstance =
        "Deleted unhealthy symlink and webdav-item. " +
        "Could not find a corresponding Radarr/Sonarr instance to trigger a new search.";

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
                var davItem = await GetHealthCheckQueueItems(dbClient)
                    .Where(x => x.NextHealthCheck == null || x.NextHealthCheck < currentDateTime)
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
                Log.Error(e, $"Unexpected error performing background health checks: {e.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5), _cancellationToken);
            }
        }
    }

    public static IOrderedQueryable<DavItem> GetHealthCheckQueueItems(DavDatabaseClient dbClient)
    {
        var actionNeeded = HealthCheckResult.RepairAction.ActionNeeded;
        var healthCheckResults = dbClient.Ctx.HealthCheckResults;
        return dbClient.Ctx.Items
            .Where(x => x.Type == DavItem.ItemType.NzbFile || x.Type == DavItem.ItemType.RarFile)
            .Where(x => !healthCheckResults.Any(h => h.DavItemId == x.Id && h.RepairStatus == actionNeeded))
            .OrderBy(x => x.NextHealthCheck)
            .ThenByDescending(x => x.ReleaseDate);
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
            dbClient.Ctx.HealthCheckResults.Add(new HealthCheckResult()
            {
                Id = Guid.NewGuid(),
                DavItemId = davItem.Id,
                Path = davItem.Path,
                CreatedAt = DateTimeOffset.UtcNow,
                Result = HealthCheckResult.HealthResult.Healthy,
                RepairStatus = HealthCheckResult.RepairAction.None,
                Message = null
            });
            await dbClient.Ctx.SaveChangesAsync(ct);
            return true;
        }
        catch (UsenetArticleNotFoundException)
        {
            // when usenet article is missing, perform repairs
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
        try
        {
            var symlink = OrganizedSymlinksUtil.GetSymlink(davItem, _configManager);

            // if the unhealthy item is unlinked/orphaned,
            // then we can simply delete it.
            if (symlink == null)
            {
                dbClient.Ctx.Items.Remove(davItem);
                dbClient.Ctx.HealthCheckResults.Add(new HealthCheckResult()
                {
                    Id = Guid.NewGuid(),
                    DavItemId = davItem.Id,
                    Path = davItem.Path,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Result = HealthCheckResult.HealthResult.Unhealthy,
                    RepairStatus = HealthCheckResult.RepairAction.Deleted,
                    Message = null
                });
                await dbClient.Ctx.SaveChangesAsync(ct);
                return;
            }

            // if the unhealthy item is symlinked within the organized media-library
            // then we must find the corresponding arr instance and trigger a new search.
            foreach (var arrClient in _configManager.GetArrConfig().GetArrClients())
            {
                var rootFolders = await arrClient.GetRootFolders();
                if (!rootFolders.Any(x => symlink.StartsWith(x.Path!))) continue;

                // if we found corresponding sonarr instance,
                // then do nothing for now. It is not yet implemented.
                if (arrClient is SonarrClient)
                {
                    Log.Warning($"Found corresponding arr instance for repair: {arrClient.Host}");
                    return;
                }

                // if we found a corresponding radarr instance,
                // then remove and search.
                if (await arrClient.RemoveAndSearch(symlink))
                {
                    dbClient.Ctx.Items.Remove(davItem);
                    dbClient.Ctx.HealthCheckResults.Add(new HealthCheckResult()
                    {
                        Id = Guid.NewGuid(),
                        DavItemId = davItem.Id,
                        Path = davItem.Path,
                        CreatedAt = DateTimeOffset.UtcNow,
                        Result = HealthCheckResult.HealthResult.Unhealthy,
                        RepairStatus = HealthCheckResult.RepairAction.Repaired,
                        Message = null
                    });
                    await dbClient.Ctx.SaveChangesAsync(ct);
                    return;
                }

                // if we could not find corresponding media-item to remove-and-search
                // within the found arr instance, then break out of this loop so that
                // we can fall back to the behavior below of deleting both the symlink
                // and the dav-item.
                break;
            }

            // if we could not find a corresponding arr instance
            // then we can delete both the item and the symlink.
            File.Delete(symlink);
            dbClient.Ctx.Items.Remove(davItem);
            dbClient.Ctx.HealthCheckResults.Add(new HealthCheckResult()
            {
                Id = Guid.NewGuid(),
                DavItemId = davItem.Id,
                Path = davItem.Path,
                CreatedAt = DateTimeOffset.UtcNow,
                Result = HealthCheckResult.HealthResult.Unhealthy,
                RepairStatus = HealthCheckResult.RepairAction.Deleted,
                Message = NoCorrespondingArrInstance
            });
            await dbClient.Ctx.SaveChangesAsync(ct);
        }
        catch (Exception e)
        {
            // if an error is encountered during repairs,
            // then mark the item as unhealthy
            var utcNow = DateTimeOffset.UtcNow;
            davItem.LastHealthCheck = utcNow;
            davItem.NextHealthCheck = null;
            dbClient.Ctx.HealthCheckResults.Add(new HealthCheckResult()
            {
                Id = Guid.NewGuid(),
                DavItemId = davItem.Id,
                Path = davItem.Path,
                CreatedAt = utcNow,
                Result = HealthCheckResult.HealthResult.Unhealthy,
                RepairStatus = HealthCheckResult.RepairAction.ActionNeeded,
                Message = e.Message
            });
            await dbClient.Ctx.SaveChangesAsync(ct);
        }
    }
}