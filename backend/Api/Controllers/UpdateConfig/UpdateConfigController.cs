using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Api.Controllers.UpdateConfig;

[ApiController]
[Route("api/update-config")]
public class UpdateConfigController(DavDatabaseClient dbClient, ConfigManager configManager) : BaseApiController
{
    private async Task<UpdateConfigResponse> UpdateConfig(UpdateConfigRequest request)
    {
        // 1. Retrieve all ConfigItems from the database that match the ConfigNames in the request
        var configNames = request.ConfigItems.Select(x => x.ConfigName).ToHashSet();
        var existingItems = await dbClient.Ctx.ConfigItems
            .Where(c => configNames.Contains(c.ConfigName))
            .ToListAsync(HttpContext.RequestAborted).ConfigureAwait(false);

        // 2. Split the items into those that need to be updated and those that need to be inserted
        var existingItemsDict = existingItems.ToDictionary(i => i.ConfigName);
        var itemsToUpdate = new List<ConfigItem>();
        var itemsToInsert = new List<ConfigItem>();
        foreach (var item in request.ConfigItems)
        {
            if (existingItemsDict.TryGetValue(item.ConfigName, out ConfigItem? existingItem))
            {
                existingItem.ConfigValue = item.ConfigValue;
                itemsToUpdate.Add(existingItem);
            }
            else
            {
                itemsToInsert.Add(item);
            }
        }

        // 3. Perform bulk insert and bulk update
        dbClient.Ctx.ConfigItems.AddRange(itemsToInsert);
        dbClient.Ctx.ConfigItems.UpdateRange(itemsToUpdate);

        // 4. Save changes in one call
        await dbClient.Ctx.SaveChangesAsync(HttpContext.RequestAborted).ConfigureAwait(false);

        // 5. Update the ConfigManager
        configManager.UpdateValues(request.ConfigItems);

        // return
        return new UpdateConfigResponse { Status = true };
    }

    protected override async Task<IActionResult> HandleRequest()
    {
        var request = new UpdateConfigRequest(HttpContext);
        var response = await UpdateConfig(request).ConfigureAwait(false);
        return Ok(response);
    }
}