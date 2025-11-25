using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Api.Controllers.IsOnboarding;

[ApiController]
[Route("api/is-onboarding")]
public class IsOnboardingController(DavDatabaseClient dbClient) : BaseApiController
{
    private async Task<IsOnboardingResponse> IsOnboarding()
    {
        var account = await dbClient.Ctx.Accounts
            .Where(a => a.Type == Account.AccountType.Admin)
            .FirstOrDefaultAsync().ConfigureAwait(false);
        return new IsOnboardingResponse() { IsOnboarding = account == null };
    }

    protected override async Task<IActionResult> HandleRequest()
    {
        var response = await IsOnboarding().ConfigureAwait(false);
        return Ok(response);
    }
}