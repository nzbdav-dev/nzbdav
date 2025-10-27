using Microsoft.AspNetCore.Http;
using NzbWebDAV.Api.SabControllers.AddFile;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.Api.SabControllers.AddUrl;

public class AddUrlRequest() : AddFileRequest
{
    private const string UserAgentHeader =
        "Mozilla/5.0 (Windows NT 11.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/134.0.6998.166 Safari/537.36";

    public static async Task<AddUrlRequest> New(HttpContext context)
    {
        var nzbUrl = context.GetQueryParam("name");
        var nzbFile = await GetNzbFile(nzbUrl);
        return new AddUrlRequest()
        {
            FileName = nzbFile.FileName,
            MimeType = nzbFile.ContentType,
            NzbFileContents = nzbFile.FileContents,
            Category = context.GetQueryParam("cat") ?? throw new BadHttpRequestException("Invalid cat param"),
            Priority = MapPriorityOption(context.GetQueryParam("priority")),
            PostProcessing = MapPostProcessingOption(context.GetQueryParam("pp")),
            CancellationToken = context.RequestAborted
        };
    }

    private static async Task<NzbFileResponse> GetNzbFile(string? url)
    {
        try
        {
            // validate url
            if (string.IsNullOrWhiteSpace(url))
                throw new Exception($"The url is invalid.");

            // fetch url
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgentHeader);
            var response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Received status code {response.StatusCode}.");

            // read the content type
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrEmpty(contentType))
                throw new Exception("Missing Content-Type header.");

            // read the filename
            var contentDisposition = response.Content.Headers.ContentDisposition;
            var fileName = contentDisposition?.FileName?.Trim('"');
            if (string.IsNullOrEmpty(fileName))
                throw new Exception("Filename could not be determined from Content-Disposition header.");

            // read the file contents
            var fileContents = await response.Content.ReadAsStringAsync();
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

    private class NzbFileResponse
    {
        public required string FileName { get; init; }
        public required string ContentType { get; init; }
        public required string FileContents { get; init; }
    }
}