using System.Net;
using Microsoft.AspNetCore.Http;
using NzbWebDAV.Api.SabControllers.AddFile;
using NzbWebDAV.Extensions;

namespace NzbWebDAV.Api.SabControllers.AddUrl;

public class AddUrlRequest() : AddFileRequest
{
    private const string UserAgentHeader =
        "Mozilla/5.0 (Windows NT 11.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/134.0.6998.166 Safari/537.36";

    // Reusable HttpClient to prevent socket exhaustion
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", UserAgentHeader } }
    };

    public static async Task<AddUrlRequest> New(HttpContext context)
    {
        var nzbUrl = context.GetQueryParam("name");
        var nzbName = context.GetQueryParam("nzbname");
        var nzbFile = await GetNzbFile(nzbUrl, nzbName);
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

    private static async Task<NzbFileResponse> GetNzbFile(string? url, string? nzbName)
    {
        try
        {
            // validate url
            if (string.IsNullOrWhiteSpace(url))
                throw new Exception($"The url is invalid.");

            // Validate URL to prevent SSRF attacks
            if (!IsUrlSafe(url))
                throw new Exception("URL is not allowed. Cannot access internal/private resources.");

            // fetch url using reusable HttpClient
            var response = await HttpClient.GetAsync(url);
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

    /// <summary>
    /// Validates URL to prevent SSRF attacks by blocking access to private IP ranges and localhost.
    /// </summary>
    private static bool IsUrlSafe(string url)
    {
        try
        {
            // Parse and validate the URI
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            // Only allow http and https schemes
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return false;

            // Get the host IP address
            var hostAddresses = Dns.GetHostAddresses(uri.Host);

            foreach (var ipAddress in hostAddresses)
            {
                // Block all private IP ranges
                var bytes = ipAddress.GetAddressBytes();

                if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    // IPv4 checks
                    // 127.0.0.0/8 - Loopback
                    if (bytes[0] == 127)
                        return false;

                    // 10.0.0.0/8 - Private network
                    if (bytes[0] == 10)
                        return false;

                    // 172.16.0.0/12 - Private network
                    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                        return false;

                    // 192.168.0.0/16 - Private network
                    if (bytes[0] == 192 && bytes[1] == 168)
                        return false;

                    // 169.254.0.0/16 - Link-local (AWS metadata service)
                    if (bytes[0] == 169 && bytes[1] == 254)
                        return false;

                    // 0.0.0.0/8 - Current network
                    if (bytes[0] == 0)
                        return false;
                }
                else if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    // IPv6 checks
                    // Block loopback (::1)
                    if (IPAddress.IsLoopback(ipAddress))
                        return false;

                    // Block link-local (fe80::/10)
                    if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
                        return false;

                    // Block unique local addresses (fc00::/7)
                    if ((bytes[0] & 0xfe) == 0xfc)
                        return false;
                }
            }

            return true;
        }
        catch
        {
            // If DNS resolution or any other error occurs, block the request
            return false;
        }
    }

    private static string? AddNzbExtension(string? nzbName)
    {
        return nzbName == null ? null
            : nzbName.ToLower().EndsWith("nzb") ? nzbName
            : $"{nzbName}.nzb";
    }

    private class NzbFileResponse
    {
        public required string FileName { get; init; }
        public required string? ContentType { get; init; }
        public required string FileContents { get; init; }
    }
}