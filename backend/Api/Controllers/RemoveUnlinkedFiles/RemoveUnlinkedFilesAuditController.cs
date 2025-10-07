using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Tasks;

namespace NzbWebDAV.Api.Controllers.RemoveUnlinkedFiles;

[ApiController]
[Route("api/remove-unlinked-files/audit")]
public class RemoveUnlinkedFilesAuditController(
) : BaseApiController
{
    protected override Task<IActionResult> HandleRequest()
    {
        var report = RemoveUnlinkedFilesTask.GetAuditReport();
        return Task.FromResult<IActionResult>(Ok(report));
    }
}