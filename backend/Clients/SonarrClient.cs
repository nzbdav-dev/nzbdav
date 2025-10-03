using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace NzbWebDAV.Clients;

public class SonarrClient : ArrClient
{
    private readonly ArrStore<SonarrSeries> _seriesStore;

    public SonarrClient(string baseUrl, string apiKey, string instanceName)
        : base(baseUrl, apiKey, instanceName)
    {
        _seriesStore = new ArrStore<SonarrSeries>(GetAllSeriesAsync, s => s.Path);
    }

    public override ArrAppType AppType => ArrAppType.Sonarr;

    public override async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var status = await GetAsync<SonarrSystemStatus>("/api/v3/system/status", ct);
            if (status != null)
            {
                Log.Information("Successfully connected to Sonarr instance '{InstanceName}' (v{Version})",
                    _instanceName, status.Version);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to test connection to Sonarr instance '{InstanceName}'", _instanceName);
            return false;
        }
    }

    public async Task<List<SonarrSeries>> GetAllSeriesAsync(CancellationToken ct = default)
    {
        var allSeries = await GetAsync<SonarrSeries[]>("/api/v3/series", ct);
        return allSeries?.ToList() ?? new List<SonarrSeries>();
    }

    public async Task<List<SonarrEpisodeFile>> GetEpisodeFilesForSeriesAsync(int seriesId, CancellationToken ct = default)
    {
        var episodeFiles = await GetAsync<SonarrEpisodeFile[]>($"/api/v3/episodefile?seriesId={seriesId}", ct);
        return episodeFiles?.ToList() ?? new List<SonarrEpisodeFile>();
    }

    public async Task<(List<SonarrEpisode> episodes, SonarrEpisodeFile? episodeFile)> GetEpisodesAndEpisodeFileForFilePathAsync(string filePath, CancellationToken ct = default)
    {
        var series = await _seriesStore.FindItemForPathAsync(filePath, ct);
        if (series == null)
        {
            return (new List<SonarrEpisode>(), null);
        }

        var episodeFileTrie = new ArrStore<SonarrEpisodeFile>(ct => GetEpisodeFilesForSeriesAsync(series.Id, ct), e => e.Path);
        var episodeFile = await episodeFileTrie.FindItemForPathAsync(filePath, ct);
        if (episodeFile == null)
        {
            return (new List<SonarrEpisode>(), null);
        }

        var episodes = await GetAsync<SonarrEpisode[]>($"/api/v3/episode?episodeFileId={episodeFile.Id}", ct);

        return (episodes?.ToList() ?? new List<SonarrEpisode>(), episodeFile);
    }

    public override async Task<(bool Success, List<int> Ids)> DeleteFileAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            var (episodes, episodeFile) = await GetEpisodesAndEpisodeFileForFilePathAsync(filePath, ct);
            if (episodes == null || episodes.Count == 0 || episodeFile == null)
            {
                return (false, new List<int>());
            }

            var episodeIds = episodes.Select(e => e.Id).ToList();

            // Delete the episode file
            var deleteEndpoint = $"/api/v3/episodefile/{episodeFile.Id}";
            var success = await DeleteAsync(deleteEndpoint, ct);

            if (success)
            {
                Log.Information("Successfully deleted episode file '{FilePath}' (ID: {FileId}) from Sonarr instance '{InstanceName}'",
                    filePath, episodeFile.Id, _instanceName);
                return (true, episodeIds);
            }
            else
            {
                Log.Warning("Failed to delete episode file '{FilePath}' (ID: {FileId}) from Sonarr instance '{InstanceName}'",
                    filePath, episodeFile.Id, _instanceName);
                return (false, episodeIds);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting file '{FilePath}' from Sonarr instance '{InstanceName}'",
                filePath, _instanceName);
            return (false, new List<int>());
        }
    }

    public override async Task<bool> MonitorFileAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            var (episodes, episodeFile) = await GetEpisodesAndEpisodeFileForFilePathAsync(filePath, ct);
            if (episodes == null || episodes.Count == 0 || episodeFile == null)
            {
                Log.Information("No episodes found with path prefix matching '{FilePath}' in Sonarr instance '{InstanceName}' for monitoring",
                    filePath, _instanceName);
                return false;
            }

            var success = true;

            foreach (var episode in episodes)
            {
                // Only monitor if currently unmonitored
                if (episode.Monitored)
                {
                    Log.Information("Episode '{Title}' S{Season:D2}E{Episode:D2} is already monitored in Sonarr instance '{InstanceName}'",
                        episode.Title, episode.SeasonNumber, episode.EpisodeNumber, _instanceName);
                    continue;
                }

                // Update the episode to monitor it
                var updatePayload = new
                {
                    id = episode.Id,
                    seriesId = episode.SeriesId,
                    seasonNumber = episode.SeasonNumber,
                    episodeNumber = episode.EpisodeNumber,
                    title = episode.Title,
                    hasFile = episode.HasFile,
                    episodeFileId = episode.EpisodeFileId,
                    monitored = true // This is the field we're changing
                };

                Log.Debug("Attempting to monitor episode '{Title}' S{Season:D2}E{Episode:D2} (ID: {EpisodeId}) with payload: {Payload}",
                    episode.Title, episode.SeasonNumber, episode.EpisodeNumber, episode.Id, System.Text.Json.JsonSerializer.Serialize(updatePayload));

                var result = await PutAsync<object>($"/api/v3/episode/{episode.Id}", updatePayload, ct);
                if (result != null)
                {
                    Log.Information("Successfully monitored episode '{Title}' S{Season:D2}E{Episode:D2} (ID: {EpisodeId}) in Sonarr instance '{InstanceName}' for re-download after corruption",
                        episode.Title, episode.SeasonNumber, episode.EpisodeNumber, episode.Id, _instanceName);
                }
                else
                {
                    Log.Warning("Failed to monitor episode '{Title}' S{Season:D2}E{Episode:D2} (ID: {EpisodeId}) in Sonarr instance '{InstanceName}' - no response",
                        episode.Title, episode.SeasonNumber, episode.EpisodeNumber, episode.Id, _instanceName);
                    success = false;
                }
            }
            return success;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error monitoring episodes for file '{FilePath}' in Sonarr instance '{InstanceName}'",
                filePath, _instanceName);
            return false;
        }
    }

    public override async Task<bool> UnmonitorFileAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            var (episodes, episodeFile) = await GetEpisodesAndEpisodeFileForFilePathAsync(filePath, ct);
            if (episodes == null || episodes.Count == 0 || episodeFile == null)
            {
                Log.Information("No episodes found with path prefix matching '{FilePath}' in Sonarr instance '{InstanceName}' for unmonitoring",
                    filePath, _instanceName);
                return false;
            }

            var success = true;

            foreach (var episode in episodes)
            {
                // Only unmonitor if currently monitored
                if (!episode.Monitored)
                {
                    Log.Information("Episode '{Title}' S{Season:D2}E{Episode:D2} is already unmonitored in Sonarr instance '{InstanceName}'",
                            episode.Title, episode.SeasonNumber, episode.EpisodeNumber, _instanceName);
                    continue;
                }

                // Update the episode to unmonitor it
                var updatePayload = new
                {
                    id = episode.Id,
                    seriesId = episode.SeriesId,
                    seasonNumber = episode.SeasonNumber,
                    episodeNumber = episode.EpisodeNumber,
                    title = episode.Title,
                    monitored = false // This is the field we're changing
                };

                var result = await PutAsync<object>($"/api/v3/episode/{episode.Id}", updatePayload, ct);
                if (result != null)
                {
                    Log.Information("Successfully unmonitored episode '{Title}' S{Season:D2}E{Episode:D2} in Sonarr instance '{InstanceName}' after integrity check",
                        episode.Title, episode.SeasonNumber, episode.EpisodeNumber, _instanceName);
                }
                else
                {
                    Log.Warning("Failed to unmonitor episode '{Title}' S{Season:D2}E{Episode:D2} in Sonarr instance '{InstanceName}' - no response",
                    episode.Title, episode.SeasonNumber, episode.EpisodeNumber, _instanceName);
                    success = false;
                }
            }

            return success;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error unmonitoring episodes for file '{FilePath}' in Sonarr instance '{InstanceName}'",
                filePath, _instanceName);
            return false;
        }
    }

    public override async Task<bool> TriggerSearchByIdAsync(int id, CancellationToken ct = default)
    {
        try
        {
            var command = new
            {
                name = "EpisodeSearch",
                episodeIds = new[] { id }
            };

            var result = await PostAsync<object>("/api/v3/command", command, ct);
            if (result != null)
            {
                Log.Information("Successfully triggered search for episode ID {EpisodeId} in Sonarr instance '{InstanceName}'",
                    id, _instanceName);
                return true;
            }

            Log.Warning("Failed to trigger search for episode ID {EpisodeId} in Sonarr instance '{InstanceName}' - no response",
                id, _instanceName);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error triggering search for episode ID {EpisodeId} in Sonarr instance '{InstanceName}'",
                id, _instanceName);
            return false;
        }
    }
}

