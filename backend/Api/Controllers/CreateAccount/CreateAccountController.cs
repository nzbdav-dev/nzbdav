using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Api.Controllers.CreateAccount;

[ApiController]
[Route("api/create-account")]
public class CreateAccountController(DavDatabaseClient dbClient) : BaseApiController
{
    private async Task<CreateAccountResponse> CreateAccount(CreateAccountRequest request)
    {
        var randomSalt = Guid.NewGuid().ToString("N");
        var account = new Account()
        {
            Type = request.Type,
            Username = request.Username,
            RandomSalt = randomSalt,
            PasswordHash = PasswordUtil.Hash(request.Password, randomSalt),
        };
        dbClient.Ctx.Accounts.Add(account);
        await dbClient.Ctx.SaveChangesAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        return new CreateAccountResponse() { Status = true };
    }

    protected override async Task<IActionResult> HandleRequest()
    {
        var request = new CreateAccountRequest(HttpContext);
        var response = await CreateAccount(request).ConfigureAwait(false);
        return Ok(response);
    }
}