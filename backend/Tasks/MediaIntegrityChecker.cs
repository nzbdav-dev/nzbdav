using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Clients;
using NzbWebDAV.Clients.Connections;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Streams;
using NzbWebDAV.Utils;
using NzbWebDAV.WebDav;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.Tasks;

public class MediaIntegrityChecker
{
    private readonly ConfigManager _configManager;
    private readonly WebsocketManager _websocketManager;
    private readonly ArrManager _arrManager;
    private readonly UsenetStreamingClient _usenetClient;

    public MediaIntegrityChecker(
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
    }

    public async Task PerformIntegrityCheckAsync(CancellationToken ct, string runId, List<IntegrityCheckItem> checkItems, IntegrityCheckRunParameters? parameters = null)
    {
        var startTime = DateTime.UtcNow;

        // Use provided parameters or get defaults
        var runParams = parameters ?? _configManager.GetDefaultRunParameters();

        // Declare counters outside try block so they're available in catch blocks
        var processedFiles = 0;
        var corruptFiles = 0;

        try
        {
            Log.Information("Starting media integrity check with run ID: {RunId}, type: {RunType}", runId, runParams.RunType);
            _ = _websocketManager.SendMessage(WebsocketTopic.IntegrityCheckProgress, $"starting:{runId}");

            // Update the existing run record to "started" status
            await using var dbContext = new DavDatabaseContext();
            var dbClient = new DavDatabaseClient(dbContext);

            var integrityRun = await dbClient.Ctx.IntegrityCheckRuns
                .FirstOrDefaultAsync(r => r.RunId == runId, ct);

            if (integrityRun == null)
            {
                Log.Error("Integrity run record not found for ID: {RunId}", runId);
                return;
            }

            // Update to "started" status
            integrityRun.IsRunning = true;
            integrityRun.Status = IntegrityCheckRun.StatusOption.Started;
            await dbClient.Ctx.SaveChangesAsync(ct);

            // Check if library directory is configured (required for arr integration)
            var scanDirectory = runParams.ScanDirectory ?? _configManager.GetLibraryDir();
            if (string.IsNullOrEmpty(scanDirectory) && runParams.CorruptFileAction == IntegrityCheckRun.CorruptFileActionOption.DeleteViaArr)
            {
                var errorMsg = "Scan directory must be configured for Radarr/Sonarr integration";
                Log.Error(errorMsg);
                _ = _websocketManager.SendMessage(WebsocketTopic.IntegrityCheckProgress, $"failed: {errorMsg}:{runId}");

                // Update run status to failed
                if (integrityRun != null)
                {
                    integrityRun.Status = IntegrityCheckRun.StatusOption.Failed;
                    integrityRun.IsRunning = false;
                    integrityRun.EndTime = DateTime.UtcNow;
                    await dbClient.Ctx.SaveChangesAsync(ct);
                }
                return;
            }

            // Log the file count and type
            if (!string.IsNullOrEmpty(scanDirectory) && Directory.Exists(scanDirectory))
            {
                Log.Information("Checking {Count} library files in: {ScanDirectory}", checkItems.Count, scanDirectory);
            }
            else
            {
                Log.Information("Checking {Count} internal nzbdav files", checkItems.Count);
            }

            if (checkItems.Count == 0)
            {
                Log.Information("No media files found to check");
                _ = _websocketManager.SendMessage(WebsocketTopic.IntegrityCheckProgress, $"complete: 0/0:{runId}");
                return;
            }

            var totalFiles = checkItems.Count;

            var debounce = DebounceUtil.CreateDebounce(TimeSpan.FromMilliseconds(500));

            foreach (var checkItem in checkItems)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var (isCorrupt, errorMessage) = await CheckFileIntegrityAsync(checkItem.DavItem, ct, checkItem.LibraryFilePath, runParams);

                    if (isCorrupt)
                    {
                        corruptFiles++;
                        Log.Warning("Corrupt media file detected: {FilePath} - {Error}", checkItem.DavItem.Path, errorMessage);

                        // Use library file path for arr integration, or symlink path for internal files
                        var filePath = checkItem.LibraryFilePath ??
                            DatabaseStoreSymlinkFile.GetTargetPath(checkItem.DavItem, _configManager.GetRcloneMountDir());
                        var actionTaken = await HandleCorruptFileAsync(checkItem.DavItem, filePath, runParams.CorruptFileAction, ct);

                        // Store error details and action taken
                        if (checkItem.LibraryFilePath != null)
                        {
                            await StoreFileResultAsync(checkItem.LibraryFilePath, checkItem.DavItem.Id.ToString(), true, false, errorMessage, actionTaken, runId, ct);
                        }
                        else
                        {
                            await StoreFileResultAsync(checkItem.DavItem.Path, checkItem.DavItem.Id.ToString(), false, false, errorMessage, actionTaken, runId, ct);
                        }
                    }
                    else
                    {
                        // For runs with unmonitor option enabled, unmonitor successfully validated files
                        if (runParams.UnmonitorValidatedFiles && checkItem.LibraryFilePath != null)
                        {
                            try
                            {
                                var unmonitorSuccess = await _arrManager.UnmonitorFileInArrAsync(checkItem.LibraryFilePath, ct);
                                if (unmonitorSuccess)
                                {
                                    Log.Debug("Successfully unmonitored validated file: {FilePath}", checkItem.LibraryFilePath);
                                }
                                else
                                {
                                    Log.Warning("Failed to unmonitor validated file: {FilePath}", checkItem.LibraryFilePath);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "Error unmonitoring validated file: {FilePath}", checkItem.LibraryFilePath);
                            }
                        }

                        // Store successful check details
                        if (checkItem.LibraryFilePath != null)
                        {
                            await StoreFileResultAsync(checkItem.LibraryFilePath, checkItem.DavItem.Id.ToString(), true, true, null, null, runId, ct);
                        }
                        else
                        {
                            await StoreFileResultAsync(checkItem.DavItem.Path, checkItem.DavItem.Id.ToString(), false, true, null, null, runId, ct);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Cancellation during file processing - propagate to main handler
                    // This includes TaskCanceledException which derives from OperationCanceledException
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error checking integrity of file: {FilePath}", checkItem.DavItem.Path);
                }

                processedFiles++;
                var progress = $"{processedFiles}/{totalFiles} ({corruptFiles} corrupt)";
                var progressPercentage = totalFiles > 0 ? (double)processedFiles / totalFiles * 100 : 0;

                // Update run progress in database
                var validFiles = processedFiles - corruptFiles;
                // Pass totalFiles on first update to set it in the database
                var totalFilesToPass = processedFiles == 1 ? totalFiles : (int?)null;
                await UpdateRunProgressAsync(runId, validFiles, corruptFiles, checkItem.DavItem.Path, progressPercentage, ct, totalFilesToPass);

                debounce(() => _websocketManager.SendMessage(WebsocketTopic.IntegrityCheckProgress, $"{progress}:{runId}"));
            }

            var finalReport = $"complete: {processedFiles}/{totalFiles} checked, {corruptFiles} corrupt files found";
            _ = _websocketManager.SendMessage(WebsocketTopic.IntegrityCheckProgress, $"{finalReport}:{runId}");
            Log.Information("Media integrity check completed: {Report}", finalReport);

            // Update run record with completion
            await using var endDbContext = new DavDatabaseContext();
            var endDbClient = new DavDatabaseClient(endDbContext);

            var completedRun = await endDbClient.Ctx.IntegrityCheckRuns
                .FirstOrDefaultAsync(r => r.RunId == runId);
            if (completedRun != null)
            {
                var finalValidFiles = processedFiles - corruptFiles;

                completedRun.EndTime = DateTime.UtcNow;
                completedRun.IsRunning = false;
                completedRun.Status = IntegrityCheckRun.StatusOption.Completed;
                completedRun.TotalFiles = processedFiles;
                completedRun.ValidFiles = finalValidFiles;
                completedRun.CorruptFiles = corruptFiles;
                completedRun.CurrentFile = null;
                completedRun.ProgressPercentage = null;

                Log.Information("Completing run {RunId}: TotalFiles={TotalFiles}, ValidFiles={ValidFiles}, CorruptFiles={CorruptFiles}",
                    runId, processedFiles, finalValidFiles, corruptFiles);

                await endDbClient.Ctx.SaveChangesAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            Log.Information("Media integrity check was cancelled after processing {ProcessedFiles} files", processedFiles);
            _ = _websocketManager.SendMessage(WebsocketTopic.IntegrityCheckProgress, $"cancelled:{runId}");

            // Update run record for cancellation with actual progress made
            try
            {
                await using var cancelDbContext = new DavDatabaseContext();
                var cancelDbClient = new DavDatabaseClient(cancelDbContext);

                var cancelledRun = await cancelDbClient.Ctx.IntegrityCheckRuns
                    .FirstOrDefaultAsync(r => r.RunId == runId, ct);
                if (cancelledRun != null)
                {
                    var finalValidFiles = processedFiles - corruptFiles;

                    cancelledRun.EndTime = DateTime.UtcNow;
                    cancelledRun.IsRunning = false;
                    cancelledRun.Status = IntegrityCheckRun.StatusOption.Cancelled;
                    cancelledRun.TotalFiles = processedFiles; // Actual files processed before cancellation
                    cancelledRun.ValidFiles = finalValidFiles;
                    cancelledRun.CorruptFiles = corruptFiles;
                    cancelledRun.CurrentFile = null;
                    cancelledRun.ProgressPercentage = null;

                    Log.Information("Cancelled run {RunId}: TotalFiles={TotalFiles}, ValidFiles={ValidFiles}, CorruptFiles={CorruptFiles}",
                        runId, processedFiles, finalValidFiles, corruptFiles);

                    await cancelDbClient.Ctx.SaveChangesAsync(CancellationToken.None);
                }
            }
            catch (Exception storeEx)
            {
                Log.Warning(storeEx, "Failed to update run record for cancelled run {RunId}", runId);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during media integrity check after processing {ProcessedFiles} files", processedFiles);
            _ = _websocketManager.SendMessage(WebsocketTopic.IntegrityCheckProgress, $"failed: {ex.Message}:{runId}");

            // Update run record for failure with actual progress made
            try
            {
                await using var failDbContext = new DavDatabaseContext();
                var failDbClient = new DavDatabaseClient(failDbContext);

                var failedRun = await failDbClient.Ctx.IntegrityCheckRuns
                    .FirstOrDefaultAsync(r => r.RunId == runId);
                if (failedRun != null)
                {
                    var finalValidFiles = processedFiles - corruptFiles;

                    failedRun.EndTime = DateTime.UtcNow;
                    failedRun.IsRunning = false;
                    failedRun.Status = IntegrityCheckRun.StatusOption.Failed;
                    failedRun.TotalFiles = processedFiles; // Actual files processed before failure
                    failedRun.ValidFiles = finalValidFiles;
                    failedRun.CorruptFiles = corruptFiles;
                    failedRun.CurrentFile = null;
                    failedRun.ProgressPercentage = null;

                    Log.Information("Failed run {RunId}: TotalFiles={TotalFiles}, ValidFiles={ValidFiles}, CorruptFiles={CorruptFiles}",
                        runId, processedFiles, finalValidFiles, corruptFiles);

                    await failDbClient.Ctx.SaveChangesAsync(CancellationToken.None);
                }
            }
            catch (Exception storeEx)
            {
                Log.Warning(storeEx, "Failed to update run record for failed run {RunId}", runId);
            }
        }
    }

    private async Task<(bool isCorrupt, string? errorMessage)> CheckFileIntegrityAsync(DavItem davItem, CancellationToken ct, string? libraryFilePath = null, IntegrityCheckRunParameters? runParams = null)
    {
        // Check if it's a media file type we can verify
        if (!FilenameUtil.IsVideoFile(davItem.Name))
        {
            return (false, null); // Not a media file, consider it valid
        }

        try
        {
            Log.Information("Checking file integrity for {FilePath} (ID: {Id})", davItem.Path, davItem.Id);

            Stream stream;
            if (davItem.Type == DavItem.ItemType.NzbFile)
            {
                await using var dbContext = new DavDatabaseContext();
                var dbClient = new DavDatabaseClient(dbContext);
                var nzbFile = await dbClient.GetNzbFileAsync(davItem.Id, ct);
                if (nzbFile == null)
                {
                    Log.Warning("Could not find NZB file data for {FilePath} (ID: {Id})", davItem.Path, davItem.Id);
                    return (true, "NZB file data not found in database"); // Consider missing NZB data as corrupt
                }

                var samplePercentage = runParams?.NzbSegmentSamplePercentage ?? _configManager.GetNzbSegmentSamplePercentage();
                var thresholdPercentage = runParams?.NzbSegmentThresholdPercentage ?? _configManager.GetNzbSegmentThresholdPercentage();
                var articlesArePresent = await _usenetClient.CheckNzbFileHealth(nzbFile.SegmentIds, samplePercentage, thresholdPercentage, ct);
                if (!articlesArePresent)
                {
                    Log.Warning("Missing usenet articles detected for {FilePath}: {Message}", davItem.Path, "NZB file is missing articles");
                    return (true, "NZB file is missing articles"); // Mark as corrupt
                }

                stream = _usenetClient.GetFileStream(nzbFile.SegmentIds, davItem.FileSize!.Value, _configManager.GetConnectionsPerStream());
            }
            else if (davItem.Type == DavItem.ItemType.RarFile)
            {
                await using var dbContext = new DavDatabaseContext();
                var dbClient = new DavDatabaseClient(dbContext);
                var rarFile = await dbClient.Ctx.RarFiles.Where(x => x.Id == davItem.Id).FirstOrDefaultAsync(ct);
                if (rarFile == null)
                {
                    Log.Warning("Could not find RAR file data for {FilePath} (ID: {Id})", davItem.Path, davItem.Id);
                    return (true, "RAR file data not found in database"); // Consider missing RAR data as corrupt
                }

                var samplePercentage = runParams?.NzbSegmentSamplePercentage ?? _configManager.GetNzbSegmentSamplePercentage();
                var thresholdPercentage = runParams?.NzbSegmentThresholdPercentage ?? _configManager.GetNzbSegmentThresholdPercentage();
                var articlesArePresent = await _usenetClient.CheckNzbFileHealth(rarFile.GetSegmentIds(), samplePercentage, thresholdPercentage, ct);
                if (!articlesArePresent)
                {
                    Log.Warning("Missing usenet articles detected for {FilePath}: {Message}", davItem.Path, "RAR file is missing articles");
                    return (true, "RAR file is missing articles"); // Mark as corrupt
                }

                stream = new RarFileStream(rarFile.RarParts, _usenetClient, _configManager.GetConnectionsPerStream());
            }
            else
            {
                Log.Debug("Skipping integrity check for unsupported file type: {FilePath} (Type: {ItemType})", davItem.Path, davItem.Type);
                return (false, null); // Consider unsupported types as valid
            }

            // Use FFMpegCore to analyze the entire stream for media integrity
            var enableMp4DeepScan = _configManager.IsMp4DeepScanEnabled();
            // Use library file path for validation if available, otherwise fall back to DavItem path
            var pathForValidation = libraryFilePath ?? davItem.Path;
            var isValid = await FfprobeUtil.IsValidMediaStreamAsync(stream, pathForValidation, enableMp4DeepScan, ct);

            // Clean up the stream
            await stream.DisposeAsync();

            var isCorrupt = !isValid;

            if (isCorrupt)
            {
                Log.Warning("File integrity check FAILED for {FilePath}: Invalid or corrupt media content", davItem.Path);
                return (true, "FFmpeg validation failed - invalid or corrupt media content");
            }
            else
            {
                Log.Information("File integrity check PASSED for {FilePath}", davItem.Path);
                return (false, null);
            }
        }
        catch (UsenetArticleNotFoundException ex)
        {
            Log.Warning("Missing usenet articles detected for {FilePath}: {Message}", davItem.Path, ex.Message);

            // Missing articles mean the file is definitely corrupt/incomplete
            // This is similar to how the download process handles missing articles
            return (true, $"Missing usenet articles: {ex.Message}"); // Mark as corrupt
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error running ffprobe on {FilePath}", davItem.Path);

            // If ffprobe is not found or can't be started, this is a configuration issue
            // We should treat this as a problem that needs attention
            if (ex is System.ComponentModel.Win32Exception win32Ex && win32Ex.NativeErrorCode == 2)
            {
                Log.Error("ffprobe not found - please ensure FFmpeg is installed. File integrity cannot be verified: {FilePath}", davItem.Path);
                // Return true (corrupt) to indicate there's an issue that needs attention
                // This prevents silent failures where users think files are checked but they're not
                return (true, "FFmpeg/ffprobe not found - please ensure FFmpeg is installed");
            }

            // For other errors, assume file is ok but log the issue
            return (false, $"Error during integrity check: {ex.Message}");
        }
    }

    private async Task<IntegrityCheckFileResult.ActionOption> HandleCorruptFileAsync(DavItem? davItem, string filePath, IntegrityCheckRun.CorruptFileActionOption corruptFileAction, CancellationToken ct)
    {
        var isAutoMonitorEnabled = _configManager.IsAutoMonitorEnabled();

        Log.Information("HandleCorruptFileAsync: action={Action}, autoMonitorEnabled={AutoMonitorEnabled}, filePath={FilePath}",
            corruptFileAction, isAutoMonitorEnabled, filePath);

        // Auto-monitor corrupt files before deletion if enabled (for re-download)
        if (isAutoMonitorEnabled && (corruptFileAction == IntegrityCheckRun.CorruptFileActionOption.Delete || corruptFileAction == IntegrityCheckRun.CorruptFileActionOption.DeleteViaArr))
        {
            await _arrManager.MonitorFileInArrAsync(filePath, ct);
        }

        switch (corruptFileAction)
        {
            case IntegrityCheckRun.CorruptFileActionOption.Delete:
                return await HandleDeleteFileAsync(davItem, filePath, ct);

            case IntegrityCheckRun.CorruptFileActionOption.DeleteViaArr:
                return await HandleDeleteViaArrAsync(davItem, filePath, ct);

            case IntegrityCheckRun.CorruptFileActionOption.Log:
            default:
                // Just log the issue (already done above)
                return IntegrityCheckFileResult.ActionOption.None;
        }
    }

    private async Task<IntegrityCheckFileResult.ActionOption> HandleDeleteViaArrAsync(DavItem? davItem, string filePath, CancellationToken ct)
    {
        try
        {
            Log.Information("Attempting to delete corrupt file via Radarr/Sonarr: {FilePath}", filePath);
            var success = await _arrManager.DeleteFileFromArrAsync(filePath, ct);

            if (success)
            {
                Log.Information("Successfully deleted corrupt file via Radarr/Sonarr: {FilePath}", filePath);
                // Remove from database since the file was deleted via arr (if this is a DavItem)
                if (davItem != null)
                {
                    await using var dbContext = new DavDatabaseContext();
                    var dbClient = new DavDatabaseClient(dbContext);
                    dbClient.Ctx.Items.Remove(davItem);
                    await dbClient.Ctx.SaveChangesAsync(ct);
                }
                return IntegrityCheckFileResult.ActionOption.FileDeletedViaArr;
            }
            else
            {
                Log.Warning("Failed to delete corrupt file via Radarr/Sonarr: {FilePath}", filePath);

                // Check if direct deletion fallback is enabled
                if (_configManager.IsDirectDeletionFallbackEnabled())
                {
                    Log.Information("Direct deletion fallback is enabled, deleting file directly: {FilePath}", filePath);
                    // Fallback to direct deletion
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        Log.Information("Deleted corrupt file directly as fallback: {FilePath}", filePath);
                    }

                    // Remove from database (if this is a DavItem)
                    if (davItem != null)
                    {
                        await using var dbContext = new DavDatabaseContext();
                        var dbClient = new DavDatabaseClient(dbContext);
                        dbClient.Ctx.Items.Remove(davItem);
                        await dbClient.Ctx.SaveChangesAsync(ct);
                    }
                    return IntegrityCheckFileResult.ActionOption.DeleteFailedDirectFallback;
                }
                else
                {
                    Log.Information("Direct deletion fallback is disabled, leaving corrupt file in place: {FilePath}", filePath);
                    Log.Warning("Corrupt file was not deleted by Radarr/Sonarr and direct deletion fallback is disabled. File remains: {FilePath}", filePath);
                    return IntegrityCheckFileResult.ActionOption.DeleteFailedNoFallback;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting corrupt file via Radarr/Sonarr: {FilePath}", filePath);
            return IntegrityCheckFileResult.ActionOption.DeleteError;
        }
    }

    private async Task<IntegrityCheckFileResult.ActionOption> HandleDeleteFileAsync(DavItem? davItem, string filePath, CancellationToken ct)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Log.Information("Deleted corrupt file: {FilePath}", filePath);
            }

            // Remove from database if this is a DavItem
            if (davItem != null)
            {
                await using var dbContext = new DavDatabaseContext();
                var dbClient = new DavDatabaseClient(dbContext);
                dbClient.Ctx.Items.Remove(davItem);
                await dbClient.Ctx.SaveChangesAsync(ct);
            }
            return IntegrityCheckFileResult.ActionOption.FileDeletedSuccessfully;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting corrupt file: {FilePath}", filePath);
            return IntegrityCheckFileResult.ActionOption.DeleteError;
        }
    }

