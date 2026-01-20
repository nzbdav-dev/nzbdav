using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Database;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Api.Controllers.GetDatabaseBackup;

[ApiController]
[Route("api/db.sqlite")]
public class GetDatabaseBackupController() : BaseApiController
{
    private IActionResult GetDatabaseBackup()
    {
        // This endpoint allows downloading a backup of the database.
        // It is disabled by default and can only be enabled by the env variable below.
        if (!EnvironmentUtil.IsVariableTrue("DANGEROUS_ENABLE_DATABASE_DOWNLOAD_ENDPOINT"))
            return Forbid("This endpoint is not enabled.");

        var filepath = DavDatabaseContext.DatabaseFilePath;
        return System.IO.File.Exists(filepath)
            ? PhysicalFile(filepath, "application/octet-stream", Path.GetFileName(filepath))
            : NotFound($"Path not found: `{filepath}`.");
    }

    protected override Task<IActionResult> HandleRequest()
    {
        return Task.FromResult(GetDatabaseBackup());
    }
}