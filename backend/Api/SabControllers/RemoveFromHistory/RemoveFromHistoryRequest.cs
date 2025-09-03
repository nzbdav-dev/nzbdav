using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using NzbWebDAV.Extensions;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Api.SabControllers.RemoveFromHistory;

public class RemoveFromHistoryRequest
{
    public List<Guid> NzoIds { get; private init; } = [];
    public bool DeleteCompletedFiles { get; private init; }
    public CancellationToken CancellationToken { get; private init; }

    public static async Task<RemoveFromHistoryRequest> New(HttpContext httpContext)
    {
        // Note: The official SABnzbd api has a query param named `del_files`
        // which only applies to Failed jobs and does not apply to Completed jobs.
        // However, Failed jobs in nzbdav never add anything to the webdav anyway,
        // so there is never anything to delete for Failed jobs. For this reason,
        // the `del_files` query param from SABnzbd is not needed here at all.
        //
        // Instead, a non-standard `del_completed_files` query param is added.
        // It applies to Completed jobs and does not apply to Failed jobs. It is
        // only used by the nzbdav web-ui when manually clearing History items to
        // provide users the option to delete all related files.
        var cancellationToken = SigtermUtil.GetCancellationToken();
        return new RemoveFromHistoryRequest()
        {
            NzoIds = NzoIdsFromQueryParam(httpContext)
                .Concat(await NzoIdsFromRequestBody(httpContext, cancellationToken))
                .ToList(),
            DeleteCompletedFiles = httpContext.GetQueryParam("del_completed_files") == "1",
            CancellationToken = cancellationToken
        };
    }

    private static IEnumerable<Guid> NzoIdsFromQueryParam(HttpContext httpContext)
    {
        return httpContext.GetQueryParamValues("value").Select(Guid.Parse);
    }

    private static async Task<List<Guid>> NzoIdsFromRequestBody(HttpContext httpContext, CancellationToken ct)
    {
        try
        {
            await using var stream = httpContext.Request.Body;
            var deserialized = await JsonSerializer.DeserializeAsync<RequestBody>(stream, cancellationToken: ct);
            return deserialized?.NzoIds ?? [];
        }
        catch
        {
            return [];
        }
    }

    private class RequestBody
    {
        [JsonPropertyName("nzo_ids")]
        public List<Guid> NzoIds { get; set; } = [];
    }
}