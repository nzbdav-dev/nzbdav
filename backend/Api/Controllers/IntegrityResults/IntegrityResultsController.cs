using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Api.Controllers;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Tasks;
using Serilog;
using System.Text.Json;

namespace NzbWebDAV.Api.Controllers.IntegrityResults;

[ApiController]
[Route("api/integrity-results")]
public class IntegrityResultsController(DavDatabaseClient dbClient) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        // Get runs from the new IntegrityCheckRuns table
        var integrityRuns = await dbClient.Ctx.IntegrityCheckRuns
            .OrderByDescending(r => r.StartTime)
            .ToListAsync(HttpContext.RequestAborted);

        // Get all file results from the new IntegrityCheckFileResults table
        var allFileResults = await dbClient.Ctx.IntegrityCheckFileResults
            .OrderByDescending(f => f.LastChecked)
            .ToListAsync(HttpContext.RequestAborted);

        // Convert to the expected IntegrityFileResult format
        var fileResults = allFileResults.Select(f => new IntegrityFileResult
        {
            FileId = f.FileId,
            FilePath = f.FilePath,
            FileName = f.FileName,
            IsLibraryFile = f.IsLibraryFile,
            LastChecked = f.LastChecked.ToUniversalTime().ToString("O"), // Convert to UTC string
            Status = f.Status,
            ErrorMessage = f.ErrorMessage,
            ActionTaken = f.ActionTaken,
            RunId = f.RunId
        }).ToList();

        // Create job runs from the IntegrityCheckRuns table
        var jobRuns = integrityRuns.Select(run =>
        {
            // Get files for this run from the file results
            var runFiles = fileResults
                .Where(f => f.RunId == run.RunId)
                .OrderByDescending(f => f.LastChecked)
                .ToList();

            // Convert run parameters to the expected format
            var parameters = new IntegrityCheckRunParameters
            {
                ScanDirectory = run.ScanDirectory,
                MaxFilesToCheck = run.MaxFilesToCheck,
                CorruptFileAction = run.CorruptFileAction,
                Mp4DeepScan = run.Mp4DeepScan,
                AutoMonitor = run.AutoMonitor,
                DirectDeletionFallback = run.DirectDeletionFallback,
                RunType = run.RunType
            };

            Log.Debug("Creating job run for runId {RunId} on {RunDate} with {FileCount} files (Start: {StartTime}, End: {EndTime})",
                run.RunId, run.StartTime.ToUniversalTime().ToString("O"), runFiles.Count, run.StartTime.ToUniversalTime().ToString("O"), run.EndTime?.ToUniversalTime().ToString("O"));

            return new IntegrityJobRun
            {
                Date = run.StartTime.ToUniversalTime().ToString("O"), // Convert to UTC string
                RunId = run.RunId,
                StartTime = run.StartTime.ToUniversalTime().ToString("O"), // Convert to UTC string
                EndTime = run.EndTime?.ToUniversalTime().ToString("O"), // Convert to UTC string
                TotalFiles = run.TotalFiles,
                CorruptFiles = run.CorruptFiles,
                ValidFiles = run.ValidFiles,
                Files = runFiles,
                Parameters = parameters
            };
        }).ToList();

        var response = new IntegrityResultsResponse
        {
            JobRuns = jobRuns,
            AllFiles = fileResults
        };

        return Ok(response);
    }
}
