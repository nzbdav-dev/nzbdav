using Microsoft.Extensions.Hosting;
using NzbWebDAV.Config;
using NzbWebDAV.Tasks;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.Services;

/// <summary>
/// Runs the RemoveUnlinkedFilesTask daily at the configured time when scheduling is enabled.
/// </summary>
public class RemoveOrphanedFilesSchedulerService : BackgroundService
{
    private readonly ConfigManager _configManager;
    private readonly WebsocketManager _websocketManager;
    private CancellationTokenSource _rescheduleCts = new();

    public RemoveOrphanedFilesSchedulerService(ConfigManager configManager, WebsocketManager websocketManager)
    {
        _configManager = configManager;
        _websocketManager = websocketManager;

        _configManager.OnConfigChanged += (_, args) =>
        {
            if (!args.ChangedConfig.ContainsKey("maintenance.remove-orphaned-schedule-enabled") &&
                !args.ChangedConfig.ContainsKey("maintenance.remove-orphaned-schedule-time"))
                return;

            var old = Interlocked.Exchange(ref _rescheduleCts, new CancellationTokenSource());
            old.Cancel();
            old.Dispose();
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_configManager.IsRemoveOrphanedFilesScheduleEnabled())
                {
                    using var disabledLinked = CancellationTokenSource
                        .CreateLinkedTokenSource(stoppingToken, _rescheduleCts.Token);
                    await Task.Delay(Timeout.Infinite, disabledLinked.Token).ConfigureAwait(false);
                    continue;
                }

                var scheduleTime = _configManager.RemoveOrphanedFilesSchedule();
                var now = DateTime.Now;
                var todayRun = now.Date + scheduleTime;
                var nextRun = todayRun > now ? todayRun : todayRun.AddDays(1);
                var delay = nextRun - now;

                Log.Information("RemoveOrphanedFilesScheduler: next run scheduled at {NextRun}", nextRun);

                using var delayLinked = CancellationTokenSource
                    .CreateLinkedTokenSource(stoppingToken, _rescheduleCts.Token);
                await Task.Delay(delay, delayLinked.Token).ConfigureAwait(false);

                Log.Information("RemoveOrphanedFilesScheduler: running scheduled Remove Orphaned Files task");
                var task = new RemoveUnlinkedFilesTask(_configManager, _websocketManager, isDryRun: false);
                await task.Execute().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (SigtermUtil.IsSigtermTriggered())
            {
                // OperationCanceledException is expected on sigterm
                return;
            }
            catch (OperationCanceledException)
            {
                // Config changed — loop and recompute the next run time
            }
            catch (Exception e)
            {
                Log.Error(e, "RemoveOrphanedFilesScheduler: error running scheduled task: {Message}", e.Message);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
