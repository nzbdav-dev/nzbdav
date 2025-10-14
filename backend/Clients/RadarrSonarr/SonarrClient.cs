using NzbWebDAV.Clients.RadarrSonarr.SonarrModels;

namespace NzbWebDAV.Clients.RadarrSonarr;

public class SonarrClient(string host, string apiKey) : ArrClient(host, apiKey)
{
    public Task<SonarrQueue> GetSonarrQueueAsync() =>
        Get<SonarrQueue>($"/queue?protocol=usenet&pageSize=5000");

    public override Task<bool> RemoveAndSearch(string symlinkPath)
    {
        return Task.FromResult(true);
    }
}