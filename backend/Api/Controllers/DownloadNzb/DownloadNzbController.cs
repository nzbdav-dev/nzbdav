using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;

namespace NzbWebDAV.Api.Controllers.DownloadNzb;

[ApiController]
[Route("api/download-nzb")]
public class DownloadNzbController(DavDatabaseContext dbContext) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        // Parse nzbBlobId from query string
        var nzbBlobIdStr = HttpContext.Request.Query["nzbBlobId"].FirstOrDefault();
        if (!Guid.TryParse(nzbBlobIdStr, out var nzbBlobId))
            throw new BadHttpRequestException("Missing or invalid nzbBlobId query parameter.");

        // Look up the original filename
        var nzbName = await dbContext.NzbNames
            .FirstOrDefaultAsync(x => x.Id == nzbBlobId, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (nzbName == null)
            throw new BadHttpRequestException("NZB not found.");

        // Open the NZB blob from the blob store
        var stream = BlobStore.ReadBlob(nzbBlobId);
        if (stream == null)
            throw new BadHttpRequestException("NZB file is no longer available.");

        return File(stream, "application/x-nzb", nzbName.FileName);
    }
}
