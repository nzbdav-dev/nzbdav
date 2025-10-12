using Microsoft.AspNetCore.Http;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.Api.Controllers.GetHealthCheckQueue;

public class GetHealthCheckQueueRequest
{
    public int PageSize { get; init; } = 20;
    public CancellationToken CancellationToken { get; init; }

    public GetHealthCheckQueueRequest(HttpContext context)
    {
        var pageSizeParam = context.GetQueryParam("pageSize");
        CancellationToken = context.RequestAborted;

        if (pageSizeParam is not null)
        {
            var isValidStartParam = int.TryParse(pageSizeParam, out int pageSize);
            if (!isValidStartParam) throw new BadHttpRequestException("Invalid pageSize parameter");
            PageSize = pageSize;
        }
    }
}