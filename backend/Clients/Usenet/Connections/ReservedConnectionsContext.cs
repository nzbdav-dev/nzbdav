namespace NzbWebDAV.Clients.Usenet.Connections;

public readonly struct ReservedConnectionsContext(int reservedCount)
{
    public int Count => reservedCount;
}