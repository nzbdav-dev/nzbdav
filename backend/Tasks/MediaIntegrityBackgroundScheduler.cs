using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Clients;
using NzbWebDAV.Clients.Connections;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.Tasks;

public class MediaIntegrityBackgroundScheduler : IDisposable
{
    private readonly ConfigManager _configManager;
    private readonly WebsocketManager _websocketManager;
    private readonly ArrManager _arrManager;
    private readonly UsenetStreamingClient _usenetClient;
    private CancellationTokenSource _cancellationTokenSource;

    public MediaIntegrityBackgroundScheduler(
        ConfigManager configManager,
        WebsocketManager websocketManager,
        ArrManager arrManager,
        UsenetStreamingClient usenetClient
    )
    {
        _configManager = configManager;
        _websocketManager = websocketManager;
        _arrManager = arrManager;
        _usenetClient = usenetClient;
        _cancellationTokenSource = SetCancellationTokenSource();

        var integrityEnabled = _configManager.IsIntegrityCheckingEnabled();
        var scheduledEnabled = _configManager.IsScheduledIntegrityCheckingEnabled();

        Log.Information("MediaIntegrityBackgroundScheduler initializing: integrity.enabled={IntegrityEnabled}, integrity.scheduled_enabled={ScheduledEnabled}",
            integrityEnabled, scheduledEnabled);

        if (integrityEnabled && scheduledEnabled)
        {
            Log.Information("Starting background integrity scheduler");
            _ = StartSchedulerAsync();
        }
        else
        {
            Log.Information("Background integrity scheduler not started - integrity checking or scheduled checking is disabled");
        }

        _configManager.OnConfigChanged += (_, args) =>
        {
            if (args.ChangedConfig.ContainsKey("integrity.enabled") ||
                args.ChangedConfig.ContainsKey("integrity.scheduled_enabled"))
            {
                Log.Information("Integrity checking config changed, cancelling current scheduler");
                Cancel();

                if (args.NewConfig["integrity.enabled"] == "true" && args.NewConfig["integrity.scheduled_enabled"] == "true")
                {
                    _ = StartSchedulerAsync();
                }
            }
        };
    }

    public async Task<bool> IsRunningAsync()
    {
        var (isRunning, taskType) = await IntegrityCheckSemaphore.GetRunningTaskStatusAsync();
        return isRunning && taskType == "Scheduled";
    }

    private CancellationTokenSource SetCancellationTokenSource()
    {
        return CancellationTokenSource.CreateLinkedTokenSource(SigtermUtil.GetCancellationToken());
    }

