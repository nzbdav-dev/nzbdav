namespace NzbWebDAV.Services.StreamTrace;

public enum StreamTraceKind
{
    RangeOpen = 0,
    Seek = 1,
    Segment = 2,
    ZeroFill = 3,
    Failover = 4,
    RangeEnd = 5,
    Retry = 6,
    PrefetchWidth = 7,
}