    private async Task StoreFileResultAsync(string filePath, string fileId, bool isLibraryFile, bool isValid, string? errorMessage, IntegrityCheckFileResult.ActionOption? actionTaken, string runId, CancellationToken ct)
    {
        try
        {
            var fileName = Path.GetFileName(filePath);
            var status = isValid ? IntegrityCheckFileResult.StatusOption.Valid : IntegrityCheckFileResult.StatusOption.Corrupt;

            var fileResult = new IntegrityCheckFileResult
            {
                RunId = runId,
                FileId = fileId,
                FilePath = filePath,
                FileName = fileName,
                IsLibraryFile = isLibraryFile,
                LastChecked = DateTime.UtcNow,
                Status = status,
                ErrorMessage = errorMessage,
                ActionTaken = actionTaken
            };

            await using var dbContext = new DavDatabaseContext();
            var dbClient = new DavDatabaseClient(dbContext);
            dbClient.Ctx.IntegrityCheckFileResults.Add(fileResult);
            await dbClient.Ctx.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Cancellation during database save is expected during check cancellation
            // Don't log this as an error - just rethrow to propagate cancellation
            // This includes TaskCanceledException which derives from OperationCanceledException
            throw;
        }
    }

    private async Task UpdateRunProgressAsync(string runId, int validFiles, int corruptFiles, string? currentFile, double? progressPercentage, CancellationToken ct, int? totalFiles = null)
    {
        try
        {
            await using var dbContext = new DavDatabaseContext();
            var dbClient = new DavDatabaseClient(dbContext);

            var run = await dbClient.Ctx.IntegrityCheckRuns
                .FirstOrDefaultAsync(r => r.RunId == runId, ct);

            if (run != null)
            {
                run.ValidFiles = validFiles;
                run.CorruptFiles = corruptFiles;
                run.CurrentFile = currentFile;
                run.ProgressPercentage = progressPercentage;

                // Set total files if provided (only on first update)
                if (totalFiles.HasValue && run.TotalFiles == 0)
                {
                    run.TotalFiles = totalFiles.Value;
                }

                await dbClient.Ctx.SaveChangesAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation during progress update is expected during check cancellation
            // Don't log this - it's normal behavior
            // This includes TaskCanceledException which derives from OperationCanceledException
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to update run progress for {RunId}", runId);
        }
    }
}
