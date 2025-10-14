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
                Log.Error($"Unexpected error performing background health checks: {e.Message}");
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
                Id = new Guid(),
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
        var symlink = OrganizedSymlinksUtil.GetSymlink(davItem, _configManager);

        // for unlinked files, we can simply delete the unhealthy item
        if (symlink == null)
        {
            dbClient.Ctx.Items.Remove(davItem);
            dbClient.Ctx.HealthCheckResults.Add(new HealthCheckResult()
            {
                Id = new Guid(),
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

        Log.Warning($"Symlink found in organized media library: {symlink}");
    }
}