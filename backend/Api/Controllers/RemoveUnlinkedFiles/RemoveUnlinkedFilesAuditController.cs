using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Tasks;
using NzbWebDAV.Websocket;

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