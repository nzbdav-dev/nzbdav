using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Clients.Usenet;

namespace NzbWebDAV.Api.Controllers.GetServerHealth;

[ApiController]
[Route("api/get-server-health")]
public class GetServerHealthController(UsenetStreamingClient usenetClient) : BaseApiController
{
    protected override async Task<IActionResult> HandleRequest()
    {
        var serverConfigs = usenetClient.GetServerConfigs();
        var healthStats = usenetClient.GetServerHealthStats();

        var response = new GetServerHealthResponse
        {
            Servers = serverConfigs.Select(config =>
            {
                var health = healthStats.FirstOrDefault(h => h.ServerId == config.Id);
                return new ServerHealthInfo
                {
                    Id = config.Id,
                    Name = config.Name,
                    Host = config.Host,
                    Port = config.Port,
                    Priority = config.Priority,
                    MaxConnections = config.MaxConnections,
                    IsAvailable = health?.IsAvailable ?? true,
                    ConsecutiveFailures = health?.ConsecutiveFailures ?? 0,
                    TotalSuccesses = health?.TotalSuccesses ?? 0,
                    TotalFailures = health?.TotalFailures ?? 0,
                    LastSuccessTime = health?.LastSuccessTime,
                    LastFailureTime = health?.LastFailureTime,
                    LastException = health?.LastException
                };
            }).ToList()
        };

        return await Task.FromResult(Ok(response));
    }
}
