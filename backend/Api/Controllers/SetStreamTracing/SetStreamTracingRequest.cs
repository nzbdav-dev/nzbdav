using Microsoft.AspNetCore.Http;

namespace NzbWebDAV.Api.Controllers.SetStreamTracing;

public sealed class SetStreamTracingRequest
{
    public bool Enabled { get; }
    public int Minutes { get; }
    public int Capacity { get; }

    public SetStreamTracingRequest(HttpContext context)
    {
        var enabledRaw = context.Request.Form["enabled"].FirstOrDefault()
            ?? context.Request.Query["enabled"].FirstOrDefault()
            ?? "false";
        Enabled = string.Equals(enabledRaw, "true", StringComparison.OrdinalIgnoreCase)
            || enabledRaw == "1";

        var minutesRaw = context.Request.Form["minutes"].FirstOrDefault()
            ?? context.Request.Query["minutes"].FirstOrDefault();
        if (!int.TryParse(minutesRaw, out var minutes)
            || !Services.StreamTrace.StreamTraceBuffer.AllowedUiMinutes.Contains(minutes))
        {
            minutes = 30;
        }

        Minutes = minutes;

        var capacityRaw = context.Request.Form["capacity"].FirstOrDefault()
            ?? context.Request.Query["capacity"].FirstOrDefault();
        Capacity = int.TryParse(capacityRaw, out var capacity)
                   && Services.StreamTrace.StreamTraceBuffer.AllowedUiCapacities.Contains(capacity)
            ? capacity
            : Services.StreamTrace.StreamTraceBuffer.DefaultUiCapacity;
    }
}
