using Microsoft.AspNetCore.Http;
using NzbWebDAV.Config;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.Api.SabControllers.GetHistory;

public class GetHistoryRequest
{
    public int Start { get; init; } = 0;
    public int Limit { get; init; } = int.MaxValue;
    public string? Category { get; init; }
    public CancellationToken CancellationToken { get; set; }


    public GetHistoryRequest(HttpContext context, ConfigManager configManager)
    {
        var startParam = context.GetQueryParam("start");
        var limitParam = context.GetQueryParam("limit");
        var pageSizeParam = context.GetQueryParam("pageSize");
        Category = context.GetQueryParam("category");
        CancellationToken = context.RequestAborted;

        if (startParam is not null)
        {
            var isValidStartParam = int.TryParse(startParam, out int start);
            if (!isValidStartParam) throw new BadHttpRequestException("Invalid start parameter");
            Start = start;
        }

        // The official Sabnzbd api uses the `limit` param to specify the number of history items
        // that should be returned in the response. However, radarr/sonarr set this param to 60 items
        // which causes problems:
        //   * https://github.com/nzbdav-dev/nzbdav/issues/48
        //   * https://github.com/Sonarr/Sonarr/issues/5452
        //
        // Because of this, NzbDAV added a setting to ignore the `limit` value specified by the Arrs.
        // When this setting is enabled, we always return all history items.
        if (limitParam is not null && !configManager.IsIgnoreSabHistoryLimitEnabled())
        {
            var isValidLimit = int.TryParse(limitParam, out var limit);
            if (!isValidLimit) throw new BadHttpRequestException("Invalid limit parameter");
            Limit = limit;
        }

        // Even though we may want to ignore the `limit` param from the Arrs, NzbDAV frontend
        // still needs a way to limit the pageSize for pagination. The `pageSize` param is used
        // for this, which takes precedence over the `limit` param. This param is not official to
        // the Sabnzbd api, and is intended to be used only by the NzbDAV frontend.
        if (pageSizeParam is not null)
        {
            var isValidPageSize = int.TryParse(pageSizeParam, out var pageSize);
            if (!isValidPageSize) throw new BadHttpRequestException("Invalid pageSize parameter");
            Limit = pageSize;
        }
    }
}