using Microsoft.AspNetCore.Http;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.Api.Controllers.GetHealthCheckHistory;

public class GetHealthCheckHistoryRequest
{
    public int PageSize { get; init; } = 20;
    public int Page { get; init; } = 1;
    public int? RepairStatus { get; init; }
    public CancellationToken CancellationToken { get; init; }

    public GetHealthCheckHistoryRequest(HttpContext context)
    {
        var pageSizeParam = context.GetQueryParam("pageSize");
        var pageParam = context.GetQueryParam("page");
        var repairStatusParam = context.GetQueryParam("repairStatus");
        CancellationToken = context.RequestAborted;

        if (pageSizeParam is not null)
        {
            var isValidStartParam = int.TryParse(pageSizeParam, out int pageSize);
            if (!isValidStartParam) throw new BadHttpRequestException("Invalid pageSize parameter");
            PageSize = pageSize;
        }

        if (pageParam is not null)
        {
            var isValidPageParam = int.TryParse(pageParam, out int page);
            if (!isValidPageParam || page <= 0) throw new BadHttpRequestException("Invalid page parameter");
            Page = page;
        }

        if (repairStatusParam is not null)
        {
            var isValidRepairStatus = int.TryParse(repairStatusParam, out int repairStatus);
            if (!isValidRepairStatus) throw new BadHttpRequestException("Invalid repairStatus parameter");
            RepairStatus = repairStatus;
        }
    }
}
