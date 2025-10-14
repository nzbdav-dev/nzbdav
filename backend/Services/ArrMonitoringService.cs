using NzbWebDAV.Clients.RadarrSonarr;
using NzbWebDAV.Clients.RadarrSonarr.BaseModels;
using NzbWebDAV.Config;
using NzbWebDAV.Utils;
using Serilog;

namespace NzbWebDAV.Services;

/// <summary>
/// - This class takes care of monitoring Radarr/Sonarr instances
///   for stuck queue items which usually require manual intervention.
/// - NzbDAV can be configured to automatically remove these stuck items,
///   optionally block these stuck items, and optionally trigger a new
///   search for these stuck items.
/// </summary>
public class ArrMonitoringService
{
    private readonly ConfigManager _configManager;
    private readonly CancellationToken _cancellationToken = SigtermUtil.GetCancellationToken();

    public ArrMonitoringService(ConfigManager configManager)
    {
        _configManager = configManager;
        _ = StartMonitoringService();
    }

    private async Task StartMonitoringService()
    {
        while (!_cancellationToken.IsCancellationRequested)
        {
            // Ensure delay runs on each iteration
            await Task.Delay(TimeSpan.FromSeconds(10), _cancellationToken);

            // if all queue-actions are disabled, then do nothing
            var arrConfig = _configManager.GetArrConfig();
            if (arrConfig.QueueRules.All(x => x.Action == ArrConfig.QueueAction.DoNothing))
                continue;

            // otherwise, handle stuck queue items according to the config
            foreach (var arrClient in arrConfig.GetArrClients())
                await HandleStuckQueueItems(arrConfig, arrClient);
        }
    }

    private async Task HandleStuckQueueItems(ArrConfig arrConfig, ArrClient client)
    {
        try
        {
            var queueStatus = await client.GetQueueStatusAsync();
            if (queueStatus is { Warnings: false, UnknownWarnings: false }) return;
            var queue = await client.GetQueueAsync();
            var actionableStatuses = arrConfig.QueueRules.Select(x => x.Message);
            var stuckRecords = queue.Records.Where(x => actionableStatuses.Any(x.HasStatusMessage));
            foreach (var record in stuckRecords)
                await HandleStuckQueueItem(record, arrConfig, client);
        }
        catch (Exception e)
        {
            Log.Error($"Error occured while monitoring queue for `{client.Host}`: {e.Message}");
        }
    }

    private async Task HandleStuckQueueItem(ArrQueueRecord item, ArrConfig arrConfig, ArrClient client)
    {
        // since there may be multiple status messages, multiple actions may apply.
        // in such case, always perform the strongest action.
        var action = arrConfig.QueueRules
            .Where(x => item.HasStatusMessage(x.Message))
            .Select(x => x.Action)
            .DefaultIfEmpty(ArrConfig.QueueAction.DoNothing)
            .Max();

        if (action is ArrConfig.QueueAction.DoNothing) return;
        await client.DeleteQueueRecord(item.Id, action);
        Log.Warning($"Resolved stuck queue item `{item.Title}` from `{client.Host}, with action `{action}`");
    }
}