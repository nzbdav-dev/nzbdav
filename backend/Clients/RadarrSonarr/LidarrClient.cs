using System.Net;
using NzbWebDAV.Clients.RadarrSonarr.LidarrModels;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Clients.RadarrSonarr;

public class LidarrClient(string host, string apiKey) : ArrClient(host, apiKey)
{
    protected override string BasePath => "/api/v1";

    private static readonly Dictionary<string, int> ArtistPathToArtistIdCache = new();
    private static readonly Dictionary<string, int> TrackFilePathToTrackFileIdCache = new();

    public Task<List<LidarrArtist>> GetAllArtists() =>
        Get<List<LidarrArtist>>($"/artist");

    public Task<LidarrArtist> GetArtist(int artistId) =>
        Get<LidarrArtist>($"/artist/{artistId}");

    public Task<List<LidarrTrackFile>> GetAllTrackFiles(int artistId) =>
        Get<List<LidarrTrackFile>>($"/trackfile?artistId={artistId}");

    public Task<LidarrTrackFile> GetTrackFile(int trackFileId) =>
        Get<LidarrTrackFile>($"/trackfile/{trackFileId}");

    public Task<HttpStatusCode> DeleteTrackFile(int trackFileId) =>
        Delete($"/trackfile/{trackFileId}");

    public Task SearchArtistAsync(int artistId) =>
        CommandAsync(new { name = "ArtistSearch", artistId });

    public override async Task<bool> RemoveAndSearch(string symlinkOrStrmPath)
    {
        var mediaIds = await GetMediaIds(symlinkOrStrmPath);
        if (mediaIds == null) return false;

        if (await DeleteTrackFile(mediaIds.Value.trackFileId) != HttpStatusCode.OK)
            throw new Exception($"Failed to delete track file `{symlinkOrStrmPath}` from Lidarr instance `{Host}`.");

        await SearchArtistAsync(mediaIds.Value.artistId);
        return true;
    }

    private async Task<(int trackFileId, int artistId)?> GetMediaIds(string symlinkOrStrmPath)
    {
        // if track-file-id is cached, verify and return it
        if (TrackFilePathToTrackFileIdCache.TryGetValue(symlinkOrStrmPath, out var cachedTrackFileId))
        {
            var trackFile = await GetTrackFile(cachedTrackFileId);
            if (trackFile.Path == symlinkOrStrmPath)
                return (cachedTrackFileId, trackFile.ArtistId);
        }

        // find the artist whose root path is a prefix of the given file path
        var artistId = await GetArtistId(symlinkOrStrmPath);
        if (artistId == null) return null;

        // scan all track files for that artist and populate the cache
        int? result = null;
        foreach (var trackFile in await GetAllTrackFiles(artistId.Value))
        {
            if (trackFile.Path != null)
                TrackFilePathToTrackFileIdCache[trackFile.Path] = trackFile.Id;
            if (trackFile.Path == symlinkOrStrmPath)
                result = trackFile.Id;
        }

        return result == null ? null : (result.Value, artistId.Value);
    }

    private async Task<int?> GetArtistId(string symlinkOrStrmPath)
    {
        // check cache first using all parent directories
        var cachedArtistId = PathUtil.GetAllParentDirectories(symlinkOrStrmPath)
            .Where(p => ArtistPathToArtistIdCache.ContainsKey(p))
            .Select(p => ArtistPathToArtistIdCache[p])
            .Select(id => (int?)id)
            .FirstOrDefault();

        if (cachedArtistId != null)
        {
            var artist = await GetArtist(cachedArtistId.Value);
            if (artist.Path != null && symlinkOrStrmPath.StartsWith(artist.Path))
                return cachedArtistId;
        }

        // refresh all artists and repopulate cache
        int? result = null;
        foreach (var artist in await GetAllArtists())
        {
            if (artist.Path != null)
                ArtistPathToArtistIdCache[artist.Path] = artist.Id;
            if (artist.Path != null && symlinkOrStrmPath.StartsWith(artist.Path))
                result = artist.Id;
        }

        return result;
    }
}
