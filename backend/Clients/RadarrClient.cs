using System.Text.Json.Serialization;
using Serilog;

namespace NzbWebDAV.Clients;

public class RadarrClient : ArrClient
{
    private readonly ArrStore<RadarrMovie> _movieStore;

    public RadarrClient(string baseUrl, string apiKey, string instanceName)
        : base(baseUrl, apiKey, instanceName)
    {
        _movieStore = new ArrStore<RadarrMovie>(GetAllMoviesAsync, m => m.Path);
    }

    public override ArrAppType AppType => ArrAppType.Radarr;

    public async Task<List<RadarrMovie>> GetAllMoviesAsync(CancellationToken ct = default)
    {
        var allMovies = await GetAsync<RadarrMovie[]>("/api/v3/movie", ct);
        return allMovies?.ToList() ?? new List<RadarrMovie>();
    }

    public override async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var status = await GetAsync<RadarrSystemStatus>("/api/v3/system/status", ct);
            if (status != null)
            {
                Log.Information("Successfully connected to Radarr instance '{InstanceName}' (v{Version})",
                    _instanceName, status.Version);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to test connection to Radarr instance '{InstanceName}'", _instanceName);
            return false;
        }
    }

    public override async Task<(bool Success, List<int> Ids)> DeleteFileAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            // First, find the movie that contains this file
            var movie = await _movieStore.FindItemForPathAsync(filePath, ct);

            if (movie == null)
            {
                Log.Warning("Could not find movie with file path '{FilePath}' in Radarr instance '{InstanceName}' using any matching strategy",
                    filePath, _instanceName);
                return (false, new List<int>());
            }

            // Delete the movie file
            if (movie.MovieFile?.Id != null)
            {
                var deleteEndpoint = $"/api/v3/moviefile/{movie.MovieFile.Id}";
                var success = await DeleteAsync(deleteEndpoint, ct);

                if (success)
                {
                    Log.Information("Successfully deleted movie file '{FilePath}' (ID: {FileId}) from Radarr instance '{InstanceName}'",
                        filePath, movie.MovieFile.Id, _instanceName);
                    return (true, new List<int> { movie.Id });
                }
                else
                {
                    Log.Warning("Failed to delete movie file '{FilePath}' (ID: {FileId}) from Radarr instance '{InstanceName}'",
                        filePath, movie.MovieFile.Id, _instanceName);
                    return (false, new List<int> { movie.Id });
                }
            }

            Log.Warning("Movie found but no file ID available for '{FilePath}' in Radarr instance '{InstanceName}'",
                filePath, _instanceName);
            return (false, new List<int> { movie.Id });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting file '{FilePath}' from Radarr instance '{InstanceName}'",
                filePath, _instanceName);
            return (false, new List<int>());
        }
    }

    public override async Task<bool> TriggerSearchByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var command = new
            {
                name = "MoviesSearch",
                movieIds = new[] { id }
            };

            var result = await PostAsync<object>("/api/v3/command", command, ct);
            if (result != null)
            {
                Log.Information("Successfully triggered search for movie ID {MovieId} in Radarr instance '{InstanceName}'",
                    id, _instanceName);
                return true;
            }

            Log.Warning("Failed to trigger search for movie ID {MovieId} in Radarr instance '{InstanceName}' - no response",
                id, _instanceName);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error triggering search for movie ID {MovieId} in Radarr instance '{InstanceName}'",
                id, _instanceName);
            return false;
        }
    }

    public override async Task<bool> UnmonitorFileAsync(string filePath, CancellationToken ct = default)
    {
        Log.Information("RadarrClient.UnmonitorFileAsync called for file: '{FilePath}' on instance '{InstanceName}'", filePath, _instanceName);
        try
        {
            // First, find the movie that contains this file
            var movie = await _movieStore.FindItemForPathAsync(filePath, ct);
            if (movie == null)
            {
                Log.Warning("Failed to retrieve movies from Radarr instance '{InstanceName}' for unmonitoring", _instanceName);
                return false;
            }

            // Only unmonitor if currently monitored
            if (!movie.Monitored)
            {
                Log.Information("Movie '{Title}' is already unmonitored in Radarr instance '{InstanceName}'",
                    movie.Title, _instanceName);
                return true; // Already unmonitored, consider this success
            }

            // Update the movie to unmonitor it - send the complete movie object with monitored = false
            var updatePayload = new
            {
                id = movie.Id,
                title = movie.Title,
                originalTitle = movie.OriginalTitle,
                sortTitle = movie.SortTitle,
                tmdbId = movie.TmdbId,
                imdbId = movie.ImdbId,
                year = movie.Year,
                path = movie.Path,
                monitored = false, // This is the field we're changing
                hasFile = movie.HasFile,
                movieFile = movie.MovieFile
            };

            var result = await PutAsync<object>($"/api/v3/movie/{movie.Id}", updatePayload, ct);
            if (result != null)
            {
                Log.Information("Successfully unmonitored movie '{Title}' (ID: {MovieId}) in Radarr instance '{InstanceName}' after integrity check",
                    movie.Title, movie.Id, _instanceName);
                return true;
            }

            Log.Warning("Failed to unmonitor movie '{Title}' (ID: {MovieId}) in Radarr instance '{InstanceName}' - no response",
                movie.Title, movie.Id, _instanceName);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error unmonitoring movie for file '{FilePath}' in Radarr instance '{InstanceName}'",
                filePath, _instanceName);
            return false;
        }
    }

    public override async Task<bool> MonitorFileAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            // First, find the movie that contains this file
            var movie = await _movieStore.FindItemForPathAsync(filePath, ct);
            if (movie == null)
            {
                Log.Warning("Could not find movie with file path '{FilePath}' in Radarr instance '{InstanceName}' for monitoring",
                    filePath, _instanceName);
                return false;
            }

            // Only monitor if currently unmonitored
            if (movie.Monitored)
            {
                Log.Debug("Movie '{Title}' is already monitored in Radarr instance '{InstanceName}'",
                    movie.Title, _instanceName);
                return true; // Already monitored, consider this success
            }

            // Update the movie to monitor it - send the complete movie object with monitored = true
            var updatePayload = new
            {
                id = movie.Id,
                title = movie.Title,
                originalTitle = movie.OriginalTitle,
                sortTitle = movie.SortTitle,
                tmdbId = movie.TmdbId,
                imdbId = movie.ImdbId,
                year = movie.Year,
                path = movie.Path,
                monitored = true, // This is the field we're changing
                hasFile = movie.HasFile,
                movieFile = movie.MovieFile
            };

            Log.Debug("Attempting to monitor movie '{Title}' (ID: {MovieId}) with payload: {Payload}",
                movie.Title, movie.Id, System.Text.Json.JsonSerializer.Serialize(updatePayload));

            var result = await PutAsync<object>($"/api/v3/movie/{movie.Id}", updatePayload, ct);
            if (result != null)
            {
                Log.Information("Successfully monitored movie '{Title}' (ID: {MovieId}) in Radarr instance '{InstanceName}' for re-download after corruption",
                    movie.Title, movie.Id, _instanceName);
                return true;
            }

            Log.Warning("Failed to monitor movie '{Title}' (ID: {MovieId}) in Radarr instance '{InstanceName}' - no response",
                movie.Title, movie.Id, _instanceName);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error monitoring movie for file '{FilePath}' in Radarr instance '{InstanceName}'",
                filePath, _instanceName);
            return false;
        }
    }
}

