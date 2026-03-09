using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NzbWebDAV.Clients.Rclone.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Utils;
using Serilog;

namespace NzbWebDAV.Clients.Rclone;

/// <summary>
/// Client for interacting with rclone's remote control (RC) API.
/// See https://rclone.org/rc/ for API documentation.
/// </summary>
public class RcloneClient
{
    private static readonly HttpClient HttpClient = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string? Host { get; } = EnvironmentUtil.GetEnvironmentVariable("RCLONE_HOST")?.TrimEnd('/');
    private string? User { get; } = EnvironmentUtil.GetEnvironmentVariable("RCLONE_USER");
    private string? Pass { get; } = EnvironmentUtil.GetEnvironmentVariable("RCLONE_PASS");

    /// <summary>
    /// Refresh the VFS directory cache for multiple paths in a single request.
    /// </summary>
    /// <param name="paths">The paths to refresh</param>
    /// <param name="recursive">Whether to refresh recursively</param>
    /// <param name="fs">Optional VFS name if multiple VFS instances exist</param>
    public async Task<RcloneResponse> RefreshVfsPaths(IEnumerable<string> paths, bool recursive = false,
        string? fs = null)
    {
        var pathList = paths.ToList();
        if (pathList.Count == 0)
            return new RcloneResponse { Success = true };

        var request = new Dictionary<string, object?>();

        // Add paths using numbered keys: dir, dir2, dir3, etc.
        for (int i = 0; i < pathList.Count; i++)
        {
            var key = i == 0 ? "dir" : $"dir{i + 1}";
            request[key] = pathList[i];
        }

        if (recursive)
            request["recursive"] = true;

        if (fs != null)
            request["fs"] = fs;

        Log.Information("Rclone vfs/refresh: {0}", paths.ToIndentedJson());
        return await Post<RcloneResponse>("vfs/refresh", request);
    }

    /// <summary>
    /// Forget (clear) VFS directory cache entries for multiple paths in a single request.
    /// </summary>
    /// <param name="paths">The paths to forget</param>
    /// <param name="fs">Optional VFS name if multiple VFS instances exist</param>
    public async Task<VfsForgetResponse> ForgetVfsPaths(IEnumerable<string> paths, string? fs = null)
    {
        var pathList = paths.ToList();
        if (pathList.Count == 0)
            return new VfsForgetResponse { Success = true, Forgotten = new List<string>() };

        var request = new Dictionary<string, object?>();

        // Add paths using numbered keys: dir, dir2, dir3, etc.
        for (int i = 0; i < pathList.Count; i++)
        {
            var key = i == 0 ? "dir" : $"dir{i + 1}";
            request[key] = pathList[i];
        }

        if (fs != null)
            request["fs"] = fs;

        Log.Information("Rclone vfs/forget: {0}", paths.ToIndentedJson());
        return await Post<VfsForgetResponse>("vfs/forget", request);
    }

    /// <summary>
    /// Get VFS statistics including cache information.
    /// </summary>
    /// <param name="fs">Optional VFS name if multiple VFS instances exist</param>
    public async Task<VfsStatsResponse> GetVfsStats(string? fs = null)
    {
        var request = fs != null ? new { fs } : null;
        return await Post<VfsStatsResponse>("vfs/stats", request);
    }

    /// <summary>
    /// Get rclone version information.
    /// </summary>
    public async Task<CoreVersionResponse> GetVersion()
    {
        return await Post<CoreVersionResponse>("core/version", null);
    }

    /// <summary>
    /// Test connectivity - a no-operation call.
    /// </summary>
    public async Task<RcloneResponse> NoOp()
    {
        return await Post<RcloneResponse>("rc/noop", null);
    }

    /// <summary>
    /// Check if the rclone RC server is reachable and authenticated.
    /// </summary>
    public async Task<bool> IsAvailable()
    {
        try
        {
            await NoOp();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<T> Post<T>(string endpoint, object? body) where T : RcloneResponse, new()
    {
        var url = $"{Host}/{endpoint}";
        var request = new HttpRequestMessage(HttpMethod.Post, url);

        if (body != null)
        {
            var jsonBody = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }
        else
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        AddAuthHeader(request);

        try
        {
            using var response = await HttpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("Rclone RC request to {Endpoint} failed with status {StatusCode}: {Content}",
                    endpoint, response.StatusCode, content);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return new T { Success = false, Error = "Authentication failed" };
                }

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<RcloneErrorResponse>(content, JsonOptions);
                    return new T { Success = false, Error = errorResponse?.Error ?? $"HTTP {response.StatusCode}" };
                }
                catch
                {
                    return new T { Success = false, Error = $"HTTP {response.StatusCode}: {content}" };
                }
            }

            if (string.IsNullOrWhiteSpace(content) || content == "{}")
            {
                return new T { Success = true };
            }

            var result = JsonSerializer.Deserialize<T>(content, JsonOptions) ?? new T();
            result.Success = true;
            return result;
        }
        catch (HttpRequestException ex)
        {
            Log.Warning(ex, "Rclone RC request to {Endpoint} failed", endpoint);
            return new T { Success = false, Error = ex.Message };
        }
        catch (TaskCanceledException ex)
        {
            Log.Warning(ex, "Rclone RC request to {Endpoint} timed out", endpoint);
            return new T { Success = false, Error = "Request timed out" };
        }
    }

    private void AddAuthHeader(HttpRequestMessage request)
    {
        if (string.IsNullOrEmpty(User) && string.IsNullOrEmpty(Pass))
            return;

        var credentials = $"{User ?? ""}:{Pass ?? ""}";
        var encodedCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encodedCredentials);
    }
}