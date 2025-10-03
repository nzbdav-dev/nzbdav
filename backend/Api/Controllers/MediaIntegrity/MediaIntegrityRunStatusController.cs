using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Api.Controllers;
using NzbWebDAV.Tasks;

namespace NzbWebDAV.Api.Controllers.MediaIntegrity;

[ApiController]
[Route("api/media-integrity/run/{runId}/status")]
public class MediaIntegrityRunStatusController(MediaIntegrityService integrityService) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        var runId = RouteData.Values["runId"]?.ToString();

        if (string.IsNullOrEmpty(runId))
        {
            return BadRequest("Run ID is required");
        }

        var runStatus = await integrityService.GetRunStatusAsync(runId);

        if (runStatus == null)
        {
            return NotFound($"Run with ID {runId} not found");
        }

        // Generate ETag based on key changeable fields
        var etag = GenerateETag(runStatus);

        // Check If-None-Match header for conditional requests
        var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();

        if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch.Contains(etag))
        {
            return StatusCode(304); // Not Modified
        }

        // Set ETag header for client caching
        Response.Headers.ETag = $"\"{etag}\"";

        return Ok(runStatus);
    }

    private static string GenerateETag(IntegrityRunStatus runStatus)
    {
        // Include key fields that change during run execution
        var etagData = $"{runStatus.RunId}|{runStatus.IsRunning}|{runStatus.ValidFiles}|{runStatus.CorruptFiles}|{runStatus.ProcessedFiles}|{runStatus.Files.Count}|{runStatus.EndTime}|{runStatus.ProgressPercentage}";

        // Generate a hash of the data for a compact ETag
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(etagData));
        return Convert.ToHexString(hash)[..16]; // Use first 16 characters for shorter ETag
    }
}
