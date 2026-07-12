using NzbWebDAV.Services;

namespace NzbWebDAV.Clients.Usenet.Contexts;

public record SpeedTrackingContext
{
    public required DownloadSpeedSession Session { get; init; }
}
