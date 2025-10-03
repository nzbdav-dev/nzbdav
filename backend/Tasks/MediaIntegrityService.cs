using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Api.Controllers.IntegrityResults;
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

public class IntegrityCheckItem
{
    public required DavItem DavItem { get; init; }
    public string? LibraryFilePath { get; init; }
}

public class IntegrityRunStatus
{
    [JsonPropertyName("runId")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("isRunning")]
    public bool IsRunning { get; set; }

    [JsonPropertyName("status")]
    public IntegrityCheckRun.StatusOption Status { get; set; }

    [JsonPropertyName("startTime")]
    public string? StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public string? EndTime { get; set; }

    [JsonPropertyName("totalFiles")]
    public int TotalFiles { get; set; }

    [JsonPropertyName("validFiles")]
    public int ValidFiles { get; set; }

    [JsonPropertyName("corruptFiles")]
    public int CorruptFiles { get; set; }

    [JsonPropertyName("processedFiles")]
    public int ProcessedFiles { get; set; }

    [JsonPropertyName("currentFile")]
    public string? CurrentFile { get; set; }

    [JsonPropertyName("progressPercentage")]
    public double? ProgressPercentage { get; set; }

    [JsonPropertyName("parameters")]
    public IntegrityCheckRunParameters? Parameters { get; set; }

    [JsonPropertyName("files")]
    public List<IntegrityFileResult> Files { get; set; } = new();
}

public class MediaIntegrityService : IDisposable
{
    private readonly ConfigManager _configManager;
    private readonly WebsocketManager _websocketManager;
    private readonly ArrManager _arrManager;
    private readonly UsenetStreamingClient _usenetClient;
    private CancellationTokenSource _cancellationTokenSource;
    private readonly MediaIntegrityBackgroundScheduler _backgroundScheduler;

    public MediaIntegrityService(
        ConfigManager configManager,
        WebsocketManager websocketManager,
        ArrManager arrManager,
        UsenetStreamingClient usenetClient,
        MediaIntegrityBackgroundScheduler backgroundScheduler
    )
    {
        _configManager = configManager;
        _websocketManager = websocketManager;
        _arrManager = arrManager;
        _usenetClient = usenetClient;
        _cancellationTokenSource = SetCancellationTokenSource();
        _backgroundScheduler = backgroundScheduler;
    }

    private CancellationTokenSource SetCancellationTokenSource()
    {
        return CancellationTokenSource.CreateLinkedTokenSource(SigtermUtil.GetCancellationToken());
    }

    public async Task<bool> IsManualCheckRunningAsync()
    {
        var (isRunning, taskType) = await IntegrityCheckSemaphore.GetRunningTaskStatusAsync();
        return isRunning && taskType == "Manual";
    }

    public async Task<bool> TriggerManualIntegrityCheckWithRunIdAsync(IntegrityCheckRunParameters? parameters, string runId)
    {
        // Use provided parameters or get defaults
        var runParams = parameters ?? _configManager.GetDefaultRunParameters();

        // Get the list of files to check
        // Send scanning message to inform frontend we're discovering files
        _ = _websocketManager.SendMessage(WebsocketTopic.IntegrityCheckProgress, $"scanning:{runId}");

        var fileScanner = new MediaIntegrityFileScanner(_configManager, runParams);
        var checkItems = await fileScanner.GetIntegrityCheckItemsAsync(_cancellationTokenSource.Token);

        // Check if there are no files to process
        if (checkItems.Count == 0)
        {
            Log.Information("Manual integrity check skipped: no files eligible for checking (runId: {RunId})", runId);
            _ = _websocketManager.SendMessage(WebsocketTopic.IntegrityCheckProgress, $"no_files:{runId}");

            // Send completion message for consistency with successful runs
            _ = _websocketManager.SendMessage(WebsocketTopic.IntegrityCheckProgress, $"complete: 0/0:{runId}");

            return false; // Return false to indicate the run didn't start
        }

        // Create the task but don't start it yet
        var checker = new MediaIntegrityChecker(_configManager, _websocketManager, _arrManager, _usenetClient);
        var integrityTask = new Task(() =>
        {
            // Set reserved connections context for this integrity check
            var reservedConnections = _configManager.GetMaxConnections() - _configManager.GetMaxQueueConnections();
            using var _ = _cancellationTokenSource.Token.SetScopedContext(new ReservedConnectionsContext(reservedConnections));

            // Run the async work synchronously within this task
            checker.PerformIntegrityCheckAsync(_cancellationTokenSource.Token, runId, checkItems, runParams).GetAwaiter().GetResult();
        }, _cancellationTokenSource.Token);

        // Try to register and start the task
        var taskStarted = await IntegrityCheckSemaphore.TryStartTaskAsync(integrityTask, "Manual", _cancellationTokenSource, _cancellationTokenSource.Token);

        if (!taskStarted)
        {
            Log.Information("Manual integrity check skipped: another task is already running (runId: {RunId})", runId);
            _ = _websocketManager.SendMessage(WebsocketTopic.IntegrityCheckProgress, $"failed: Already running:{runId}");
            return false;
        }

        Log.Information("Manual integrity check started successfully (runId: {RunId})", runId);
        return true;
    }

