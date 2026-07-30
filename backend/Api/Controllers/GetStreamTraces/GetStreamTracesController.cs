using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Services.StreamTrace;

namespace NzbWebDAV.Api.Controllers.GetStreamTraces;

[ApiController]
[Route("api/get-stream-traces")]
public class GetStreamTracesController(StreamTraceBuffer buffer) : BaseApiController
{
    protected override Task<IActionResult> HandleRequest()
    {
        var limit = int.TryParse(HttpContext.Request.Query["limit"].ToString(), out var n)
            ? Math.Clamp(n, 1, 500)
            : 50;

        var status = buffer.GetStatus();
        var sessions = buffer.ListSessions(limit);
        return Task.FromResult<IActionResult>(Ok(new GetStreamTracesResponse
        {
            Status = true,
            Enabled = status.Enabled,
            Retained = status.Retained,
            Source = status.Source,
            ExpiresAtUnixMs = status.ExpiresAtUnixMs,
            RetainedUntilUnixMs = status.RetainedUntilUnixMs,
            Capacity = status.Capacity,
            EventCount = status.EventCount,
            SessionCount = status.SessionCount,
            RetainedEventCount = status.RetainedEventCount,
            OverwrittenEventCount = status.OverwrittenEventCount,
            OldestRetainedSequence = status.OldestRetainedSequence,
            NewestRetainedSequence = status.NewestRetainedSequence,
            OldestRetainedAtUnixMs = status.OldestRetainedAtUnixMs,
            NewestRetainedAtUnixMs = status.NewestRetainedAtUnixMs,
            Overflowed = status.Overflowed,
            Sessions = sessions.Select(s => new StreamTraceSessionDto
            {
                SessionId = s.SessionId,
                Path = s.Path,
                FirstAt = s.FirstAt,
                LastAt = s.LastAt,
                EventCount = s.EventCount,
                LastKind = s.LastKind,
            }).ToList(),
        }));
    }
}
