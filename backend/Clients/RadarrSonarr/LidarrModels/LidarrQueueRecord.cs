using System.Text.Json.Serialization;
using NzbWebDAV.Clients.RadarrSonarr.BaseModels;

namespace NzbWebDAV.Clients.RadarrSonarr.LidarrModels;

public class LidarrQueueRecord : ArrQueueRecord
{
    [JsonPropertyName("artistId")]
    public int ArtistId { get; set; }

    [JsonPropertyName("albumId")]
    public int AlbumId { get; set; }
}
