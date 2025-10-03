using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Api.Controllers;
using NzbWebDAV.Tasks;
using Serilog;

namespace NzbWebDAV.Api.Controllers.MediaIntegrity;

[ApiController]
[Route("api/media-integrity/cancel")]
public class CancelIntegrityCheckController(MediaIntegrityService mediaIntegrityService) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        Log.Information("Received request to cancel integrity check");

        var cancelled = await mediaIntegrityService.CancelIntegrityCheckAsync();

        if (cancelled)
        {
            Log.Information("Integrity check cancellation initiated successfully");
            return Ok(new { success = true, message = "Integrity check cancellation initiated" });
        }
        else
        {
            Log.Information("No active integrity check found to cancel");
            return Ok(new { success = false, message = "No active integrity check found" });
        }
    }
}
