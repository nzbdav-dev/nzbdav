using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Api.SabControllers.AddFile;
using NzbWebDAV.Api.SabControllers.AddUrl;
using NzbWebDAV.Api.SabControllers.GetConfig;
using NzbWebDAV.Api.SabControllers.GetFullStatus;
using NzbWebDAV.Api.SabControllers.GetHistory;
using NzbWebDAV.Api.SabControllers.GetQueue;
using NzbWebDAV.Api.SabControllers.GetVersion;
using NzbWebDAV.Api.SabControllers.RemoveFromHistory;
using NzbWebDAV.Api.SabControllers.RemoveFromQueue;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Extensions;
using NzbWebDAV.Queue;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Api.SabControllers;

[ApiController]
[Route("api")]
public class SabApiController(
    DavDatabaseClient dbClient,
    ConfigManager configManager,
    QueueManager queueManager,
    WebsocketManager websocketManager
) : ControllerBase
{
    [HttpGet]
    [HttpPost]
    public async Task<IActionResult> HandleApiRequests()
    {
        try
        {
            var controller = GetController();
            return await controller.HandleRequest();
        }
        catch (BadHttpRequestException e)
        {
            return BadRequest(new SabBaseResponse()
            {
                Status = false,
                Error = e.Message
            });
        }
        catch (UnauthorizedAccessException e)
        {
            return Unauthorized(new SabBaseResponse()
            {
                Status = false,
                Error = e.Message
            });
        }
        catch (Exception e)
        {
            return StatusCode(500, new SabBaseResponse()
            {
                Status = false,
                Error = e.Message
            });
        }
    }

    public BaseController GetController()
    {
        switch (HttpContext.GetQueryParam("mode"))
        {
            case "version":
                return new GetVersionController(
                    HttpContext, configManager);
            case "get_config":
                return new GetConfigController(
                    HttpContext, configManager);
            case "fullstatus":
                return new GetFullStatusController(
                    HttpContext, configManager);
            case "addfile":
                return new AddFileController(
                    HttpContext, dbClient, queueManager, configManager, websocketManager);
            case "addurl":
                return new AddUrlController(
                    HttpContext, dbClient, queueManager, configManager, websocketManager);

            case "queue" when HttpContext.GetQueryParam("name") == "delete":
                return new RemoveFromQueueController(
                    HttpContext, dbClient, queueManager, configManager, websocketManager);
            case "queue":
                return new GetQueueController(
                    HttpContext, dbClient, queueManager, configManager);

            case "history" when HttpContext.GetQueryParam("name") == "delete":
                return new RemoveFromHistoryController(
                    HttpContext, dbClient, configManager, websocketManager);
            case "history":
                return new GetHistoryController(
                    HttpContext, dbClient, configManager);

            default:
                throw new BadHttpRequestException("Invalid mode");
        }
    }

    public abstract class BaseController(HttpContext httpContext, ConfigManager configManager) : ControllerBase
    {
        public Task<IActionResult> HandleRequest()
        {
            if (RequiresAuthentication)
            {
                var apiKey = httpContext.GetRequestApiKey();
                var isValidKey = apiKey?.IsAny(
                    configManager.GetApiKey(),
                    EnvironmentUtil.GetVariable("FRONTEND_BACKEND_API_KEY")
                );
                if (!isValidKey.HasValue)
                    throw new UnauthorizedAccessException("API Key Required");
                if (!isValidKey.Value)
                    throw new UnauthorizedAccessException("API Key Incorrect");
            }

            return Handle();
        }

        protected virtual bool RequiresAuthentication => true;
        protected abstract Task<IActionResult> Handle();
    }
}