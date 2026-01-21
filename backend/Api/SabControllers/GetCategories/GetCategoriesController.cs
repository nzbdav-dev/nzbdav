using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Api.SabControllers.GetCategories;

public class GetCategoriesController(
    HttpContext httpContext,
    ConfigManager configManager
) : SabApiController.BaseController(httpContext, configManager)
{
    protected override async Task<IActionResult> Handle()
    {
        var categories = configManager.GetApiCategories().Split(',')
            .Prepend(configManager.GetManualUploadCategory())
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        var response = new { categories };
        return Ok(response);
    }
}