using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Api.Controllers.Authenticate;

[ApiController]
[Route("api/authenticate")]
public class AuthenticateController(DavDatabaseClient dbClient) : BaseApiController
{
    private async Task<AuthenticateResponse> Authenticate(AuthenticateRequest request)
    {
        var account = await dbClient.Ctx.Accounts
            .Where(a => a.Type == request.Type && a.Username == request.Username)
            .FirstOrDefaultAsync().ConfigureAwait(false);

        return new AuthenticateResponse()
        {
            Authenticated = account != null
                && PasswordUtil.Verify(account.PasswordHash, request.Password, account.RandomSalt)
        };
    }

    protected override async Task<IActionResult> HandleRequest()
    {
        var request = new AuthenticateRequest(HttpContext);
        var response = await Authenticate(request).ConfigureAwait(false);
        return Ok(response);
    }
}