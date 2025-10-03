using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Tasks;
using Serilog;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NzbWebDAV.Api.Controllers.MediaIntegrity;

[ApiController]
[Route("api/media-integrity/parameters")]
public class MediaIntegrityParametersController(ConfigManager configManager) : BaseApiController
{
    protected override Task<IActionResult> HandleRequest()
    {
        var parameters = configManager.GetDefaultRunParameters();
        return Task.FromResult<IActionResult>(Ok(parameters));
    }
}

[ApiController]
[Route("api/media-integrity/run")]
public class MediaIntegrityRunController(MediaIntegrityService integrityService, ConfigManager configManager) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        IntegrityCheckRunParameters? parameters = null;

        // Try to parse parameters from request body if provided
        if (Request.ContentLength > 0)
        {
            try
            {
                await using var stream = Request.Body;
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
                };
                parameters = await JsonSerializer.DeserializeAsync<IntegrityCheckRunParameters>(stream, jsonOptions);
            }
            catch (JsonException ex)
            {
                return BadRequest($"Invalid JSON in request body: {ex.Message}");
            }
        }

        // Merge provided parameters with defaults (prioritize provided values)
        var defaults = configManager.GetDefaultRunParameters();
        var runParams = parameters != null
            ? new IntegrityCheckRunParameters
            {
                ScanDirectory = parameters.ScanDirectory ?? defaults.ScanDirectory,
                MaxFilesToCheck = parameters.MaxFilesToCheck > 0 ? parameters.MaxFilesToCheck : defaults.MaxFilesToCheck,
                CorruptFileAction = parameters.CorruptFileAction, // Use provided value (already has default in deserialization)
                Mp4DeepScan = parameters.Mp4DeepScan,
                AutoMonitor = parameters.AutoMonitor,
                UnmonitorValidatedFiles = parameters.UnmonitorValidatedFiles,
                DirectDeletionFallback = parameters.DirectDeletionFallback,
                NzbSegmentSamplePercentage = parameters.NzbSegmentSamplePercentage > 0 ? parameters.NzbSegmentSamplePercentage : defaults.NzbSegmentSamplePercentage,
                NzbSegmentThresholdPercentage = parameters.NzbSegmentThresholdPercentage > 0 ? parameters.NzbSegmentThresholdPercentage : defaults.NzbSegmentThresholdPercentage,
                RunType = parameters.RunType
            }
            : defaults;

        // Generate a unique run ID
        var runId = Guid.NewGuid().ToString();

        var integrityRun = new IntegrityCheckRun
        {
            RunId = runId,
            StartTime = DateTime.UtcNow,
            RunType = runParams.RunType,
            ScanDirectory = runParams.ScanDirectory,
            MaxFilesToCheck = runParams.MaxFilesToCheck,
            CorruptFileAction = runParams.CorruptFileAction,
            Mp4DeepScan = runParams.Mp4DeepScan,
            AutoMonitor = runParams.AutoMonitor,
            UnmonitorValidatedFiles = runParams.UnmonitorValidatedFiles,
            DirectDeletionFallback = runParams.DirectDeletionFallback,
            NzbSegmentSamplePercentage = runParams.NzbSegmentSamplePercentage,
            NzbSegmentThresholdPercentage = runParams.NzbSegmentThresholdPercentage,
            ValidFiles = 0,
            CorruptFiles = 0,
            TotalFiles = 0,
            IsRunning = false, // Will be set to true when task actually starts
            Status = IntegrityCheckRun.StatusOption.Initialized // New state before "started"
        };

        await using var dbContext = new DavDatabaseContext();

        var dbClient = new DavDatabaseClient(dbContext);
        dbClient.Ctx.IntegrityCheckRuns.Add(integrityRun);

        await dbClient.Ctx.SaveChangesAsync();

        // Trigger the background task asynchronously without waiting
        _ = Task.Run(async () =>
        {
            try
            {
                var started = await integrityService.TriggerManualIntegrityCheckWithRunIdAsync(runParams, runId);
                if (!started)
                {
                    // If task couldn't start, update status to failed
                    // (websocket message already sent by the service)
                    await using var failDbContext = new DavDatabaseContext();
                    var failDbClient = new DavDatabaseClient(failDbContext);
                    var failedRun = await failDbClient.Ctx.IntegrityCheckRuns
                        .FirstOrDefaultAsync(r => r.RunId == runId);

                    if (failedRun != null)
                    {
                        failedRun.Status = IntegrityCheckRun.StatusOption.Failed;
                        failedRun.IsRunning = false;
                        await failDbClient.Ctx.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                // Log any unexpected errors in the background task
                Log.Error(ex, "Error in background integrity check task for run {RunId}", runId);

                // Update status to failed
                try
                {
                    await using var failDbContext = new DavDatabaseContext();
                    var failDbClient = new DavDatabaseClient(failDbContext);
                    var failedRun = await failDbClient.Ctx.IntegrityCheckRuns
                        .FirstOrDefaultAsync(r => r.RunId == runId);

                    if (failedRun != null)
                    {
                        failedRun.Status = IntegrityCheckRun.StatusOption.Failed;
                        failedRun.IsRunning = false;
                        await failDbClient.Ctx.SaveChangesAsync();
                    }
                }
                catch (Exception dbEx)
                {
                    Log.Error(dbEx, "Failed to update failed run status for {RunId}", runId);
                }
            }
        });

        // Return immediately with the run ID - frontend will get status updates via websockets
        return Ok(new MediaIntegrityRunResponse
        {
            Message = "Media integrity check queued successfully",
            Started = true,
            RunDetails = null // Frontend will get status via websockets
        });
    }
}

