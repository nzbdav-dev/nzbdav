using Microsoft.AspNetCore.Http;

namespace NzbWebDAV.Extensions;

public static class HttpContextExtensions
{
    public static string? GetQueryParam(this HttpContext httpContext, string name)
    {
        return httpContext.Request.Query[name].FirstOrDefault();
    }

    public static IEnumerable<string> GetQueryParamValues(this HttpContext httpContext, string name)
    {
        return httpContext.Request.Query[name]
            .Where(x => x is not null)
            .Select(x => x!);
    }

    public static string? GetRequestApiKey(this HttpContext httpContext)
    {
        return httpContext.Request.Headers["x-api-key"].FirstOrDefault()
            ?? httpContext.GetQueryParam("apikey");
    }
}