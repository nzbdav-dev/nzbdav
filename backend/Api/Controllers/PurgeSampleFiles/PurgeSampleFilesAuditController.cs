using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Tasks;

namespace NzbWebDAV.Api.Controllers.PurgeSampleFiles;

[ApiController]
[Route("api/purge-sample-files/audit")]
public class PurgeSampleFilesAuditController(
) : BaseApiController
{
    protected override Task<IActionResult> HandleRequest()
    {
        var report = PurgeSampleFilesTask.GetAuditReport();
        return Task.FromResult<IActionResult>(Ok(report));
    }
}