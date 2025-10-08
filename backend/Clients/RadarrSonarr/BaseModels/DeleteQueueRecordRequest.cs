using NzbWebDAV.Config;

namespace NzbWebDAV.Clients.RadarrSonarr.BaseModels;

// ReSharper disable IdentifierTypo
public class DeleteQueueRecordRequest
{
    private bool RemoveFromClient { get; }
    private bool Blocklist { get; }
    private bool SkipRedownload { get; }

    // ReSharper disable once ConvertToPrimaryConstructor
    public DeleteQueueRecordRequest(ArrConfig.QueueAction queueAction)
    {
        RemoveFromClient = queueAction >= ArrConfig.QueueAction.Remove;
        Blocklist = queueAction >= ArrConfig.QueueAction.RemoveAndBlocklist;
        SkipRedownload = !(queueAction >= ArrConfig.QueueAction.RemoveAndBlocklistAndSearch);
    }

    public Dictionary<string, string> GetQueryParams()
    {
        return new Dictionary<string, string>()
        {
            { "removeFromClient", RemoveFromClient.ToString().ToLower() },
            { "blocklist", Blocklist.ToString().ToLower() },
            { "skipRedownload", SkipRedownload.ToString().ToLower() },
        };
    }
}