using System.Net;
using NzbWebDAV.Clients.RadarrSonarr.BaseModels;
using NzbWebDAV.Clients.RadarrSonarr.SonarrModels;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Clients.RadarrSonarr;

public class SonarrClient(string host, string apiKey) : ArrClient(host, apiKey)
{
    private static readonly Dictionary<string, int> SeriesPathToSeriesIdCache = new();
    private static readonly Dictionary<string, int> SymlinkOrStrmToEpisodeFileIdCache = new();

    public Task<SonarrQueue> GetSonarrQueueAsync() =>
        Get<SonarrQueue>($"/queue?protocol=usenet&pageSize=5000");

    public Task<List<SonarrSeries>> GetAllSeries() =>
        Get<List<SonarrSeries>>($"/series");

    public Task<SonarrSeries> GetSeries(int seriesId) =>
        Get<SonarrSeries>($"/series/{seriesId}");

    public Task<SonarrEpisodeFile> GetEpisodeFile(int episodeFileId) =>
        Get<SonarrEpisodeFile>($"/episodefile/{episodeFileId}");

    public Task<List<SonarrEpisodeFile>> GetAllEpisodeFiles(int seriesId) =>
        Get<List<SonarrEpisodeFile>>($"/episodefile?seriesId={seriesId}");

    public Task<List<SonarrEpisode>> GetEpisodesFromEpisodeFileId(int episodeFileId) =>
        Get<List<SonarrEpisode>>($"/episode?episodeFileId={episodeFileId}");

    public Task<HttpStatusCode> DeleteEpisodeFile(int episodeFileId) =>
        Delete($"/episodefile/{episodeFileId}");

    public Task<ArrCommand> SearchEpisodesAsync(List<int> episodeIds) =>
        CommandAsync(new { name = "EpisodeSearch", episodeIds });

    public override async Task<bool> RemoveAndSearch(string symlinkOrStrmPath)
    {
        // get episode-file-id and episode-ids
        var mediaIds = await GetMediaIds(symlinkOrStrmPath);
        if (mediaIds == null) return false;

        // delete the episode-file
        if (await DeleteEpisodeFile(mediaIds.Value.episodeFileId) != HttpStatusCode.OK)
            throw new Exception($"Failed to delete episode file `{symlinkOrStrmPath}` from sonarr instance `{Host}`.");

        // trigger a new search for each episode
        await SearchEpisodesAsync(mediaIds.Value.episodeIds);
        return true;
    }

    private async Task<(int episodeFileId, List<int> episodeIds)?> GetMediaIds(string symlinkOrStrmPath)
    {
        // get episode-file-id
        var episodeFileId = await GetEpisodeFileId(symlinkOrStrmPath);
        if (episodeFileId == null) return null;

        // get episode-ids
        var episodes = await GetEpisodesFromEpisodeFileId(episodeFileId.Value);
        var episodeIds = episodes.Select(x => x.Id).ToList();
        if (episodeIds.Count == 0) return null;

        // return
        return (episodeFileId.Value, episodeIds);
    }

    private async Task<int?> GetEpisodeFileId(string symlinkOrStrmPath)
    {
        // if episode-file-id is found in the cache, verify it and return it
        if (SymlinkOrStrmToEpisodeFileIdCache.TryGetValue(symlinkOrStrmPath, out var episodeFileId))
        {
            var episodeFile = await GetEpisodeFile(episodeFileId);
            if (episodeFile.Path == symlinkOrStrmPath) return episodeFileId;
        }

        // otherwise, find the series-id
        var seriesId = await GetSeriesId(symlinkOrStrmPath);
        if (seriesId == null) return null;

        // then use it to find all episode-files and repopulate the cache
        int? result = null;
        foreach (var episodeFile in await GetAllEpisodeFiles(seriesId.Value))
        {
            SymlinkOrStrmToEpisodeFileIdCache[episodeFile.Path!] = episodeFile.Id;
            if (episodeFile.Path == symlinkOrStrmPath)
                result = episodeFile.Id;
        }

        // return the found episode-file-id
        return result;
    }

    private async Task<int?> GetSeriesId(string symlinkOrStrmPath)
    {
        // get series-id from cache
        var cachedSeriesId = PathUtil.GetAllParentDirectories(symlinkOrStrmPath)
            .Where(x => SeriesPathToSeriesIdCache.ContainsKey(x))
            .Select(x => SeriesPathToSeriesIdCache[x])
            .Select(x => (int?)x)
            .FirstOrDefault();

        // if found, verify and return it
        if (cachedSeriesId != null)
        {
            var series = await GetSeries(cachedSeriesId.Value);
            if (symlinkOrStrmPath.StartsWith(series.Path!))
                return cachedSeriesId;
        }

        // otherwise, fetch all series and repopulate the cache
        int? result = null;
        foreach (var series in await GetAllSeries())
        {
            SeriesPathToSeriesIdCache[series.Path!] = series.Id;
            if (symlinkOrStrmPath.StartsWith(series.Path!))
                result = series.Id;
        }

        // return the found series-id
        return result;
    }
}