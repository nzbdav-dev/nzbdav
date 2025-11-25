using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NWebDav.Server.Stores;
using NzbWebDAV.Config;
using NzbWebDAV.WebDav;
using NzbWebDAV.WebDav.Base;

namespace NzbWebDAV.Api.Controllers.ListWebdavDirectory;

[ApiController]
[Route("api/list-webdav-directory")]
public class ListWebdavDirectoryController(DatabaseStore store, ConfigManager configManager) : BaseApiController
{
    private async Task<ListWebdavDirectoryResponse> ListWebdavDirectory(ListWebdavDirectoryRequest request)
    {
        var item = await store.GetItemAsync(request.Directory, HttpContext.RequestAborted).ConfigureAwait(false);
        if (item is null) throw new BadHttpRequestException("The directory does not exist.");
        if (item is not IStoreCollection dir) throw new BadHttpRequestException("The directory does not exist.");
        var children = new List<ListWebdavDirectoryResponse.DirectoryItem>();
        var showHiddenWebdavFiles = configManager.ShowHiddenWebdavFiles();
        await foreach (var child in dir.GetItemsAsync(HttpContext.RequestAborted))
        {
            if (!showHiddenWebdavFiles && child.Name.StartsWith('.'))
                continue;

            children.Add(new ListWebdavDirectoryResponse.DirectoryItem()
            {
                Name = child.Name,
                IsDirectory = (child is IStoreCollection),
                Size = (child is BaseStoreItem bsi ? bsi.FileSize : null)
            });
        }

        return new ListWebdavDirectoryResponse() { Items = children };
    }

    protected override async Task<IActionResult> HandleRequest()
    {
        var request = new ListWebdavDirectoryRequest(HttpContext);
        var response = await ListWebdavDirectory(request).ConfigureAwait(false);
        return Ok(response);
    }
}