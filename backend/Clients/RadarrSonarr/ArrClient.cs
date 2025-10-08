using System.Net;
using System.Text.Json;
using NzbWebDAV.Clients.RadarrSonarr.BaseModels;
using NzbWebDAV.Config;

namespace NzbWebDAV.Clients.RadarrSonarr;

public abstract class ArrClient(string host, string apiKey)
{
    public string Host { get; } = host;
    private string ApiKey { get; } = apiKey;
    private const string BasePath = "/api/v3";

    public Task<ArrQueueStatus> GetQueueStatusAsync() =>
        Get<ArrQueueStatus>($"/queue/status");

    public Task<ArrQueue<ArrQueueRecord>> GetQueueAsync() =>
        Get<ArrQueue<ArrQueueRecord>>($"/queue?protocol=usenet");

    public Task<HttpStatusCode> DeleteQueueRecord(int id, DeleteQueueRecordRequest request) =>
        Delete($"/queue/{id}", request.GetQueryParams());

    public Task<HttpStatusCode> DeleteQueueRecord(int id, ArrConfig.QueueAction request) =>
        request is not ArrConfig.QueueAction.DoNothing
            ? Delete($"/queue/{id}", new DeleteQueueRecordRequest(request).GetQueryParams())
            : Task.FromResult(HttpStatusCode.OK);

    protected async Task<T> Get<T>(string path)
    {
        using var httpClient = GetHttpClient();
        await using var response = await httpClient.GetStreamAsync($"{Host}{BasePath}{path}");
        return await JsonSerializer.DeserializeAsync<T>(response) ?? throw new NullReferenceException();
    }

    private async Task<HttpStatusCode> Delete(string path, Dictionary<string, string>? queryParams = null)
    {
        using var httpClient = GetHttpClient();
        using var response = await httpClient.DeleteAsync(GetRequestUri(path, queryParams));
        return response.StatusCode;
    }

    private string GetRequestUri(string path, Dictionary<string, string>? queryParams = null)
    {
        queryParams ??= new Dictionary<string, string>();
        var resource = $"{Host}{BasePath}{path}";
        var query = queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}");
        var queryString = string.Join("&", query);
        if (queryString.Length > 0) resource = $"{resource}?{queryString}";
        return resource;
    }

    private HttpClient GetHttpClient()
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return httpClient;
    }
}