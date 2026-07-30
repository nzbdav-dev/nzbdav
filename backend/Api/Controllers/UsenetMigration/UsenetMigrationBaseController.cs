using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NzbWebDAV.Auth;
using NzbWebDAV.Config;
using NzbWebDAV.Extensions;
using NzbWebDAV.UsenetMigration;
using Serilog;

namespace NzbWebDAV.Api.Controllers.UsenetMigration;

/// <summary>
/// Shared auth + error envelope for the migration wizard's attribute-routed
/// controllers. The stock <see cref="BaseApiController"/> only maps GET+POST to a
/// single handler, but the wizard needs distinct GET/PUT/POST/DELETE verbs per
/// route, so these controllers declare explicit actions and wrap each body
/// in <see cref="GuardedAsync"/> to reproduce <see cref="BaseApiController"/>'s
/// API-key check and error shape.
/// </summary>
[ApiController]
public abstract class UsenetMigrationBaseController : ControllerBase
{
    protected async Task<IActionResult> GuardedAsync(Func<Task<IActionResult>> handler)
    {
        try
        {
            var configManager = HttpContext.RequestServices.GetRequiredService<ConfigManager>();
            ApiKeyValidator.Validate(HttpContext, configManager);
            var migrationStore = HttpContext.RequestServices.GetRequiredService<UsenetMigrationStore>();
            await migrationStore.EnsureDatabaseAsync(HttpContext.RequestAborted).ConfigureAwait(false);
            return await handler().ConfigureAwait(false);
        }
        catch (Exception e) when (e is BadHttpRequestException or ArgumentException)
        {
            return BadRequest(new BaseApiResponse { Status = false, Error = e.Message });
        }
        catch (UnauthorizedAccessException e)
        {
            return Unauthorized(new BaseApiResponse { Status = false, Error = e.Message });
        }
        catch (Exception e) when (e is not OperationCanceledException ||
                                  !HttpContext.RequestAborted.IsCancellationRequested)
        {
            if (e.TryGetKnownErrorMessage(out var reason))
            {
                Log.Warning("Usenet migration API request failed. Reason: {Reason}", reason);
                Log.Debug(e, "Usenet migration API known failure stack");
            }
            else
            {
                Log.Error(e, "Unhandled Usenet migration API failure");
            }
            return StatusCode(500, new BaseApiResponse { Status = false, Error = "An internal server error occurred." });
        }
    }
}
