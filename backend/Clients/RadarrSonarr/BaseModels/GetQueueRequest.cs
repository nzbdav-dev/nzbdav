namespace NzbWebDAV.Clients.RadarrSonarr.BaseModels;

// ReSharper disable IdentifierTypo
public class GetQueueRequest
{
    private int Page { get; init; }
    private int PageSize { get; init; }

    public Dictionary<string, string> GetQueryParams()
    {
        return new Dictionary<string, string>()
        {
            { "page", Page.ToString() },
            { "pageSize", PageSize.ToString() },
            { "protocol", "usenet" },
        };
    }
}