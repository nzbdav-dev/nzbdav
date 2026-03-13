using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace NzbWebDAV.Middlewares;

/// <summary>
/// Middleware that patches WebDAV PROPFIND responses for broader client compatibility.
///
/// Fixes:
/// 1. Converts absolute href URLs to relative paths. NWebDav generates absolute URLs
///    (e.g., http://host:port/path) but some WebDAV clients (notably macOS mount_webdav)
///    expect relative paths in href elements.
/// 2. Strips Set-Cookie headers from WebDAV responses. Session cookies can confuse
///    stateless WebDAV clients that don't implement cookie management.
/// 3. Removes the 404 propstat for unsupported creationdate property. macOS webdavfs_agent
///    requests creationdate and may fail if the response contains a 404 propstat for it.
/// </summary>
public partial class WebDavCompatibilityMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> WebDavMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "OPTIONS", "PROPFIND", "PROPPATCH", "MKCOL", "COPY", "MOVE",
        "LOCK", "UNLOCK", "DELETE", "PUT", "GET", "HEAD"
    };

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var isWebDav = WebDavMethods.Contains(method);

        if (!isWebDav)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // For non-PROPFIND WebDAV methods, just strip cookies and pass through
        if (!method.Equals("PROPFIND", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.Remove("Set-Cookie");
                return Task.CompletedTask;
            });
            await next(context).ConfigureAwait(false);
            return;
        }

        // For PROPFIND: capture response body to fix XML content
        var originalBody = context.Response.Body;
        using var memoryStream = new MemoryStream();
        context.Response.Body = memoryStream;

        await next(context).ConfigureAwait(false);

        // Read the response
        memoryStream.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(memoryStream).ReadToEndAsync().ConfigureAwait(false);

        // Fix 1: absolute hrefs → relative paths
        responseBody = AbsoluteHrefPattern().Replace(responseBody, "<D:href>/$1</D:href>");

        // Fix 2: Strip Set-Cookie headers
        context.Response.Headers.Remove("Set-Cookie");

        // Fix 3: Remove 404 propstat blocks for unsupported properties (e.g., creationdate)
        responseBody = NotFoundPropstatPattern().Replace(responseBody, "");

        // Fix 4: Replace DateTime.MinValue dates (year 0001) with a valid epoch date.
        // Some DavItem instances return DateTime.MinValue for CreatedAt/ModifiedAt,
        // producing "Mon, 01 Jan 0001 00:00:00 GMT" which is invalid per RFC 7231
        // and breaks macOS webdavfs_agent.
        responseBody = DateTimeMinValuePattern().Replace(responseBody, "Thu, 01 Jan 1970 00:00:00 GMT");

        // Write the modified response with correct Content-Length
        var modifiedBytes = Encoding.UTF8.GetBytes(responseBody);
        context.Response.ContentLength = modifiedBytes.Length;
        context.Response.Body = originalBody;
        await context.Response.Body.WriteAsync(modifiedBytes).ConfigureAwait(false);
    }

    [GeneratedRegex(@"<D:href>https?://[^/]+/(.*?)</D:href>")]
    private static partial Regex AbsoluteHrefPattern();

    [GeneratedRegex(@"<D:propstat><D:prop>.*?</D:prop><D:status>HTTP/1\.1 404 Not Found</D:status>.*?</D:propstat>")]
    private static partial Regex NotFoundPropstatPattern();

    [GeneratedRegex(@"\w{3}, 01 Jan 0001 00:00:00 GMT")]
    private static partial Regex DateTimeMinValuePattern();
}