public class SonarrSystemStatus
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

public class SonarrSeries
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("sortTitle")]
    public string SortTitle { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("network")]
    public string Network { get; set; } = string.Empty;

    [JsonPropertyName("airTime")]
    public string AirTime { get; set; } = string.Empty;

    [JsonPropertyName("images")]
    public SonarrImage[]? Images { get; set; }

    [JsonPropertyName("seasons")]
    public SonarrSeason[]? Seasons { get; set; }

    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("tvdbId")]
    public int TvdbId { get; set; }

    [JsonPropertyName("tvMazeId")]
    public int TvMazeId { get; set; }

    [JsonPropertyName("imdbId")]
    public string ImdbId { get; set; } = string.Empty;

    [JsonPropertyName("titleSlug")]
    public string TitleSlug { get; set; } = string.Empty;

    [JsonPropertyName("monitored")]
    public bool Monitored { get; set; }

    [JsonPropertyName("useSceneNumbering")]
    public bool UseSceneNumbering { get; set; }

    [JsonPropertyName("runtime")]
    public int Runtime { get; set; }

    [JsonPropertyName("seriesType")]
    public string SeriesType { get; set; } = string.Empty;

    [JsonPropertyName("cleanTitle")]
    public string CleanTitle { get; set; } = string.Empty;

    [JsonPropertyName("languageProfileId")]
    public int LanguageProfileId { get; set; }

    [JsonPropertyName("genres")]
    public string[]? Genres { get; set; }

    [JsonPropertyName("tags")]
    public int[]? Tags { get; set; }

    [JsonPropertyName("added")]
    public DateTime Added { get; set; }

    [JsonPropertyName("statistics")]
    public SonarrSeriesStatistics? Statistics { get; set; }
}

