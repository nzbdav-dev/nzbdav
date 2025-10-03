using System.Linq;
using NzbWebDAV.Config;
using Serilog;

namespace NzbWebDAV.Clients;

public enum ContentType
{
    Movie,
    TvShow
}

public class ArrManager : IDisposable
{
    private readonly ConfigManager _configManager;
    private readonly List<ArrClient> _radarrClients = new();
    private readonly List<ArrClient> _sonarrClients = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public ArrManager(ConfigManager configManager)
    {
        _configManager = configManager;

        InitializeClients();

        _configManager.OnConfigChanged += (sender, e) =>
        {
            if (e.ChangedConfig.Keys.Any(k => k.StartsWith("radarr.") || k.StartsWith("sonarr.")))
            {
                Log.Information("Arr config changed, refreshing clients");
                RefreshClients();
            }
        };
    }

    private void InitializeClients()
    {
        _radarrClients.AddRange(GetClients(ArrAppType.Radarr));
        _sonarrClients.AddRange(GetClients(ArrAppType.Sonarr));
    }

    public async Task<bool> DeleteFileFromArrAsync(string filePath, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var anySuccess = false;
            var contentType = DetectContentType(filePath);

            Log.Information("Detected content type '{ContentType}' for file: {FilePath}", contentType, filePath);

            var arrClients = contentType == ContentType.Movie ? _radarrClients : _sonarrClients;

            foreach (var arrClient in arrClients)
            {
                try
                {
                    var (success, itemIds) = await arrClient.DeleteFileAsync(filePath, ct);
                    if (success)
                    {
                        Log.Information("Successfully deleted file '{FilePath}' via {AppType} instance '{InstanceName}'",
                            filePath, arrClient.AppType, arrClient.InstanceName);
                        anySuccess = true;

                        foreach (var itemId in itemIds)
                        {
                            // Trigger search for replacement if we have a item ID
                            Log.Information("Triggering search for {AppType} item (ID: {ItemId}) in {AppType} instance '{InstanceName}'",
                                 itemId, arrClient.AppType, arrClient.InstanceName);
                            await arrClient.TriggerSearchByIdAsync(itemId, ct);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to delete file '{FilePath}' via {AppType} instance '{InstanceName}'",
                        filePath, arrClient.AppType, arrClient.InstanceName);
                }
            }

            if (!anySuccess)
            {
                Log.Warning("File '{FilePath}' was not found in any configured {ServiceTypes} instances",
                    filePath, GetServiceTypesString(contentType));
            }

            return anySuccess;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> MonitorFileInArrAsync(string filePath, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var anySuccess = false;
            var contentType = DetectContentType(filePath);

            Log.Information("Attempting to monitor content type '{ContentType}' for file: {FilePath}", contentType, filePath);

            var arrClients = contentType == ContentType.Movie ? _radarrClients : _sonarrClients;

            foreach (var arrClient in arrClients)
            {
                var success = await arrClient.MonitorFileAsync(filePath, ct);
                if (success) anySuccess = true;
            }

            if (!anySuccess)
            {
                Log.Information("File '{FilePath}' was not found for monitoring in any configured {ServiceTypes} instances",
                    filePath, GetServiceTypesString(contentType));
            }

            return anySuccess;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> UnmonitorFileInArrAsync(string filePath, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var anySuccess = false;
            var contentType = DetectContentType(filePath);

            Log.Information("Attempting to unmonitor content type '{ContentType}' for file: {FilePath}", contentType, filePath);

            var arrClients = contentType == ContentType.Movie ? _radarrClients : _sonarrClients;

            foreach (var arrClient in arrClients)
            {
                var success = await arrClient.UnmonitorFileAsync(filePath, ct);
                if (success) anySuccess = true;
            }

            if (!anySuccess)
            {
                Log.Information("File '{FilePath}' was not found for unmonitoring in any configured {ServiceTypes} instances",
                    filePath, GetServiceTypesString(contentType));
            }

            return anySuccess;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> TestAllConnectionsAsync(CancellationToken ct = default)
    {
        var allSuccessful = true;

        var allClients = _radarrClients.Cast<ArrClient>().Concat(_sonarrClients);

        // Combine the lists and test connections
        foreach (var client in allClients)
        {
            var success = await client.TestConnectionAsync(ct);
            if (!success)
            {
                allSuccessful = false;
            }
        }

        return allSuccessful;
    }

    public void RefreshClients()
    {
        // Dispose existing clients
        foreach (var client in _radarrClients)
        {
            client.Dispose();
        }
        foreach (var client in _sonarrClients)
        {
            client.Dispose();
        }

        _radarrClients.Clear();
        _sonarrClients.Clear();

        // Reinitialize with updated configuration
        InitializeClients();
    }

    private List<ArrClient> GetClients(ArrAppType appType)
    {
        var clients = new List<ArrClient>();
        var instanceCount = GetInstanceCount(appType);
        var lowerCaseAppType = appType.ToString().ToLowerInvariant();

        for (int i = 0; i < instanceCount; i++)
        {
            var name = _configManager.GetConfigValue($"{lowerCaseAppType}.{i}.name") ?? $"{appType}-{i}";
            var baseUrl = _configManager.GetConfigValue($"{lowerCaseAppType}.{i}.url");
            var apiKey = _configManager.GetConfigValue($"{lowerCaseAppType}.{i}.api_key");

            if (!string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(apiKey))
            {
                clients.Add(ArrClient.CreateClient(appType, baseUrl.TrimEnd('/'), apiKey, name));
            }
        }

        return clients;
    }

    private int GetInstanceCount(ArrAppType appType)
    {
        // Look for the highest numbered instance to determine count
        var maxIndex = -1;
        var lowerCaseAppType = appType.ToString().ToLowerInvariant();

        for (int i = 0; i < 10; i++) // Support up to 10 instances of each
        {
            var url = _configManager.GetConfigValue($"{lowerCaseAppType}.{i}.url");
            if (!string.IsNullOrEmpty(url))
            {
                maxIndex = i;
            }
        }

        return maxIndex + 1;
    }

    public List<string> GetConfiguredInstances()
    {
        var instances = new List<string>();

        instances.AddRange(_radarrClients.Select(c => $"Radarr: {c.InstanceName}"));
        instances.AddRange(_sonarrClients.Select(c => $"Sonarr: {c.InstanceName}"));

        return instances;
    }

    private ContentType DetectContentType(string filePath)
    {
        // Normalize the path for analysis
        var normalizedPath = filePath.Replace('\\', '/').ToLowerInvariant();

        // Common TV show patterns
        var tvPatterns = new[]
        {
            @"s\d{1,2}e\d{1,3}",     // S01E01 pattern
            @"season\s*\d+",          // Season 1 pattern
            @"\d{1,2}x\d{1,3}",      // 1x01 pattern
        };

        // Check for TV show patterns
        foreach (var pattern in tvPatterns)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(normalizedPath, pattern))
            {
                return ContentType.TvShow;
            }
        }

        // Default to movie if we can't determine it's a tv show
        return ContentType.Movie;
    }

    private string GetServiceTypesString(ContentType contentType)
    {
        return contentType switch
        {
            ContentType.Movie => "Radarr",
            ContentType.TvShow => "Sonarr",
            _ => "Radarr/Sonarr"
        };
    }

    public void Dispose()
    {
        foreach (var client in _radarrClients)
        {
            client.Dispose();
        }
        foreach (var client in _sonarrClients)
        {
            client.Dispose();
        }
        _semaphore?.Dispose();
    }
}