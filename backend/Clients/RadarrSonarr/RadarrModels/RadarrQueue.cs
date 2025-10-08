using NzbWebDAV.Clients.RadarrSonarr.BaseModels;

namespace NzbWebDAV.Clients.RadarrSonarr.RadarrModels;

public class RadarrQueue: ArrQueue<RadarrQueueRecord>
{
    public ArrQueue<ArrQueueRecord> ToGeneric()
    {
        return new ArrQueue<ArrQueueRecord>()
        {
            Page = Page,
            PageSize = PageSize,
            TotalRecords = TotalRecords,
            Records = Records.Select(ArrQueueRecord (x) => x).ToList()
        };
    }
}