    public async Task<bool> CancelIntegrityCheckAsync()
    {
        // Try to cancel any running integrity check (manual or scheduled)
        var wasCancelled = await IntegrityCheckSemaphore.CancelRunningTaskAsync();

        if (wasCancelled)
        {
            Log.Information("Cancelled active integrity check");

            // Reset our manual cancellation token source for future operations
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = SetCancellationTokenSource();

            return true;
        }

        return false; // No active task to cancel
    }

    public async Task<IntegrityRunStatus?> GetRunStatusAsync(string runId)
    {
        await using var dbContext = new DavDatabaseContext();
        var dbClient = new DavDatabaseClient(dbContext);

        // Get run from the new table
        var run = await dbClient.Ctx.IntegrityCheckRuns
            .FirstOrDefaultAsync(r => r.RunId == runId);

        if (run == null)
        {
            return null; // Run not found
        }

        // Check if this is the currently running task
        var (isRunning, taskType) = await IntegrityCheckSemaphore.GetRunningTaskStatusAsync();
        var isCurrentlyRunning = isRunning && taskType == "Manual" && run.IsRunning;

        // Convert to parameters object for compatibility
        var parameters = new IntegrityCheckRunParameters
        {
            ScanDirectory = run.ScanDirectory,
            MaxFilesToCheck = run.MaxFilesToCheck,
            CorruptFileAction = run.CorruptFileAction,
            Mp4DeepScan = run.Mp4DeepScan,
            AutoMonitor = run.AutoMonitor,
            UnmonitorValidatedFiles = run.UnmonitorValidatedFiles,
            DirectDeletionFallback = run.DirectDeletionFallback,
            NzbSegmentSamplePercentage = run.NzbSegmentSamplePercentage,
            NzbSegmentThresholdPercentage = run.NzbSegmentThresholdPercentage,
            RunType = run.RunType
        };

        // Get files for this specific run
        var runFiles = await dbClient.Ctx.IntegrityCheckFileResults
            .Where(f => f.RunId == runId)
            .OrderByDescending(f => f.LastChecked)
            .ToListAsync();

        // Convert to the expected IntegrityFileResult format
        var fileResults = runFiles.Select(f => new IntegrityFileResult
        {
            FileId = f.FileId,
            FilePath = f.FilePath,
            FileName = f.FileName,
            IsLibraryFile = f.IsLibraryFile,
            LastChecked = f.LastChecked.ToUniversalTime().ToString("O"),
            Status = f.Status,
            ErrorMessage = f.ErrorMessage,
            ActionTaken = f.ActionTaken,
            RunId = f.RunId
        }).ToList();

        // Calculate counters from actual stored file results for accuracy
        var actualValidFiles = fileResults.Count(f => f.Status == IntegrityCheckFileResult.StatusOption.Valid);
        var actualCorruptFiles = fileResults.Count(f => f.Status == IntegrityCheckFileResult.StatusOption.Corrupt);
        var actualTotalFiles = fileResults.Count;

        var result = new IntegrityRunStatus
        {
            RunId = run.RunId,
            IsRunning = isCurrentlyRunning,
            Status = run.Status, // JsonStringEnumConverter handles serialization
            StartTime = run.StartTime.ToUniversalTime().ToString("O"),
            EndTime = run.EndTime?.ToUniversalTime().ToString("O"),
            TotalFiles = Math.Max(run.TotalFiles, actualTotalFiles), // Use higher value for more accuracy
            ValidFiles = actualValidFiles, // Use actual count from stored results
            CorruptFiles = actualCorruptFiles, // Use actual count from stored results
            ProcessedFiles = actualTotalFiles, // Actual processed files
            CurrentFile = run.CurrentFile,
            ProgressPercentage = run.ProgressPercentage,
            Parameters = parameters,
            Files = fileResults
        };

        return result;
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _backgroundScheduler?.Dispose();
    }
}