public class SonarrEpisode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("seriesId")]
    public int SeriesId { get; set; }

    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("episodeNumber")]
    public int EpisodeNumber { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("hasFile")]
    public bool HasFile { get; set; }

    [JsonPropertyName("episodeFileId")]
    public int? EpisodeFileId { get; set; }

    [JsonPropertyName("monitored")]
    public bool Monitored { get; set; }
}

public class SonarrEpisodeFile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("seriesId")]
    public int SeriesId { get; set; }

    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("dateAdded")]
    public DateTime DateAdded { get; set; }

    [JsonPropertyName("quality")]
    public SonarrQuality? Quality { get; set; }

    [JsonPropertyName("mediaInfo")]
    public SonarrMediaInfo? MediaInfo { get; set; }

    [JsonPropertyName("originalFilePath")]
    public string OriginalFilePath { get; set; } = string.Empty;

    [JsonPropertyName("sceneName")]
    public string SceneName { get; set; } = string.Empty;

    [JsonPropertyName("releaseGroup")]
    public string ReleaseGroup { get; set; } = string.Empty;

    [JsonPropertyName("edition")]
    public string Edition { get; set; } = string.Empty;

    [JsonPropertyName("languages")]
    public SonarrLanguage[]? Languages { get; set; }
}

public class SonarrImage
{
    [JsonPropertyName("coverType")]
    public string CoverType { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("remoteUrl")]
    public string RemoteUrl { get; set; } = string.Empty;
}

public class SonarrSeason
{
    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("monitored")]
    public bool Monitored { get; set; }

    [JsonPropertyName("statistics")]
    public SonarrSeasonStatistics? Statistics { get; set; }
}

public class SonarrSeriesStatistics
{
    [JsonPropertyName("seasonCount")]
    public int SeasonCount { get; set; }

    [JsonPropertyName("episodeFileCount")]
    public int EpisodeFileCount { get; set; }

    [JsonPropertyName("episodeCount")]
    public int EpisodeCount { get; set; }

    [JsonPropertyName("totalEpisodeCount")]
    public int TotalEpisodeCount { get; set; }

    [JsonPropertyName("sizeOnDisk")]
    public long SizeOnDisk { get; set; }

    [JsonPropertyName("percentOfEpisodes")]
    public double PercentOfEpisodes { get; set; }
}

public class SonarrSeasonStatistics
{
    [JsonPropertyName("episodeFileCount")]
    public int EpisodeFileCount { get; set; }

    [JsonPropertyName("episodeCount")]
    public int EpisodeCount { get; set; }

    [JsonPropertyName("totalEpisodeCount")]
    public int TotalEpisodeCount { get; set; }

    [JsonPropertyName("sizeOnDisk")]
    public long SizeOnDisk { get; set; }

    [JsonPropertyName("percentOfEpisodes")]
    public double PercentOfEpisodes { get; set; }
}

public class SonarrQuality
{
    [JsonPropertyName("quality")]
    public SonarrQualityInfo? QualityInfo { get; set; }

    [JsonPropertyName("revision")]
    public SonarrRevision? Revision { get; set; }
}

public class SonarrQualityInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("resolution")]
    public int Resolution { get; set; }
}

public class SonarrRevision
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("real")]
    public int Real { get; set; }

    [JsonPropertyName("isRepack")]
    public bool IsRepack { get; set; }
}

public class SonarrMediaInfo
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

    [JsonPropertyName("runTime")]
    public string RunTime { get; set; } = string.Empty;
}

public class SonarrLanguage
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