public class RadarrSystemStatus
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("buildTime")]
    public DateTime BuildTime { get; set; }

    [JsonPropertyName("isDebug")]
    public bool IsDebug { get; set; }

    [JsonPropertyName("isProduction")]
    public bool IsProduction { get; set; }

    [JsonPropertyName("isAdmin")]
    public bool IsAdmin { get; set; }

    [JsonPropertyName("isUserInteractive")]
    public bool IsUserInteractive { get; set; }

    [JsonPropertyName("startupPath")]
    public string StartupPath { get; set; } = string.Empty;

    [JsonPropertyName("appData")]
    public string AppData { get; set; } = string.Empty;
}

public class RadarrMovie
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("originalTitle")]
    public string OriginalTitle { get; set; } = string.Empty;

    [JsonPropertyName("sortTitle")]
    public string SortTitle { get; set; } = string.Empty;

    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }

    [JsonPropertyName("imdbId")]
    public string ImdbId { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("movieFile")]
    public RadarrMovieFile? MovieFile { get; set; }

    [JsonPropertyName("monitored")]
    public bool Monitored { get; set; }

    [JsonPropertyName("hasFile")]
    public bool HasFile { get; set; }
}

public class RadarrMovieFile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("movieId")]
    public int MovieId { get; set; }

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("dateAdded")]
    public DateTime DateAdded { get; set; }

    [JsonPropertyName("quality")]
    public RadarrQuality? Quality { get; set; }

    [JsonPropertyName("mediaInfo")]
    public RadarrMediaInfo? MediaInfo { get; set; }
}

public class RadarrQuality
{
    [JsonPropertyName("quality")]
    public RadarrQualityInfo? QualityInfo { get; set; }

    [JsonPropertyName("revision")]
    public RadarrRevision? Revision { get; set; }
}

public class RadarrQualityInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class RadarrRevision
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("real")]
    public int Real { get; set; }

    [JsonPropertyName("isRepack")]
    public bool IsRepack { get; set; }
}

public class RadarrMediaInfo
{
    [JsonPropertyName("videoCodec")]
    public string VideoCodec { get; set; } = string.Empty;

    [JsonPropertyName("audioCodec")]
    public string AudioCodec { get; set; } = string.Empty;

    [JsonPropertyName("audioChannels")]
    public double AudioChannels { get; set; }

    [JsonPropertyName("audioLanguages")]
    public string AudioLanguages { get; set; } = string.Empty;

    [JsonPropertyName("subtitles")]
    public string Subtitles { get; set; } = string.Empty;
}
