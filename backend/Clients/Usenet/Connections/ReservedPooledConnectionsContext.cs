namespace NzbWebDAV.Clients.Usenet.Connections;

public readonly struct ReservedPooledConnectionsContext(int reservedCount)
{
    public readonly int Count = Math.Max(reservedCount, 0);
}