using Microsoft.AspNetCore.Http;
using NzbWebDAV.Api.SabControllers.AddFile;
using NzbWebDAV.Config;
using NzbWebDAV.Extensions;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Api.SabControllers.AddUrl;

public class AddUrlRequest() : AddFileRequest
{
    private static readonly HttpClient HttpClient = GetHttpClient();

    private const int MaxAutomaticRedirections = 10;
    private const string UserAgentHeader =
        "Mozilla/5.0 (Windows NT 11.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/134.0.6998.166 Safari/537.36";

    public static async Task<AddUrlRequest> New(HttpContext context, ConfigManager configManager)
    {
        var nzbUrl = context.GetQueryParam("name");
        var nzbName = context.GetQueryParam("nzbname");
        var nzbFile = await GetNzbFile(nzbUrl, nzbName).ConfigureAwait(false);
        return new AddUrlRequest()
        {
            FileName = nzbFile.FileName,
            MimeType = nzbFile.ContentType,
            NzbFileContents = nzbFile.FileContents,
            Category = context.GetQueryParam("cat") ?? configManager.GetManualUploadCategory(),
            Priority = MapPriorityOption(context.GetQueryParam("priority")),
            PostProcessing = MapPostProcessingOption(context.GetQueryParam("pp")),
            CancellationToken = context.RequestAborted
        };
    }

    private static async Task<NzbFileResponse> GetNzbFile(string? url, string? nzbName)
    {
        try
        {
            // validate url
            if (string.IsNullOrWhiteSpace(url))
                throw new Exception($"The url is invalid.");

            // fetch url
            var response = await GetAsync(url).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Received status code {response.StatusCode}.");

            // read the content type
            var contentType = response.Content.Headers.ContentType?.MediaType;

            // determine the filename
            var fileName = AddNzbExtension(nzbName);
            if (fileName == null)
            {
                var contentDisposition = response.Content.Headers.ContentDisposition;
                fileName = contentDisposition?.FileName?.Trim('"');
                if (string.IsNullOrEmpty(fileName))
                    throw new Exception("Filename could not be determined from Content-Disposition header.");
            }

            // read the file contents
            var fileContents = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(fileContents))
                throw new Exception("NZB file contents are empty.");

            // return response
            return new NzbFileResponse
            {
                FileName = fileName,
                ContentType = contentType,
                FileContents = fileContents
            };
        }
        catch (Exception ex)
        {
            throw new BadHttpRequestException($"Failed to fetch nzb-file url `{url}`: {ex.Message}");
        }
    }

    private static string? AddNzbExtension(string? nzbName)
    {
        return nzbName == null ? null
            : nzbName.ToLower().EndsWith("nzb") ? nzbName
            : $"{nzbName}.nzb";
    }

    private static async Task<HttpResponseMessage> GetAsync(string url)
    {
        var response = await HttpClient.GetAsync(url);
        var remainingRedirects = MaxAutomaticRedirections;
        while
        (
            (int)response.StatusCode is >= 300 and < 400
            && remainingRedirects > 0
            && response.Headers.Location is not null
            && EnvironmentUtil.IsVariableTrue("ALLOW_HTTPS_TO_HTTP_REDIRECTS")
        )
        {
            var redirect = response.Headers.Location;
            var redirectUri = redirect.IsAbsoluteUri ? redirect : new Uri(new Uri(url), redirect);
            response = await HttpClient.GetAsync(redirectUri);
            remainingRedirects--;
        }

        return response;
    }

    private static HttpClient GetHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = MaxAutomaticRedirections,
        };
        var httpClient = new HttpClient(handler);
        httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgentHeader);
        return httpClient;
    }

    private class NzbFileResponse
    {
        public required string FileName { get; init; }
        public required string? ContentType { get; init; }
        public required string FileContents { get; init; }
    }
}