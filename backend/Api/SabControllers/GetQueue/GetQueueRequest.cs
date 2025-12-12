using Microsoft.AspNetCore.Http;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.Api.SabControllers.GetQueue;

public class GetQueueRequest
{
    public int Start { get; init; } = 0;
    public int Limit { get; init; } = int.MaxValue;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = int.MaxValue;
    public string? Category { get; init; }
    public CancellationToken CancellationToken { get; init; }


    public GetQueueRequest(HttpContext context)
    {
        var startParam = context.GetQueryParam("start");
        var limitParam = context.GetQueryParam("limit");
        var pageParam = context.GetQueryParam("page");
        var pageSizeParam = context.GetQueryParam("pageSize");
        Category = context.GetQueryParam("category");
        CancellationToken = context.RequestAborted;

        if (startParam is not null)
        {
            var isValidStartParam = int.TryParse(startParam, out int start);
            if (!isValidStartParam) throw new BadHttpRequestException("Invalid start parameter");
            Start = start;
        }

        if (limitParam is not null)
        {
            var isValidLimit = int.TryParse(limitParam, out int limit);
            if (!isValidLimit) throw new BadHttpRequestException("Invalid limit parameter");
            Limit = limit;
        }

        if (pageParam is not null)
        {
            var isValidPage = int.TryParse(pageParam, out int page);
            if (!isValidPage || page < 1) throw new BadHttpRequestException("Invalid page parameter");
            Page = page;
        }

        if (pageSizeParam is not null)
        {
            var isValidPageSize = int.TryParse(pageSizeParam, out int pageSize);
            if (!isValidPageSize || pageSize < 1) throw new BadHttpRequestException("Invalid pageSize parameter");
            PageSize = pageSize;
        }

        // If page/pageSize are provided, override start/limit accordingly
        if (pageParam is not null || pageSizeParam is not null)
        {
            var effectivePageSize = PageSize == int.MaxValue ? Limit : PageSize;
            var effectivePage = Page;
            Start = (effectivePage - 1) * effectivePageSize;
            Limit = effectivePageSize;
        }
    }
}