    public void Cancel()
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        _cancellationTokenSource = SetCancellationTokenSource();
    }

    public async Task StartSchedulerAsync()
    {
        var schedulerCancelToken = _cancellationTokenSource.Token;

        Log.Information("Background integrity scheduler starting - waiting 2 minutes for backend initialization");

        // Wait 2 minutes for backend to start before starting the scheduler
        await Task.Delay(TimeSpan.FromMinutes(2), schedulerCancelToken);

        Log.Information("Background integrity scheduler active - beginning check loop");

        while (!schedulerCancelToken.IsCancellationRequested)
        {
            try
            {
                // Check if a check is needed based on last check time and get next check time
                var (shouldRunCheck, nextCheckInMinutes) = await ShouldRunBackgroundCheckWithTimingAsync(schedulerCancelToken);

                if (shouldRunCheck)
                {
                    Log.Information("Background integrity check is due, checking for eligible files");
                    var ct = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token).Token;

                    // Get default parameters for scheduled run
                    var scheduledParams = _configManager.GetDefaultRunParameters();
                    scheduledParams.RunType = IntegrityCheckRun.RunTypeOption.Scheduled;

                    var fileScanner = new MediaIntegrityFileScanner(_configManager, scheduledParams);

                    // Get the actual list of files to check to determine if we should proceed
                    var checkItems = await fileScanner.GetIntegrityCheckItemsAsync(ct);

                    if (checkItems.Count == 0)
                    {
                        Log.Information("Background integrity check skipped: no files eligible for checking");
                        // Don't use continue here - we still need to wait before checking again
                    }
                    else
                    {
                        Log.Information("Background integrity check starting: {EligibleFiles} files eligible for checking", checkItems.Count);

                        // Generate a unique run ID for the scheduled check
                        var scheduledRunId = Guid.NewGuid().ToString();
                        var startTime = DateTime.UtcNow;

                        // Clean up old integrity check runs before starting a new one
                        await CleanupOldIntegrityRunsAsync(ct);

                        var integrityRun = new IntegrityCheckRun
                        {
                            RunId = scheduledRunId,
                            StartTime = startTime,
                            RunType = scheduledParams.RunType,
                            ScanDirectory = scheduledParams.ScanDirectory,
                            MaxFilesToCheck = scheduledParams.MaxFilesToCheck,
                            CorruptFileAction = scheduledParams.CorruptFileAction,
                            Mp4DeepScan = scheduledParams.Mp4DeepScan,
                            AutoMonitor = scheduledParams.AutoMonitor,
                            UnmonitorValidatedFiles = scheduledParams.UnmonitorValidatedFiles,
                            DirectDeletionFallback = scheduledParams.DirectDeletionFallback,
                            NzbSegmentSamplePercentage = scheduledParams.NzbSegmentSamplePercentage,
                            NzbSegmentThresholdPercentage = scheduledParams.NzbSegmentThresholdPercentage,
                            ValidFiles = 0,
                            CorruptFiles = 0,
                            TotalFiles = 0,
                            IsRunning = false, // Will be set to true when task actually starts
                            Status = IntegrityCheckRun.StatusOption.Initialized
                        };

                        await using var dbContext = new DavDatabaseContext();
                        var dbClient = new DavDatabaseClient(dbContext);
                        dbClient.Ctx.IntegrityCheckRuns.Add(integrityRun);
                        await dbClient.Ctx.SaveChangesAsync(ct);

                        // Create the task but don't start it yet
                        var checker = new MediaIntegrityChecker(_configManager, _websocketManager, _arrManager, _usenetClient);
                        var integrityTask = new Task(async () =>
                        {
                            // Set reserved connections context for this integrity check
                            var reservedConnections = _configManager.GetMaxConnections() - _configManager.GetMaxQueueConnections();
                            using var _ = ct.SetScopedContext(new ReservedConnectionsContext(reservedConnections));

                            await checker.PerformIntegrityCheckAsync(ct, scheduledRunId, checkItems, scheduledParams);
                        }, ct);

                        // Try to register and start the task
                        var taskStarted = await IntegrityCheckSemaphore.TryStartTaskAsync(integrityTask, "Scheduled", _cancellationTokenSource, ct);

                        if (taskStarted)
                        {
                            // Wait for the task to complete
                            await integrityTask;
                        }
                        else
                        {
                            Log.Information("Background integrity check skipped: another task is already running");
                        }
                    }
                }
                else
                {
                    Log.Debug("Background integrity check not due yet, next check in {NextCheckMinutes} minutes", nextCheckInMinutes);
                }

                // Wait until next check is due
                var waitMinutes = nextCheckInMinutes;
                if (waitMinutes <= 0) waitMinutes = 10; // Fallback to 10 minutes if calculation fails

                Log.Debug("Waiting {WaitMinutes} minutes before next scheduler check", waitMinutes);
                await Task.Delay(TimeSpan.FromMinutes(waitMinutes), schedulerCancelToken);
            }
            catch (OperationCanceledException)
            {
                Log.Information("Background integrity check scheduler cancelled");
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in background integrity check scheduler");
                // Wait before retrying on error
                await Task.Delay(TimeSpan.FromMinutes(10), schedulerCancelToken);
            }
        }
    }

    private async Task<(bool shouldRun, int nextCheckInMinutes)> ShouldRunBackgroundCheckWithTimingAsync(CancellationToken ct)
    {
        try
        {
            // First check if there's already a task running
            var (isRunning, taskType) = await IntegrityCheckSemaphore.GetRunningTaskStatusAsync();
            if (isRunning)
            {
                Log.Debug("Background check skipped: {TaskType} integrity check already running", taskType);
                return (false, 10); // Don't run, check again in 10 minutes
            }

            await using var dbContext = new DavDatabaseContext();
            var dbClient = new DavDatabaseClient(dbContext);

            var intervalHours = _configManager.GetIntegrityCheckIntervalHours();

            // Get the most recent check time from the new table
            var mostRecentCheck = await dbClient.Ctx.IntegrityCheckFileResults
                .OrderByDescending(r => r.LastChecked)
                .FirstOrDefaultAsync(ct);

            if (mostRecentCheck == null)
            {
                Log.Information("No previous integrity checks found, background check is due");
                return (true, 0); // No previous checks, so run now
            }

            var lastCheckTime = mostRecentCheck.LastChecked;

            var timeSinceLastCheck = DateTime.UtcNow - lastCheckTime.ToUniversalTime();
            var hoursUntilNext = intervalHours - timeSinceLastCheck.TotalHours;

            Log.Debug("Background check evaluation: Last check {LastCheck}, hours since: {HoursSince:F1}, interval: {Interval}h, hours until next: {UntilNext:F1}",
                lastCheckTime, timeSinceLastCheck.TotalHours, intervalHours, hoursUntilNext);

            var shouldRun = hoursUntilNext <= 0;
            var nextCheckMinutes = shouldRun
                ? intervalHours * 60  // When check is due, wait the full interval for next check
                : (int)Math.Max(hoursUntilNext * 60, 0); // When not due, wait until next check is due

            return (shouldRun, nextCheckMinutes);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error determining if background check should run");
            return (false, 10); // Don't run on error, retry in 10 minutes
        }
    }

    private async Task CleanupOldIntegrityRunsAsync(CancellationToken ct)
    {
        try
        {
            await using var dbContext = new DavDatabaseContext();
            var dbClient = new DavDatabaseClient(dbContext);

            var cutoffDate = DateTime.UtcNow.AddDays(-7);

            // Get old runs that are older than 7 days
            var oldRuns = await dbClient.Ctx.IntegrityCheckRuns
                .Where(r => r.StartTime < cutoffDate)
                .ToListAsync(ct);

            if (oldRuns.Count > 0)
            {
                Log.Information("Cleaning up {Count} integrity check runs older than 7 days", oldRuns.Count);

                // Delete associated file results first (foreign key constraint)
                var oldRunIds = oldRuns.Select(r => r.RunId).ToList();
                var oldFileResults = await dbClient.Ctx.IntegrityCheckFileResults
                    .Where(f => oldRunIds.Contains(f.RunId))
                    .ToListAsync(ct);

                if (oldFileResults.Count > 0)
                {
                    dbClient.Ctx.IntegrityCheckFileResults.RemoveRange(oldFileResults);
                    Log.Debug("Removing {Count} associated file results", oldFileResults.Count);
                }

                // Then delete the runs
                dbClient.Ctx.IntegrityCheckRuns.RemoveRange(oldRuns);

                await dbClient.Ctx.SaveChangesAsync(ct);
                Log.Information("Successfully cleaned up {RunCount} old runs and {FileCount} associated file results",
                    oldRuns.Count, oldFileResults.Count);
            }
            else
            {
                Log.Debug("No integrity check runs older than 7 days found for cleanup");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to clean up old integrity check runs - continuing with new run");
            // Don't throw - cleanup failure shouldn't prevent new runs
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }
}
