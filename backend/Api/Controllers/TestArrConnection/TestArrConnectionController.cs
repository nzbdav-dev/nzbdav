using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Api.Controllers;
using NzbWebDAV.Clients;

namespace NzbWebDAV.Api.Controllers.TestArrConnection;

[ApiController]
[Route("api/test-arr-connection")]
public class TestArrConnectionController(ArrManager arrManager) : BaseApiController
{
    private async Task<TestArrConnectionResponse> TestConnections()
    {
        var success = await arrManager.TestAllConnectionsAsync();
        var instances = arrManager.GetConfiguredInstances();

        return new TestArrConnectionResponse
        {
            Success = success,
            ConfiguredInstances = instances,
            Message = success
                ? $"Successfully connected to all {instances.Count} configured instances"
                : "Failed to connect to one or more instances"
        };
    }

    private async Task<TestArrConnectionResponse> TestSingleConnection(ArrAppType appType, string url, string apiKey, string name)
    {
        try
        {
            // Create a temporary client to test the connection
            if (appType == ArrAppType.Radarr)
            {
                using var client = new RadarrClient(url, apiKey, name);
                var success = await client.TestConnectionAsync();

                return new TestArrConnectionResponse
                {
                    Success = success,
                    Message = success
                        ? "Connected successfully to Radarr"
                        : "Failed to connect to Radarr - check URL and API key"
                };
            }
            else if (appType == ArrAppType.Sonarr)
            {
                using var client = new SonarrClient(url, apiKey, name);
                var success = await client.TestConnectionAsync();

                return new TestArrConnectionResponse
                {
                    Success = success,
                    Message = success
                        ? "Connected successfully to Sonarr"
                        : "Failed to connect to Sonarr - check URL and API key"
                };
            }
            else
            {
                return new TestArrConnectionResponse
                {
                    Success = false,
                    Message = $"Unsupported app type: {appType}"
                };
            }
        }
        catch (Exception ex)
        {
            return new TestArrConnectionResponse
            {
                Success = false,
                Message = $"Connection test failed: {ex.Message}"
            };
        }
    }

    protected override async Task<IActionResult> HandleRequest()
    {
        // Check if this is a single connection test (POST with body) or all connections test (GET)
        if (Request.Method == "POST")
        {
            try
            {
                using var reader = new StreamReader(Request.Body);
                var json = await reader.ReadToEndAsync();
                var testRequest = System.Text.Json.JsonSerializer.Deserialize<TestSingleConnectionRequest>(json, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) }
                });

                if (testRequest == null || string.IsNullOrEmpty(testRequest.Url) || string.IsNullOrEmpty(testRequest.ApiKey))
                {
                    return BadRequest(new TestArrConnectionResponse
                    {
                        Success = false,
                        Message = "Url and ApiKey are required"
                    });
                }

                var response = await TestSingleConnection(testRequest.AppType, testRequest.Url, testRequest.ApiKey, testRequest.Name ?? "Test");
                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new TestArrConnectionResponse
                {
                    Success = false,
                    Message = $"Invalid request: {ex.Message}"
                });
            }
        }
        else
        {
            // Default behavior: test all configured connections
            var response = await TestConnections();
            return Ok(response);
        }
    }
}
