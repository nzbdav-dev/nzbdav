using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using NzbWebDAV.Config;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Api.Controllers.GetWebdavItem;

public class GetWebdavItemRequest
{
    public string Item { get; init; }
    public long? RangeStart { get; init; }
    public long? RangeEnd { get; init; }

    public GetWebdavItemRequest(HttpContext context)
    {
        // normalize path
        var path = context.Request.Path.Value;
        if (path.StartsWith("/")) path = path[1..];
        if (path.StartsWith("view")) path = path[4..];
        if (path.StartsWith("/")) path = path[1..];
        Item = path;

        // authenticate the downloadKey
        var downloadKey = context.Request.Query["downloadKey"];
        var configManager = (ConfigManager)context.Items["configManager"]!;
        if (!VerifyDownloadKey(downloadKey, Item, configManager))
            throw new UnauthorizedAccessException("Invalid download key");

        // parse range header
        var rangeHeader = context.Request.Headers["Range"].FirstOrDefault() ?? "";
        if (!rangeHeader.StartsWith("bytes=")) return;
        var parts = rangeHeader[6..].Split("-", StringSplitOptions.RemoveEmptyEntries);
        RangeStart = long.Parse(parts[0]);
        if (parts.Length > 1) RangeEnd = long.Parse(parts[1]);
    }

    private static bool VerifyDownloadKey(string? downloadKey, string path, ConfigManager configManager)
    {
        if (path.StartsWith(".ids"))
        {
            // strm streams link items by id and use a different download key
            var strmKey = configManager.GetStrmKey();
            var expectedDownloadKey = GenerateDownloadKey(strmKey, path);
            if (downloadKey == expectedDownloadKey)
                return true;
        }

        var apiKey = EnvironmentUtil.GetVariable("FRONTEND_BACKEND_API_KEY");
        return downloadKey == GenerateDownloadKey(apiKey, path);
    }

    public static string GenerateDownloadKey(string apiKey, string path)
    {
        var input = $"{path}_{apiKey}";
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(inputBytes);
        var hash = Convert.ToHexStringLower(hashBytes);
        return hash;
    }
}